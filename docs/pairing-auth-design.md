# Pairing auth for Bluey

Proposal only. Nothing here is implemented. The human picks one option before anyone changes `Shared/`, `Mac/`, or `iOS/`.

Reviewed against `agents/scout-review` at `0e24249`. The socket is `NWParameters.googly` in `Shared/GooglyLink.swift`: TCP, `tls: nil`, Bonjour `_googly._tcp`. Each side's first message is `Packet.hello` with a device name. `PhoneServer` then accepts `tool`, `realtimeToken`, `transcribe`, and `notes` from any peer. All four reviews flagged this.

## What has to be protected

Anyone on the same Wi-Fi can open the Bonjour port today. That peer can click and type on the Mac (`tool`, and computer control defaults to on), mint an OpenAI client secret (`realtimeToken`), spend the API key on transcription (`transcribe`), and write meeting-note files (`notes`).

The same socket later carries the client secret, screen JPEGs, and five-minute AAC chunks. An on-path listener (open Wi-Fi, a malicious access point) can read those even after a one-time login, unless the bytes are encrypted.

A paired phone is still fully trusted. This design does not decide which tools that phone may call. Awake-gating, the computer-control default, shortcut denylist, and session-id paths stay separate tasks.

## Facts the options share

- Discovery stays Bonjour. The service name is not a secret. `Host.current().localizedName` also changes when the Mac is renamed, so a paired secret should be stored under a stable id, a random UUID the Mac creates once and publishes in the Bonjour TXT record (`id`). The TXT record is public.
- The pairing secret is generated on the Mac (`SecRandomCopyBytes`, 16 bytes). It is shown once and stored in the **real** Keychain on both sides. `Mac/Sources/GooglyMac/Keychain.swift` writes `~/Library/Application Support/Googly/keys.json` in plaintext. The pairing secret does not go in that file.
- A 6-digit code is too small. A captured handshake can be guessed offline. The secret is the full 16 bytes, shown as Crockford base32 in groups of four (26 characters). The user types it once. A QR of the same string can come later. A QR needs `NSCameraUsageDescription` and a scanner, so it is not required for the first version.
- One household secret is enough. Several phones can use it. "New pairing code" in the Mac menu replaces it and drops current connections. Per-phone keys can wait.
- Old apps will fail closed. A mismatch must show a sentence on the phone ("This Mac needs the pairing code"). It must not fall through to plaintext, and it must not reuse `MacLink`'s 1.5-second `.waiting` retry as the only feedback.
- `Packet` can stay as it is. `hello` stays a display name and is sent only after the connection is authenticated. The secret is never a JSON field.

## Option A — TLS-PSK with a pairing code

On "Pair iPhone…" the Mac shows the code (and the stable id, so the phone can tell two Macs apart). The user types the code into the phone once. `PairingView` is the right screen: today it only says "Looking for your Mac" and has no way to pick or approve a Mac.

Both apps build `NWParameters` with TLS instead of `tls: nil`:

- `NWProtocolTLS.Options`, minimum and maximum TLS 1.2.
- `sec_protocol_options_add_pre_shared_key` with the 16-byte secret and a fixed identity string (`bluey`).
- PSK cipher only, `TLS_PSK_WITH_AES_128_GCM_SHA256`.
- No certificate check. PSK is the authentication.

`NWListener` and `NWConnection` both use that parameter object. A peer without the secret never reaches `LineConnection`. Tool JSON, the client secret, screenshots, and audio are inside TLS. Replay of an old session is the TLS stack's job.

Apple's Network framework supports this PSK API for **TLS 1.2 only**. An Apple DTS reply on Developer Forums thread 688508 (Aug 2021, radar 53459020) says TLS 1.3 with a pre-shared key is not supported, and that certificate TLS is the path that supports TLS 1.3. That is old, and it has to be re-checked on macOS 14 and iOS 17 before this option is treated as done. The check is a throwaway listener and connection, not a change to Bluey's protocol.

If that spike fails, keep this same code and the same screens, and switch the mechanism to a self-signed identity generated on the Mac. The phone pins that certificate (the code can be a fingerprint of the public key, or the QR can carry the pin). Apple documents certificate TLS as the TLS 1.3 path. The app protocol does not change between those two mechanisms. Do not fall back to a plaintext port.

| | |
| --- | --- |
| Implementation cost | Medium, plus one afternoon of handshake testing on a real Mac and phone. Almost all of the crypto stays in `NWParameters.googly(psk:)`. `LineConnection` and `Packet` stay put. Mac adds Keychain storage, the menu panel, and passes the secret into `PhoneServer`. iOS adds a code field on `PairingView`, Keychain storage keyed by the Bonjour TXT id, and refuses to connect when no secret is saved. |
| UX friction | Once per Mac, or again when the code is rotated. Typing 26 characters once is the whole cost. After that, reconnects are silent. Rotating the code is a deliberate menu action. |
| Shared | `NWParameters.googly` takes the PSK. Optional helper for the base32 code and for reading the TXT `id`. No new `Packet` fields. |
| Mac | Generate and store the secret. Menu UI. Listener uses the TLS parameters. Drop the plaintext listener. |
| iOS | Entry UI, storage, TLS parameters on the connection. A distinct failure state when the handshake fails. |

Risk: the TLS 1.2 PSK spike might fail on current OS versions. The certificate-pin contingency is the planned answer to that, and it has to be tested too. Both apps must ship together.

## Option B — HMAC challenge on the current plaintext socket

Keep `tls: nil`. After TCP is up, and before `PhoneServer` hands any packet to `RealtimeHost`:

1. The Mac sends a fresh 32-byte nonce.
2. The phone replies with `HMAC-SHA256(secret, "phone-v1" || nonce || macId)`.
3. The Mac checks it in constant time, then replies with `HMAC-SHA256(secret, "mac-v1" || nonce || phoneName)`.
4. The phone checks that. Only then does either side accept `hello`, `tool`, `realtimeToken`, `transcribe`, `notes`, or `face`.

The nonce stops a replay of an old proof. A wrong code fails closed. This stops a stranger who can only open a new TCP connection.

It does not encrypt the connection. After the two HMAC packets, the client secret, screenshots, and meeting audio are still plaintext JSON lines. An on-path listener reads them. An on-path attacker can also insert a later `tool` line, because nothing authenticates the lines after the handshake.

MACing every line (a sequence number plus an HMAC over the JSON) would stop that injection and still would not hide the contents. It means a private frame format around multi-megabyte audio lines, with its own replay and truncation rules. That is a second TLS, maintained here.

| | |
| --- | --- |
| Implementation cost | Lower for the handshake alone: a few states in `LineConnection` or in `PhoneServer` / `MacLink`, and two new optional `Packet` fields (`nonce`, `proof`) or a dedicated `command`. Higher if every line is MACed, and that still leaves the traffic readable. |
| UX friction | Same as option A. The code, the Keychain, and the one-time entry screen are the same work. The crypto choice does not make pairing easier. |
| Shared | `NWParameters` stay plaintext. The challenge fields and the "not authenticated yet" rule have to live in the shared connection, or the two apps will drift. |
| Mac | Same secret storage and menu as option A. `PhoneServer` must ignore every command until the proof checks. Today it accepts commands as soon as the connection is `.ready`. |
| iOS | Same entry UI and storage as option A. `MacLink` must not send `realtimeToken` or `tool` until the Mac's proof is verified. |

Risk: shipping the handshake and calling the socket "paired" would close the unauthenticated-connect bug and leave the confidentiality bug open. The reviews called out both.

## Recommendation

Choose **option A**.

The port is not only a command channel. It carries an OpenAI client secret, screenshots, and meeting audio, and it can type into the Mac. TLS covers authentication and confidentiality with the system stack. Option B's handshake is less code and is the wrong place to stop: the dangerous bytes are the ones that come after it.

Approve option A with these constraints:

1. Spike TLS 1.2 PSK (`sec_protocol_options_add_pre_shared_key`, PSK-GCM, no certificate) between a Mac and an iPhone on the OS versions Bluey already requires (macOS 14, iOS 17).
2. If that spike fails, implement the same pairing code and the same screens with a self-signed Mac identity that the phone pins. That is still option A. It is not a reason to switch to option B.
3. Use the 16-byte Keychain secret and the one-time code above. Do not use a short numeric code, and do not add a PAKE in this round.
4. Leave `Packet` unchanged. Put the secret in `NWParameters`, not in `hello`.
5. Keep option B off the table unless TLS itself cannot be turned on. Even then, a handshake-only HMAC is not sufficient.

Suggested split after approval, so the three owners do not edit the same files: shared parameters and the base32/TXT helper in `Shared/`, secret plus menu plus listener on the Mac, code entry plus storage plus the failure state on the phone. One round, both apps together. No plaintext listener left behind.
