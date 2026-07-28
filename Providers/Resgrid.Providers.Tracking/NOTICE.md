# Third-party notices

## Traccar

The Resgrid `traccar-json-v1` interoperability adapter was independently implemented
against Traccar's public JSON position-forwarding contract. The bounded
`queclink-attrack` and `gt06` protocol modules are C# adaptations informed by the
pinned Traccar decoders, frame handlers, and tests. Their interoperability fixtures
include sanitized packets copied from the pinned Traccar test suite.

- Project: Traccar GPS Tracking System
- Repository: https://github.com/traccar/traccar
- Pinned version: `v6.14.5`
- Pinned commit: `5c5e710d5e357912f1b30561ed54bfd07a5d42f9`
- License: Apache License 2.0
- Copyright: Anton Tananaev and Traccar contributors

No Traccar Java source is compiled into Resgrid. The repository root `LICENSE` contains
the Apache License 2.0. See `PROVENANCE.md` for the exact files reviewed, adapted
behavior, copied fixtures, and certification limits.
