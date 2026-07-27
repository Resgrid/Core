# Teltonika generated protocol fixtures

These fixtures were independently generated from the public Teltonika Codec8/8E packet
structure documented at:

https://wiki.teltonika-gps.com/view/Teltonika_Data_Sending_Protocols

They contain no customer or captured device data and do not constitute hardware or
firmware certification.

- `codec8-location.hex`: one Codec8 record for San Francisco at
  `2024-01-02T03:04:05Z`, 15 m altitude, 90° heading, 8 satellites, and 36 km/h.
- `codec8e-location.hex`: one Codec8 Extended record for Berlin at the same timestamp,
  34 m altitude, 270° heading, 11 satellites, and 72 km/h.
- `codec8-udp-location.hex`: the Codec8 data array above wrapped in the documented UDP
  channel using channel packet ID `0xCAFE`, AVL packet ID `0x05`, and the generated
  test IMEI.
- `codec8e-udp-location.hex`: the Codec8 Extended data array above wrapped in the
  documented UDP channel using channel packet ID `0xBEEF`, AVL packet ID `0x07`, and
  the same generated test IMEI.
- `codec8-io-location.hex`: one generated Codec8 record with the shared Wave 1
  allowlisted values for HDOP, external voltage, ignition, and movement.

The original Codec8 and Codec8 Extended records deliberately contain zero I/O elements.
The I/O fixture validates only the documented shared Wave 1 map and does not represent
captured model/firmware evidence.
