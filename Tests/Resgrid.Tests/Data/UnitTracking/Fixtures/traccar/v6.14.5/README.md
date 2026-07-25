# Traccar v6.14.5 fixture

- Adapter: `traccar-json-v1`
- Upstream version: Traccar `v6.14.5`
- Upstream commit: `5c5e710d5e357912f1b30561ed54bfd07a5d42f9`
- Fixture source: independently generated from Traccar's Apache-2.0 `PositionData`,
  `Position`, and `Device` serialization contract
- Long-tail profile represented: SinoTrack ST-901 using Traccar's `h02` decoder
- Redistribution: generated for Resgrid; contains no captured customer, device, or vendor
  data
- Expected result: one valid canonical position, unique ID `917000000000`, speed converted
  from 10 knots to 5.14444 m/s, selected health/alarm attributes mapped, and unknown
  attributes ignored

This fixture proves the pinned JSON mapping and retry fingerprint only. It is not a
captured hardware packet and does not by itself promote the catalog profile beyond
`Candidate`.
