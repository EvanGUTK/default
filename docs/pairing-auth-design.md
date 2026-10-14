# Pairing auth for Bluey

**Decision (2026-10-13, Hermes):** ship **Option B**, the HMAC challenge below, now. Do not implement TLS-PSK in this round.

Reason: Apple's Network framework does not support TLS 1.3 with a pre-shared key (Developer Forums thread 688508, Aug 2021, radar 53459020). Blocking the open-port hole on an unverified Mac+iPhone TLS spike is the wrong trade. TLS-PSK stays Option A / Phase 2 until someone can test it on real hardware.

This file is the wire contract. `Shared/`, `Mac/`, and `iOS/` implement it as written. `NWParameters.googly` stays `tls: nil`.

Base packet is relay-shared's round 2 `Packet` (their branch, not yet required to be in every worktree): `command` is still a `String?`, `Command` is the typed vocabulary, `proto` and `error` exist, `volume` and `speech` are gone. Merge that change before adding the fields below. Today each side sends `Packet.hello` (a device name) as soon as TCP is `.ready`, and `PhoneServer` then accepts `tool`, `realtimeToken`, `transcribe`, and `notes` from any peer. The round 2 `init` stamps `proto = GooglyService.protocolVersion` whenever `hello != nil`. Handshake packets that carry `hello` will therefore also carry `proto`. That is fine. The HMAC input does not include `proto`, and a handshake packet must not be rejected because `proto` is present or absent.

## Known limitation (Phase 2 closes this)

v1 **authenticates the peer. It does not encrypt the socket.**

After `authOk`, every line is still plaintext JSON. A LAN sniffer can read the OpenAI client secret (`realtimeToken`), screen JPEGs (`image`), and meeting-audio chunks (`audio`). An on-path attacker can also insert a later `tool` line. A per-line MAC is not part of v1.

That confidentiality gap is what Option A / Phase 2 is for. Do not describe v1 as "encrypted pairing" or "TLS" in UI copy or comments.

A paired phone is still fully trusted. Awake-gating, the computer-control default, the shortcut denylist, and session-id paths are separate tasks.

## Secret and the code the user types

The shared secret **is** 16 random bytes. v1 does not derive it from a password, a short numeric code, or a KDF. No QR code in v1.

The Mac generates it once with `SecRandomCopyBytes` (16 bytes) the first time the listener starts, and shows it in the menu. The user types that code into the phone once. Both sides store the raw 16 bytes in the **real** Keychain (`Security` framework). `Mac/Sources/GooglyMac/Keychain.swift` writes `~/Library/Application Support/Googly/keys.json` in plaintext. The pairing secret does not go in that file, in `hello`, or in any `Packet` field.

**Code format.** Uppercase hex of the 16 bytes, 32 digits, displayed in groups of four separated by hyphens:

```text
0011-2233-4455-6677-8899-AABB-CCDD-EEFF
```

**Parsing.** Strip ASCII spaces, hyphens, and newlines. Uppercase. Accept only `0-9A-F`, and only when exactly 32 digits remain. Decode to exactly 16 bytes. Anything else is an invalid code and must not open a connection.

**One secret per Mac.** Every phone uses the same secret. "New pairing code" replaces those 16 bytes, **keeps the same Mac id**, and cancels every current phone connection. Phones must type the new code. Per-phone keys are out of scope.

**Mac id.** A UUID the Mac generates once, alongside the secret, and never rotates when the code rotates. Lowercase, with hyphens, 36 characters (`^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`). Bonjour name is not the id: `Host.current().localizedName` changes when the Mac is renamed.

Publish it on the Bonjour service as TXT key `id` (value is that 36-character string). The TXT record is public and is not authentication. The phone uses it to choose which saved secret to try. The handshake still carries `macId` in the challenge, and that value is the one that enters the HMAC.

**Keychain (local to each device).** Service string `co.visionairy.googly.pairing`. Accessibility `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`.

| Device | Account | Value |
| --- | --- | --- |
| Mac | `secret` | the 16 raw bytes |
| Mac | `id` | the UUID string, UTF-8 |
| iPhone | the Mac's UUID string | the 16 raw bytes |

Save a phone secret only after `authOk` verifies. A failed proof does not delete a secret that was already saved. A code the user just typed is discarded if `authOk` does not verify.

## Packet fields

Add three optional strings to `Packet` in `Shared/GooglyLink.swift`, **after `error`**, on both inits (the `String` command init and the `Command` init). Default them to `nil`. Do not reorder or rename `face`, `hello`, `proto`, `command`, `audio`, `callID`, `tool`, `text`, `image`, or `error`. JSON keys are the Swift names (default `JSONEncoder`, no snake_case). Nil values stay omitted. Key order on the wire does not matter.

Add these cases to `Command`. The wire value is the raw name (`authChallenge`, `authProof`, `authOk`). `Packet.kind` then returns them. Do not change `command` from `String?` to an enum.

- `authChallenge` — Mac → phone, first packet on a new connection.
- `authProof` — phone → Mac, the only packet the Mac accepts before it authenticates the connection.
- `authOk` — Mac → phone, sent only after the phone proof matches.

| Field | JSON key | Who sets it | Value |
| --- | --- | --- | --- |
| `nonce` | `nonce` | Mac, on `authChallenge` only | Standard base64 (**with** `=` padding) of **32** raw random bytes. `Data.base64EncodedString()` / `Data(base64Encoded:)`. |
| `macId` | `macId` | Mac, on `authChallenge` only | The Mac UUID string above. |
| `proof` | `proof` | Phone on `authProof`, Mac on `authOk` | Standard base64 with padding of the 32-byte HMAC-SHA256. |

`command` and `hello` already exist. Handshake commands are exactly `authChallenge`, `authProof`, and `authOk`. No other new `command` strings.

`hello` on `authProof` is the phone's display name. `hello` on `authOk` is the Mac's display name. If the device name is empty, the phone sends `iPhone` and the Mac sends `Mac`. Reject a `hello` whose UTF-8 length is greater than 64 bytes. The name is the decoded JSON string. A newline inside the name is allowed. JSON escaping takes care of framing.

Do not put the nonce, the proof, or the Mac id in `text`, `callID`, or `audio`.

Framing is unchanged: one JSON object, UTF-8, terminated by a single `0x0A` byte.

## HMAC

`CryptoKit.HMAC<SHA256>`, key = the raw 16 secret bytes (`SymmetricKey`). The message is the UTF-8 bytes of one canonical string. Build that string in Shared, not in each app.

Phone proof message (`role` is the four characters `phone`):

```text
bluey-auth-v1\n
phone\n
<nonce string, verbatim from the packet>\n
<macId string, verbatim from the packet>\n
<phone hello string>\n
```

Mac proof message is the same with `mac` in place of `phone` (three characters). The Mac hashes the phone name it **received** on `authProof`, not its own `hello`.

The nonce and mac id inside the message are the JSON strings, not the decoded bytes and not a re-encoded form. There is a trailing `\n` (U+000A). There is no `\r`. There are no spaces around the newlines.

Compare proofs by base64-decoding both sides and XOR-ing all 32 bytes. Do not use `String.==`. If either side does not decode to exactly 32 bytes, the proof is bad.

### Test vector

Both apps' helper must produce these outputs before the handshake is trusted.

- Secret hex: `00112233445566778899aabbccddeeff`
- Nonce raw: bytes `0x00` through `0x1F`
- Nonce string: `AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=`
- Mac id: `550e8400-e29b-41d4-a716-446655440000`
- Phone hello: `Test Phone`

Phone message is 113 bytes. Phone proof: `aSLimVD5MK4dYSBs2Rr5qmkInmOokQN5zAmtqOwHnkI=`

Mac message is 111 bytes. Mac proof: `6pwxUqEVbxlfDDOZ1BBR0SvIQFyCObrPJh3n3Zq8b5w=`

## Handshake

One handshake per TCP connection. A reconnect starts over with a **new** nonce. Timeout is **5 seconds** at each wait below. On any failure: `connection.cancel()`, log a reason locally, send **no** error packet, and do not call `onRequest`. Do not keep the socket open. Do not answer a bad proof with `authOk`.

`MacLink.connected` stays false until step 6. TCP `.ready` is not "paired". The phone must not send `hello`, `realtimeToken`, `tool`, `caption`, `transcribe`, `notes`, `awake`, or `asleep` before step 6. Send `authProof` with `send`, not `request` (no `callID`).

1. **TCP `.ready`.** The Mac draws 32 bytes (`SecRandomCopyBytes`), starts the 5s timer, and sends exactly:

```json
{"command":"authChallenge","macId":"<uuid>","nonce":"<base64>"}
```

The Mac does not send `hello`, `face`, or anything else yet. `PhoneServer.send(face:)` skips any phone that has not finished step 6.

2. **Phone waits for that one packet.** The first packet must be `authChallenge` with a `nonce` that decodes to 32 bytes and a `macId` that matches the UUID pattern. Any other first packet, a second challenge, a timeout, or a malformed field: close. Copy for a first packet that is a legacy `hello`: "This Mac needs an updated Bluey." Copy for a timeout with no packet: "The Mac didn't answer the pairing check."

3. **Phone picks a secret.** Use the saved secret for that `macId`. If none is saved, use a code the user has already typed and parsed. If there is no secret, **close**. Do not send a proof of zeros, and do not hold the socket open while the user types. Show the code field. After they enter a valid code, connect again. The new connection gets a new challenge.

4. **Phone sends:**

```json
{"command":"authProof","hello":"<phone name>","proof":"<base64 hmac>"}
```

5. **Mac checks the next packet.** It must be `authProof`, and it must arrive before the 5s timer. `hello` required, non-empty, at most 64 UTF-8 bytes. `proof` must match the phone message. Anything else, including a missing proof, a second packet that is not this one, or a mismatch: **close**. Do not ignore it and keep reading.

6. **Mac marks the connection authenticated**, cancels the timer, and sends:

```json
{"command":"authOk","hello":"<mac name>","proof":"<base64 hmac>"}
```

Then it may send the current `face` and normal traffic. The phone name stored for the menu is the `hello` from `authProof`.

7. **Phone checks `authOk`.** It must be the next packet, within 5s of sending the proof. `proof` must match the mac message over the same phone name the phone just sent. On success, set `connected = true`, save the secret under `macId` if this code was just typed, and only then send ordinary traffic (the existing `hello` may follow). On mismatch, timeout, or any other packet: close, do not save the new code. Copy: "That pairing code doesn't match this Mac."

After step 6, further `authChallenge`, `authProof`, and `authOk` packets on that connection are ignored. They do not start a second handshake and they do not close the socket. A new TCP connection is the only way to authenticate again.

## What each owner changes

No plaintext exception and no "auth off" flag.

| Owner | Files | Work |
| --- | --- | --- |
| relay-shared | `Shared/GooglyLink.swift` (new helper next to it is fine) | Add `nonce`, `proof`, `macId` after `error`, and the three `Command` cases. Add `PairingAuth` with the command constants, the 5s timeout, code parse/format, the two message builders, `proof(secret:message:) -> String`, and constant-time `proofsMatch`. `NWParameters.googly` stays plaintext. The helper must match the test vector. |
| forge-engine | `PhoneServer.swift`, menu in `AppDelegate.swift`, real Keychain calls | Generate and store `id` + secret. Menu "Pair iPhone…" shows the grouped code and "New code". TXT `id` on the Bonjour service. Challenge on `.ready`, gate every command and every face until step 6, close on a bad or missing proof. |
| nova-ui | `MacLink.swift`, `PairingView.swift`, iOS Keychain | Code field, no QR, no camera permission. `connected` only after `authOk`. Send nothing but `authProof` before that. Verify the Mac proof. Use the failure sentences above. Do not treat a dropped handshake as the 1.5s Bonjour retry with no message. |

`command` strings the Mac already accepts (`realtimeToken`, `transcribe`, `notes`, `awake`, `asleep`, `caption`, `captionDone`, `tool`) are unchanged after step 6. `wake` / `sleep` from the Mac are unchanged after step 6.

## Option A / Phase 2 — TLS-PSK (not this round)

Tracked backlog. Do not build it until a real Mac and a real iPhone are available to test the handshake.

Same pairing code and the same Keychain secret. Both apps build `NWParameters` with TLS instead of `tls: nil`:

- `NWProtocolTLS.Options`, minimum and maximum TLS 1.2.
- `sec_protocol_options_add_pre_shared_key` with the 16-byte secret and identity `bluey`.
- PSK cipher only, `TLS_PSK_WITH_AES_128_GCM_SHA256`.
- No certificate check. The PSK is the authentication.

Apple's PSK API is documented for **TLS 1.2 only**. The DTS reply cited above says TLS 1.3 external PSK is not supported, and that certificate-based TLS is the path that gets TLS 1.3. Phase 2 starts with a throwaway listener/connection spike on macOS 14 and iOS 17. If that spike fails, keep the same code and screens and pin a self-signed Mac identity. Do not leave a plaintext listener beside it.

When Phase 2 lands, the HMAC commands can go away. The v1 hole it closes is the one stated at the top: a sniffer on the LAN can read secrets, screenshots, and meeting audio, and an on-path attacker can still inject `tool` lines.
