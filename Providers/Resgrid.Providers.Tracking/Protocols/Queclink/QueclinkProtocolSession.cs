using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Providers.Tracking.Protocols.Queclink
{
	public sealed class QueclinkProtocolSession :
		ITrackingProtocolSession
	{
		private const int MaximumFields = 128;
		private const int MaximumFieldLength = 256;
		private const int MaximumPositionsPerFrame = 16;
		private const decimal MaximumSpeedKilometersPerHour =
			1000m;

		private static readonly IReadOnlySet<string>
			LocationReportTypes =
				new HashSet<string>(
					new[]
					{
						"CTN", "DOG", "ERI", "FRI",
						"GEO", "HBM", "IDL", "IGF",
						"IGL", "IGN", "JDR", "JDS",
						"NMR", "RTL", "SOS", "SPD",
						"STR", "STT", "SWG", "TEM",
						"TMP", "TOW", "VGF", "VGN"
					},
					StringComparer.Ordinal);

		private static readonly IReadOnlySet<string>
			StatusReportTypes =
				new HashSet<string>(
					new[]
					{
						"BPL", "DIS", "EPF", "EPN",
						"INF", "MPF", "MPN", "PFA",
						"PNA", "VER"
					},
					StringComparer.Ordinal);

		private static readonly IReadOnlyDictionary<string, string>
			AlarmCodes =
				new Dictionary<string, string>(
					StringComparer.Ordinal)
				{
					["BPL"] = "lowBattery",
					["EPF"] = "powerCut",
					["EPN"] = "powerRestored",
					["HBM"] = "harshMotion",
					["IDL"] = "idle",
					["JDR"] = "jamming",
					["JDS"] = "jamming",
					["MPF"] = "powerCut",
					["MPN"] = "powerRestored",
					["PFA"] = "powerOff",
					["PNA"] = "powerOn",
					["SOS"] = "sos",
					["SPD"] = "overspeed",
					["STT"] = "movement",
					["SWG"] = "geofence",
					["TEM"] = "temperature",
					["TMP"] = "temperature",
					["TOW"] = "tow"
				};

		private readonly int _maximumFrameBytes;

		public QueclinkProtocolSession(
			TrackingSessionContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (context.MaxFrameBytes <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(context.MaxFrameBytes));

			_maximumFrameBytes = context.MaxFrameBytes;
		}

		public ProtocolParseResult Parse(
			ref ReadOnlySequence<byte> input)
		{
			if (input.IsEmpty)
				return NeedMore(input);

			var frameLength = FindFrameLength(input);
			if (frameLength == 0)
			{
				return input.Length >= _maximumFrameBytes
					? Terminal(
						input.End,
						ProtocolParseStatus.Malformed,
						"frame-too-large")
					: NeedMore(input);
			}
			if (frameLength > _maximumFrameBytes)
			{
				return Terminal(
					input.GetPosition(frameLength),
					ProtocolParseStatus.Malformed,
					"frame-too-large");
			}

			var consumed = input.GetPosition(frameLength);
			var frame = input.Slice(0, frameLength).ToArray();
			var contentLength = frame.Length - 1;
			while (contentLength > 0 &&
			       (frame[contentLength - 1] == (byte)'\r' ||
			        frame[contentLength - 1] == (byte)'\n'))
			{
				contentLength--;
			}

			if (contentLength == 0 ||
			    !IsPrintableAscii(
				    frame.AsSpan(0, contentLength)))
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"ascii-frame-invalid");
			}

			var sentence = Encoding.ASCII.GetString(
				frame,
				0,
				contentLength);
			var fields = sentence.Split(
				',',
				StringSplitOptions.None);
			if (fields.Length < 3 ||
			    fields.Length > MaximumFields ||
			    fields.Any(
				    field =>
					    field.Length >
					    MaximumFieldLength))
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"field-count-invalid");
			}

			if (!TryReadHeader(
				    fields[0],
				    out var prefix,
				    out var reportType) ||
			    !IsProtocolVersion(fields[1]) ||
			    !IsImei(fields[2]))
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Malformed,
					"header-invalid");
			}

			if (prefix == "ACK" &&
			    reportType == "HBD")
			{
				var acknowledgement =
					Encoding.ASCII.GetBytes(
						$"+SACK:GTHBD,{fields[1]},{fields[^1]}$");
				return Message(
					consumed,
					ProtocolParseStatus.Heartbeat,
					new ProtocolMessage
					{
						MessageType =
							ProtocolMessageType.Heartbeat,
						ExternalIdentifier = fields[2],
						AcknowledgementToken =
							acknowledgement,
						RequiresResponse = true
					});
			}

			if (prefix != "RESP" &&
			    prefix != "BUFF")
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Unsupported,
					"prefix-unsupported");
			}
			if (!LocationReportTypes.Contains(reportType) &&
			    !StatusReportTypes.Contains(reportType))
			{
				return Terminal(
					consumed,
					ProtocolParseStatus.Unsupported,
					"report-type-unsupported");
			}

			var positions = ParsePositions(
				fields,
				reportType,
				frame.AsSpan(0, contentLength));
			if (positions.Count == 0)
			{
				return Message(
					consumed,
					ProtocolParseStatus.Heartbeat,
					new ProtocolMessage
					{
						MessageType =
							ProtocolMessageType.Heartbeat,
						ExternalIdentifier = fields[2],
						RequiresResponse = false
					});
			}

			return Message(
				consumed,
				ProtocolParseStatus.Positions,
				new ProtocolMessage
				{
					MessageType =
						ProtocolMessageType.Positions,
					ExternalIdentifier = fields[2],
					Positions = positions,
					RequiresResponse = false
				});
		}

		public ReadOnlyMemory<byte> BuildResponse(
			ProtocolMessage message,
			TrackingAcceptance acceptance)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (acceptance == null)
				throw new ArgumentNullException(nameof(acceptance));
			if (message.MessageType !=
				    ProtocolMessageType.Heartbeat ||
			    acceptance.Status !=
			    TrackingAcceptanceStatus.Accepted)
			{
				return ReadOnlyMemory<byte>.Empty;
			}

			return message.AcknowledgementToken;
		}

		private static IReadOnlyCollection<CanonicalTrackingPosition>
			ParsePositions(
				IReadOnlyList<string> fields,
				string reportType,
				ReadOnlySpan<byte> rawFrame)
		{
			var positions =
				new List<CanonicalTrackingPosition>();
			var receivedOn = DateTime.UtcNow;
			for (var index = 3;
			     index + 6 < fields.Count - 1 &&
			     positions.Count < MaximumPositionsPerFrame;
			     index++)
			{
				if (!TryParseLocation(
					    fields,
					    index,
					    receivedOn,
					    out var position))
				{
					continue;
				}

				ApplyReportMetadata(
					position,
					reportType,
					fields,
					index);
				position.EventId = CreateEventId(
					rawFrame,
					positions.Count);
				positions.Add(position);
				index += 6;
			}

			return positions;
		}

		private static bool TryParseLocation(
			IReadOnlyList<string> fields,
			int index,
			DateTime receivedOn,
			out CanonicalTrackingPosition position)
		{
			position = null;
			if (!TryParseOptionalDecimal(
				    fields[index],
				    0m,
				    99m,
				    out var hdop) ||
			    !TryParseOptionalDecimal(
				    fields[index + 1],
				    0m,
				    MaximumSpeedKilometersPerHour,
				    out var speed) ||
			    !TryParseOptionalDecimal(
				    fields[index + 2],
				    0m,
				    360m,
				    out var heading) ||
			    !TryParseOptionalDecimal(
				    fields[index + 3],
				    -20000m,
				    100000m,
				    out var altitude) ||
			    !TryParseCoordinate(
				    fields[index + 4],
				    -180m,
				    180m,
				    out var longitude) ||
			    !TryParseCoordinate(
				    fields[index + 5],
				    -90m,
				    90m,
				    out var latitude) ||
			    !TryParseTimestamp(
				    fields[index + 6],
				    out var timestamp))
			{
				return false;
			}

			position = new CanonicalTrackingPosition
			{
				TimestampUtc = timestamp,
				ReceivedOnUtc = receivedOn,
				Latitude = latitude,
				Longitude = longitude,
				Hdop = hdop,
				AltitudeMeters = altitude,
				SpeedMetersPerSecond =
					speed.HasValue
						? speed.Value / 3.6m
						: null,
				HeadingDegrees = heading,
				IsMoving =
					speed.HasValue
						? speed.Value > 0m
						: null,
				TimestampSource =
					TrackingTimestampSource.Device,
				IsValidFix = true
			};
			return true;
		}

		private static void ApplyReportMetadata(
			CanonicalTrackingPosition position,
			string reportType,
			IReadOnlyList<string> fields,
			int locationIndex)
		{
			if (AlarmCodes.TryGetValue(
				    reportType,
				    out var alarmCode))
				position.AlarmCode = alarmCode;

			if (reportType == "IGN" ||
			    reportType == "VGN")
				position.Ignition = true;
			else if (reportType == "IGF" ||
			         reportType == "VGF")
				position.Ignition = false;

			if (reportType == "STT" ||
			    reportType == "HBM")
				position.IsMoving = true;

			for (var index = locationIndex - 1;
			     index >= 3 &&
			     index >= locationIndex - 5;
			     index--)
			{
				if (!int.TryParse(
					    fields[index],
					    NumberStyles.None,
					    CultureInfo.InvariantCulture,
					    out var millivolts) ||
				    millivolts <= 10 ||
				    millivolts > 100000)
					continue;

				position.ExternalPowerVolts =
					millivolts / 1000m;
				break;
			}

			for (var index = locationIndex + 7;
			     index + 1 < fields.Count - 1;
			     index++)
			{
				if (!IsDeviceStatus(fields[index + 1]) ||
				    !int.TryParse(
					    fields[index],
					    NumberStyles.None,
					    CultureInfo.InvariantCulture,
					    out var battery) ||
				    battery < 0 ||
				    battery > 100)
				{
					continue;
				}

				position.BatteryPercent = battery;
				ApplyDeviceStatus(
					position,
					fields[index + 1]);
				break;
			}
		}

		private static void ApplyDeviceStatus(
			CanonicalTrackingPosition position,
			string status)
		{
			if (status.Length != 6 ||
			    !int.TryParse(
				    status.Substring(0, 2),
				    NumberStyles.HexNumber,
				    CultureInfo.InvariantCulture,
				    out var ignition))
				return;

			if ((ignition & (1 << 4)) != 0)
				position.Ignition = false;
			else if ((ignition & (1 << 5)) != 0)
				position.Ignition = true;
		}

		private static bool TryReadHeader(
			string header,
			out string prefix,
			out string reportType)
		{
			prefix = null;
			reportType = null;
			if (header?.Length == 10 &&
			    header.StartsWith(
				    "+ACK:GT",
				    StringComparison.Ordinal))
			{
				prefix = "ACK";
				reportType = header.Substring(7, 3);
				return true;
			}
			if (header?.Length != 11 ||
			    header[0] != '+' ||
			    header[5] != ':' ||
			    header[6] != 'G' ||
			    header[7] != 'T')
				return false;

			prefix = header.Substring(1, 4);
			reportType = header.Substring(8, 3);
			return prefix == "RESP" ||
			       prefix == "BUFF";
		}

		private static bool IsProtocolVersion(
			string value)
		{
			return value != null &&
			       (value.Length == 6 ||
			        value.Length == 10) &&
			       value.All(IsHexCharacter);
		}

		private static bool IsImei(string value)
		{
			return value?.Length == 15 &&
			       value.All(
				       character =>
					       character >= '0' &&
					       character <= '9');
		}

		private static bool IsDeviceStatus(string value)
		{
			return value != null &&
			       (value.Length == 2 ||
			        value.Length == 6) &&
			       value.All(IsHexCharacter);
		}

		private static bool IsHexCharacter(char value)
		{
			return value >= '0' &&
			       value <= '9' ||
			       value >= 'A' &&
			       value <= 'F' ||
			       value >= 'a' &&
			       value <= 'f';
		}

		private static bool TryParseOptionalDecimal(
			string value,
			decimal minimum,
			decimal maximum,
			out decimal? parsed)
		{
			parsed = null;
			if (string.IsNullOrEmpty(value))
				return true;
			if (!TryParseRequiredDecimal(
				    value,
				    minimum,
				    maximum,
				    out var required))
				return false;

			parsed = required;
			return true;
		}

		private static bool TryParseRequiredDecimal(
			string value,
			decimal minimum,
			decimal maximum,
			out decimal parsed)
		{
			return decimal.TryParse(
				       value,
				       NumberStyles.AllowLeadingSign |
				       NumberStyles.AllowDecimalPoint,
				       CultureInfo.InvariantCulture,
				       out parsed) &&
			       parsed >= minimum &&
			       parsed <= maximum;
		}

		private static bool TryParseCoordinate(
			string value,
			decimal minimum,
			decimal maximum,
			out decimal parsed)
		{
			parsed = 0;
			var separator = value?.LastIndexOf('.') ?? -1;
			if (separator <= 0 ||
			    value.Length - separator - 1 != 6)
				return false;

			for (var index = separator + 1;
			     index < value.Length;
			     index++)
			{
				if (value[index] < '0' ||
				    value[index] > '9')
					return false;
			}

			return TryParseRequiredDecimal(
				value,
				minimum,
				maximum,
				out parsed);
		}

		private static bool TryParseTimestamp(
			string value,
			out DateTime timestamp)
		{
			return DateTime.TryParseExact(
				value,
				"yyyyMMddHHmmss",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
				out timestamp);
		}

		private static bool IsPrintableAscii(
			ReadOnlySpan<byte> value)
		{
			foreach (var item in value)
			{
				if (item < 0x20 ||
				    item > 0x7E)
					return false;
			}

			return true;
		}

		private static long FindFrameLength(
			ReadOnlySequence<byte> input)
		{
			var reader = new SequenceReader<byte>(input);
			long length = 0;
			while (reader.TryRead(out var value))
			{
				length++;
				if (value == (byte)'$' ||
				    value == 0)
					return length;
			}

			return 0;
		}

		private static string CreateEventId(
			ReadOnlySpan<byte> frame,
			int positionIndex)
		{
			using var hash = IncrementalHash.CreateHash(
				HashAlgorithmName.SHA256);
			hash.AppendData(frame);
			Span<byte> indexBytes = stackalloc byte[4];
			BinaryPrimitives.WriteInt32BigEndian(
				indexBytes,
				positionIndex);
			hash.AppendData(indexBytes);
			return "queclink:" +
			       Convert.ToHexString(
					       hash.GetHashAndReset())
				       .ToLowerInvariant();
		}

		private static ProtocolParseResult NeedMore(
			ReadOnlySequence<byte> input)
		{
			return new ProtocolParseResult
			{
				Status =
					ProtocolParseStatus.NeedMoreData,
				Consumed = input.Start,
				Examined = input.End
			};
		}

		private static ProtocolParseResult Message(
			SequencePosition consumed,
			ProtocolParseStatus status,
			ProtocolMessage message)
		{
			return new ProtocolParseResult
			{
				Status = status,
				Consumed = consumed,
				Examined = consumed,
				Message = message
			};
		}

		private static ProtocolParseResult Terminal(
			SequencePosition consumed,
			ProtocolParseStatus status,
			string reasonCode)
		{
			return new ProtocolParseResult
			{
				Status = status,
				Consumed = consumed,
				Examined = consumed,
				ReasonCode = reasonCode
			};
		}
	}
}
