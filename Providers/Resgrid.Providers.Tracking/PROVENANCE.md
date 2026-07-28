# Tracking adapter provenance

## `traccar-json-v1`

### Pin

- Upstream repository: https://github.com/traccar/traccar
- Release: `v6.14.5`
- Commit: `5c5e710d5e357912f1b30561ed54bfd07a5d42f9`
- Commit date: 2026-06-18
- License: Apache License 2.0

### Public material reviewed

- Position-forwarding configuration: https://www.traccar.org/forward/
- `src/main/java/org/traccar/forward/PositionData.java`
- `src/main/java/org/traccar/forward/PositionForwarderJson.java`
- `src/main/java/org/traccar/model/Position.java`
- `src/main/java/org/traccar/model/Device.java`
- `src/main/java/org/traccar/model/Message.java`
- `src/main/java/org/traccar/model/ExtendedModel.java`
- `src/main/java/org/traccar/model/BaseModel.java`

All source-file references above are pinned to commit
`5c5e710d5e357912f1b30561ed54bfd07a5d42f9`.

### Adaptation record

The Resgrid adapter is an independent C# implementation of the serialized forwarding
contract. No Java implementation code was copied or ported. The mapping intentionally:

- accepts the `PositionData` envelope's `position` and `device` objects;
- uses `device.uniqueId` only for defense-in-depth binding validation;
- cross-checks Traccar's internal device IDs;
- converts `Position.speed` from knots to meters per second;
- maps only allowlisted health and alarm attributes;
- ignores unrecognized fields;
- derives a deterministic SHA-256 retry fingerprint when `Position.id` is zero.

### Fixtures

`Tests/Resgrid.Tests/Data/UnitTracking/Fixtures/traccar/v6.14.5/` contains an
independently generated, sanitized fixture representing a long-tail SinoTrack ST-901
decoded by Traccar's `h02` protocol. It contains no upstream or customer packet data and
does not constitute physical-hardware certification.

## `teltonika-codec8`

### Public material reviewed

- Official protocol documentation:
  https://wiki.teltonika-gps.com/view/Teltonika_Data_Sending_Protocols
- Official Wave 1 model parameter tables:
  - https://wiki.teltonika-gps.com/view/FMC920_Teltonika_Data_Sending_Parameters_ID
  - https://wiki.teltonika-gps.com/view/FMM920_Teltonika_Data_Sending_Parameters_ID
  - https://wiki.teltonika-gps.com/view/FMC130_Teltonika_Data_Sending_Parameters_ID
  - https://wiki.teltonika-gps.com/view/FMM130_Teltonika_Data_Sending_Parameters_ID
  - https://wiki.teltonika-gps.com/view/FMC003_Teltonika_Data_Sending_Parameters_ID
- Reviewed: 2026-07-26

### Adaptation record

The Resgrid module is an independent C# implementation of the public Teltonika data
sending protocol. No vendor implementation code was copied or ported. The initial
bounded scope:

- accepts the 15-digit TCP IMEI login and emits the documented binary accept/reject
  response;
- accepts the UDP channel wrapper with its declared length, channel packet ID, marker,
  AVL packet ID, and embedded 15-digit IMEI;
- parses Codec8 (`0x08`) and Codec8 Extended (`0x8E`) AVL record arrays;
- validates the preamble, big-endian data length, CRC-16/IBM, codec, record counts,
  coordinates, timestamps, priority, angle, and bounded I/O-element structure;
- maps codec-defined GPS fields to canonical tracking positions;
- loads the five Wave 1 profiles and their I/O map from the validated embedded catalog;
- registers seven WP11 family candidates against the same module without assigning
  those models an I/O map;
- retains only fixed-width numeric I/O values allowlisted by that catalog, then applies
  the selected model profile only after IMEI authentication;
- maps AVL 182 to HDOP with multiplier `0.1`, AVL 66 to external power volts with
  multiplier `0.001`, AVL 239 to ignition, and AVL 240 to movement, enforcing the
  documented raw ranges;
- ignores all other I/O values and discards the bounded parser metadata before canonical
  ingress;
- derives deterministic SHA-256 event fingerprints from each raw AVL record;
- emits the TCP four-byte accepted-record count or UDP channel response with matching
  packet IDs only when canonical ingress accepts the complete packet.

The FMC920, FMM920, FMC130, FMM130, and FMC003 Wave 1 profiles and the FMM003, FMC125,
FMM125, FMC150, FMM150, FMC230, and FMM230 WP11 profiles remain non-selectable
`Candidate` entries with no certified transports. The WP11 entries are catalog-only
family-placement hypotheses and deliberately have no model I/O map. Captured
model/firmware fixtures, exact firmware pins, model mapping review, and physical-device
certification are not part of this documentation-derived pass. The test-only TCP and
UDP simulators are generated from the public packet layout and are not certification
evidence.

### Fixtures

`Tests/Resgrid.Tracking.Tests/Data/Teltonika/` contains independently generated minimal
Codec8 and Codec8 Extended TCP packets, their UDP channel wrappers, and a synthetic
nonzero-I/O Codec8 packet covering the four allowlisted values. They are based on the
public field layout, contain no vendor or customer packet data, and do not constitute
physical-hardware certification. The test-only TCP simulator uses these fixtures to
exercise login, fragmented current/buffered batches, duplicate resend, CRC rejection,
mid-frame disconnect, and ACK timing relative to canonical ingress confirmation. The
UDP simulator covers wrapper construction, matching response IDs and record counts,
buffered/duplicate datagrams, malformed count rejection, and the same ACK ordering.

## `queclink-attrack`

### Pin

- Upstream repository: https://github.com/traccar/traccar
- Release: `v6.14.5`
- Commit: `5c5e710d5e357912f1b30561ed54bfd07a5d42f9`
- Commit date: 2026-06-18
- License: Apache License 2.0

### Upstream source reviewed

- `src/main/java/org/traccar/protocol/Gl200Protocol.java`
- `src/main/java/org/traccar/protocol/Gl200FrameDecoder.java`
- `src/main/java/org/traccar/protocol/Gl200ProtocolDecoder.java`
- `src/main/java/org/traccar/protocol/Gl200TextProtocolDecoder.java`
- `src/main/java/org/traccar/protocol/Gl200BinaryProtocolDecoder.java`
- `src/test/java/org/traccar/protocol/Gl200TextProtocolDecoderTest.java`
- `src/test/java/org/traccar/protocol/Gl200BinaryProtocolDecoderTest.java`

All source-file references are pinned to commit
`5c5e710d5e357912f1b30561ed54bfd07a5d42f9`.

### Adaptation record

The Resgrid module is a bounded C# adaptation informed by the pinned Traccar GL200
implementation. No Traccar Java source is compiled into Resgrid. The initial scope:

- accepts printable ASCII `+RESP` and `+BUFF` reports terminated by `$` or NUL, with
  strict frame, field-count, field-length, protocol-version, and 15-digit IMEI bounds;
- accepts only an explicit allowlist of position, status, ignition, and alarm report
  types and rejects unknown report types instead of guessing a layout;
- recognizes the bounded GL200 location tuple and maps timestamp, coordinates, speed,
  heading, altitude, HDOP, movement, ignition, battery, external power, and selected
  alarms into the canonical tracking model;
- accepts `+ACK:GTHBD` heartbeat reports and emits the matching `+SACK:GTHBD` only after
  canonical ingress accepts the heartbeat;
- leaves position acknowledgements disabled because their behavior is
  configuration-dependent in the pinned implementation;
- derives deterministic SHA-256 event fingerprints from the raw report and position
  index;
- supports TCP only. UDP and GL200 binary framing are deliberately not registered.

The GV57MG, GV350MG, and GV500MA profiles remain non-selectable `Candidate` entries
with no certified transports. Current manufacturer documents, captured
model/firmware packets, and physical-device certification remain required before any
profile or transport is promoted.

### Fixtures

`Tests/Resgrid.Tracking.Tests/Data/Queclink/` contains sanitized ASCII messages copied
from the pinned `Gl200TextProtocolDecoderTest`. They exercise live and buffered
positions, ignition, and heartbeat/response behavior. They are upstream
interoperability fixtures, not captured packets from the three target devices and not
certification evidence. The test-only TCP simulator uses them to exercise fragmented
and duplicate reports plus heartbeat response behavior through the real listener.

## `gt06`

### Pin

- Upstream repository: https://github.com/traccar/traccar
- Release: `v6.14.5`
- Commit: `5c5e710d5e357912f1b30561ed54bfd07a5d42f9`
- Commit date: 2026-06-18
- License: Apache License 2.0

### Upstream source reviewed

- `src/main/java/org/traccar/protocol/Gt06Protocol.java`
- `src/main/java/org/traccar/protocol/Gt06FrameDecoder.java`
- `src/main/java/org/traccar/protocol/Gt06ProtocolDecoder.java`
- `src/test/java/org/traccar/protocol/Gt06ProtocolDecoderTest.java`

All source-file references are pinned to commit
`5c5e710d5e357912f1b30561ed54bfd07a5d42f9`.

### Adaptation record

The Resgrid module is a bounded C# adaptation informed by the pinned Traccar GT06
implementation. No Traccar Java source is compiled into Resgrid. The initial scope:

- accepts short `0x7878` and extended `0x7979` frames with bounded declared lengths,
  `0x0D0A` terminators, serial numbers, and CRC-16/X25 validation;
- requires a valid BCD-encoded 15-digit IMEI login before all other messages;
- accepts the bounded login, heartbeat/status, GPS/LBS `0x22`, GPS/LBS status/alarm
  `0x16`, and JM-VL03 `0xA0` location layouts selected for this implementation;
- maps timestamp, coordinates, fix validity, satellites, speed, heading, movement,
  ignition, battery, signal, and selected model-aware alarms into the canonical model;
- emits CRC-protected acknowledgements with the original header form, protocol number,
  and serial only after login or complete canonical-ingress acceptance;
- derives deterministic SHA-256 event fingerprints from the complete raw frame;
- rejects unrecognized message types rather than attempting universal GT06-clone
  detection;
- supports TCP only. UDP is deliberately not registered.

The VL103M and JM-VL03 Wave 1 profiles and JM-VL01, JM-VL02, and JM-VL04 WP11 profiles
remain non-selectable `Candidate` entries with no certified transports. The WP11
decoder-variant names are explicitly marked unverified; they do not add accepted
message layouts or claim that a sibling uses one of the tested Wave 1 layouts. Current
manufacturer documents, captured model/firmware packets, exact variant dispatch, and
physical-device certification remain required before any profile or transport is
promoted.

### Fixtures

`Tests/Resgrid.Tracking.Tests/Data/Gt06/` contains sanitized binary packets copied from
the pinned `Gt06ProtocolDecoderTest`. They cover login, a standard GPS/LBS location,
the JM-VL03-associated `0xA0` layout, and heartbeat/status framing. A generated
extended-header form exercises bounded framing and exact response construction. These
are upstream interoperability fixtures, not captured packets from the target devices
and not certification evidence. The test-only TCP simulator exercises fragmented
login/location frames and verifies that the gateway defers the response until
canonical ingress confirms acceptance.
