import Foundation
import Network

/// Everything the Mac and the iPhone agree on: the Bonjour service name,
/// what a "face update" looks like, and how lines of JSON travel over TCP.
public enum GooglyService {
    public static let type = "_googly._tcp"
    /// Version of the packet vocabulary below. Sent as `proto` in each side's `hello`; bump it when
    /// a change would make an older peer misread a packet (adding optional fields does not need it).
    public static let protocolVersion = 1
}

public enum Mood: String, Codable, CaseIterable, Sendable {
    case listening, resting, thinking, talking, pointing, happy, sleepy

    public var title: String { rawValue.capitalized }
}

/// What the phone should show right now.
public struct FaceState: Codable, Equatable, Sendable {
    /// Where the eyes look. x: -1 left … 1 right. y: -1 up … 1 down.
    public var gazeX: Double
    public var gazeY: Double
    public var mood: Mood
    /// Voice level, 0…1. Drives the talking bounce.
    public var talk: Double

    public init(gazeX: Double = 0, gazeY: Double = 0, mood: Mood = .listening, talk: Double = 0) {
        self.gazeX = gazeX
        self.gazeY = gazeY
        self.mood = mood
        self.talk = talk
    }
}

/// The request and event names that travel in `Packet.command`.
///
/// Requests from the phone are answered by the Mac with the same `callID`. The replies to
/// `realtimeToken` and `notes` reuse the request's name; the others get their own.
public enum Command: String, CaseIterable, Sendable {
    // Mac → phone
    /// Wake him up, as if the phone were double tapped.
    case wake
    /// Put him to sleep.
    case sleep

    // Phone → Mac, answered with the same `callID`
    /// Ask for a Realtime client secret. Reply: `realtimeToken` with the secret in `text`.
    case realtimeToken
    /// Transcribe a recorded chunk (`audio` base64 AAC, `text` metadata JSON). Reply: `transcribed`.
    case transcribe
    /// Save the session transcripts as notes files (`text` JSON). Reply: `notes` with the folder path in `text`.
    case notes
    /// Run a tool (`tool` name, `text` JSON arguments). Reply: `toolResult` with `text` and maybe `image`.
    case tool

    // Phone → Mac, no reply
    /// His reply, streaming in (`text` so far).
    case caption
    /// His reply is complete (`text` is all of it).
    case captionDone
    /// The Realtime session is up.
    case awake
    /// The Realtime session ended.
    case asleep

    // Mac → phone, replies
    case toolResult
    case transcribed
}

/// One line of JSON on the wire. Every field is optional and unknown keys are ignored, so the two
/// apps can add fields without breaking an older peer.
public struct Packet: Codable, Sendable {
    /// What the phone should show right now (Mac → phone).
    public var face: FaceState?
    /// Sent once by each side after connecting, with a device name.
    public var hello: String?
    /// Version of the packet vocabulary (`GooglyService.protocolVersion`). Sent with `hello`;
    /// nil from a peer that predates it.
    public var proto: Int?
    /// What this packet is or asks for. Kept as the raw string so a name from a newer peer still
    /// decodes; read it through `kind`, or write it with `Packet(command: .wake)`.
    public var command: String?
    /// AAC audio (base64) for `transcribe`.
    public var audio: String?
    /// Pairs a request with its reply.
    public var callID: String?
    /// Tool name for a `tool` request.
    public var tool: String?
    /// Free text: tool arguments or output, a token, a caption, notes JSON.
    public var text: String?
    /// A JPEG (base64) that goes with a tool result.
    public var image: String?
    /// Set on a reply when the request failed, so a failure can be told apart from an empty
    /// success (`text == nil`). Nil means no error.
    public var error: String?

    public init(face: FaceState? = nil, hello: String? = nil, proto: Int? = nil, command: String? = nil,
                audio: String? = nil, callID: String? = nil, tool: String? = nil,
                text: String? = nil, image: String? = nil, error: String? = nil) {
        self.face = face
        self.hello = hello
        // A hello always says which vocabulary it speaks, even from callers that don't pass `proto`.
        self.proto = proto ?? (hello != nil ? GooglyService.protocolVersion : nil)
        self.command = command
        self.audio = audio
        self.callID = callID
        self.tool = tool
        self.text = text
        self.image = image
        self.error = error
    }

    /// Same as the main initializer, with the command as a `Command`.
    public init(command: Command, audio: String? = nil, callID: String? = nil, tool: String? = nil,
                text: String? = nil, image: String? = nil, error: String? = nil) {
        self.init(command: command.rawValue, audio: audio, callID: callID, tool: tool,
                  text: text, image: image, error: error)
    }

    /// `command` as a `Command`, or nil when there is none or this build doesn't know the name.
    public var kind: Command? {
        command.flatMap(Command.init(rawValue:))
    }
}

public extension NWParameters {
    static var googly: NWParameters {
        let tcp = NWProtocolTCP.Options()
        tcp.noDelay = true
        tcp.enableKeepalive = true
        tcp.keepaliveIdle = 2
        let params = NWParameters(tls: nil, tcp: tcp)
        params.includePeerToPeer = true
        return params
    }
}

/// Newline-delimited JSON over one NWConnection. All callbacks run on the main queue.
public final class LineConnection {
    public let connection: NWConnection
    public var onPacket: ((Packet) -> Void)?
    public var onState: ((NWConnection.State) -> Void)?

    private var buffer = Data()
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    public init(_ connection: NWConnection) {
        self.connection = connection
    }

    public func start() {
        connection.stateUpdateHandler = { [weak self] state in
            self?.onState?(state)
        }
        connection.start(queue: .main)
        receive()
    }

    public func send(_ packet: Packet) {
        guard var data = try? encoder.encode(packet) else { return }
        data.append(0x0A)
        connection.send(content: data, completion: .contentProcessed { _ in })
    }

    public func cancel() {
        connection.cancel()
    }

    private func receive() {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 1024 * 1024) { [weak self] data, _, isComplete, error in
            guard let self else { return }
            if let data, !data.isEmpty {
                self.buffer.append(data)
                while let newline = self.buffer.firstIndex(of: 0x0A) {
                    let line = self.buffer[self.buffer.startIndex..<newline]
                    self.buffer.removeSubrange(self.buffer.startIndex...newline)
                    if let packet = try? self.decoder.decode(Packet.self, from: line) {
                        self.onPacket?(packet)
                    }
                }
            }
            if isComplete || error != nil {
                self.connection.cancel()
            } else {
                self.receive()
            }
        }
    }
}
