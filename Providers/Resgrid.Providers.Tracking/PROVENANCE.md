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
