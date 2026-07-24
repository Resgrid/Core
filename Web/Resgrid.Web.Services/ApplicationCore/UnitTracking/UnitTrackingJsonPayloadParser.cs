using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Tracking;
using Resgrid.Web.Services.Models.v4.UnitTracking;

namespace Resgrid.Web.Services.ApplicationCore.UnitTracking
{
	public enum UnitTrackingPayloadParseStatus
	{
		Success = 0,
		Malformed = 1,
		Invalid = 2,
		TooLarge = 3,
		UnsupportedMediaType = 4
	}

	public sealed class UnitTrackingPayloadParseResult
	{
		public UnitTrackingPayloadParseStatus Status { get; set; }
		public IReadOnlyCollection<CanonicalTrackingPosition> Positions { get; set; } =
			Array.Empty<CanonicalTrackingPosition>();
		public string ReportedDeviceIdentifier { get; set; }
		public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
	}

	public class UnitTrackingJsonPayloadParser
	{
		private static readonly UTF8Encoding StrictUtf8 = new(false, true);

		public bool Supports(string payloadAdapterKey) =>
			string.Equals(
				payloadAdapterKey?.Trim(),
				"resgrid-json-v1",
				StringComparison.OrdinalIgnoreCase);

		public async Task<UnitTrackingPayloadParseResult> ParseAsync(
			HttpRequest request,
			DateTime receivedOn,
			CancellationToken cancellationToken = default)
		{
			if (!IsJson(request.ContentType) || HasUnsupportedContentEncoding(request))
				return Result(UnitTrackingPayloadParseStatus.UnsupportedMediaType);

			var maximumBytes = Math.Max(1, UnitTrackingConfig.MaxRequestBytes);
			if (request.ContentLength.HasValue && request.ContentLength.Value > maximumBytes)
				return Result(UnitTrackingPayloadParseStatus.TooLarge);

			byte[] body;
			try
			{
				body = await ReadBoundedBodyAsync(request.Body, maximumBytes, cancellationToken);
			}
			catch (PayloadTooLargeException)
			{
				return Result(UnitTrackingPayloadParseStatus.TooLarge);
			}

			if (body.Length == 0)
				return Result(UnitTrackingPayloadParseStatus.Malformed);

			JObject root;
			try
			{
				var json = StrictUtf8.GetString(body);
				using var stringReader = new StringReader(json);
				using var jsonReader = new JsonTextReader(stringReader)
				{
					DateParseHandling = DateParseHandling.DateTime,
					FloatParseHandling = FloatParseHandling.Decimal,
					MaxDepth = Math.Max(1, UnitTrackingConfig.MaxJsonDepth)
				};
				var token = JToken.ReadFrom(
					jsonReader,
					new JsonLoadSettings
					{
						DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
						LineInfoHandling = LineInfoHandling.Ignore
					});
				if (token is not JObject parsedRoot || HasTrailingContent(jsonReader))
					return Result(UnitTrackingPayloadParseStatus.Malformed);
				root = parsedRoot;
			}
			catch (Exception ex) when (
				ex is JsonException ||
				ex is DecoderFallbackException ||
				ex is ArgumentException)
			{
				return Result(UnitTrackingPayloadParseStatus.Malformed);
			}

			var serializer = JsonSerializer.Create(new JsonSerializerSettings
			{
				DateTimeZoneHandling = DateTimeZoneHandling.Utc,
				FloatParseHandling = FloatParseHandling.Decimal,
				MissingMemberHandling = MissingMemberHandling.Ignore
			});

			UnitTrackingPositionInput[] inputs;
			string envelopeIdentifier = null;
			try
			{
				if (root.TryGetValue("positions", StringComparison.OrdinalIgnoreCase, out _))
				{
					var batch = root.ToObject<UnitTrackingPositionsInput>(serializer);
					inputs = batch?.Positions;
					envelopeIdentifier = batch?.DeviceIdentifier;
				}
				else
				{
					inputs = new[] { root.ToObject<UnitTrackingPositionInput>(serializer) };
				}
			}
			catch (JsonException)
			{
				return Result(UnitTrackingPayloadParseStatus.Malformed);
			}

			if (inputs == null || inputs.Length == 0)
				return Invalid("At least one position is required.");
			if (inputs.Length > Math.Max(1, UnitTrackingConfig.MaxBatchPositions))
				return Result(UnitTrackingPayloadParseStatus.TooLarge);

			var errors = new List<string>();
			var positions = new List<CanonicalTrackingPosition>(inputs.Length);
			var identifiers = new HashSet<string>(StringComparer.Ordinal);

			if (!string.IsNullOrWhiteSpace(envelopeIdentifier))
				identifiers.Add(envelopeIdentifier.Trim());

			for (var index = 0; index < inputs.Length; index++)
			{
				var input = inputs[index];
				if (input == null)
				{
					errors.Add($"positions[{index}]: Position is required.");
					continue;
				}
				if (string.IsNullOrWhiteSpace(input.EventId))
					errors.Add($"positions[{index}]: eventId is required.");
				if (!input.Latitude.HasValue)
					errors.Add($"positions[{index}]: latitude is required.");
				if (!input.Longitude.HasValue)
					errors.Add($"positions[{index}]: longitude is required.");

				if (!string.IsNullOrWhiteSpace(input.DeviceIdentifier))
					identifiers.Add(input.DeviceIdentifier.Trim());

				if (input.Latitude.HasValue && input.Longitude.HasValue)
					positions.Add(Map(input, receivedOn));
			}

			if (identifiers.Count > 1)
				errors.Add("All reported device identifiers in a batch must match.");
			if (errors.Count > 0)
				return Invalid(errors);

			return new UnitTrackingPayloadParseResult
			{
				Status = UnitTrackingPayloadParseStatus.Success,
				Positions = positions,
				ReportedDeviceIdentifier = identifiers.SingleOrDefault()
			};
		}

		private static CanonicalTrackingPosition Map(
			UnitTrackingPositionInput input,
			DateTime receivedOn)
		{
			var deviceTimestamp = ParseTimestamp(input.Timestamp);
			var hasDeviceTimestamp = deviceTimestamp.HasValue;
			return new CanonicalTrackingPosition
			{
				EventId = input.EventId,
				TimestampUtc = hasDeviceTimestamp
					? EnsureUtc(deviceTimestamp.Value)
					: receivedOn,
				ReceivedOnUtc = receivedOn,
				Latitude = input.Latitude.Value,
				Longitude = input.Longitude.Value,
				AccuracyMeters = input.AccuracyMeters,
				AltitudeMeters = input.AltitudeMeters,
				SpeedMetersPerSecond = input.SpeedMetersPerSecond,
				HeadingDegrees = input.HeadingDegrees,
				Satellites = input.Satellites,
				Hdop = input.Hdop,
				BatteryPercent = input.BatteryPercent,
				ExternalPowerVolts = input.ExternalPowerVolts,
				SignalPercent = input.SignalPercent,
				Ignition = input.Ignition,
				IsMoving = input.Moving,
				AlarmCode = input.AlarmCode,
				TimestampSource = hasDeviceTimestamp
					? TrackingTimestampSource.Device
					: TrackingTimestampSource.Server,
				IsValidFix = input.ValidFix ?? true
			};
		}

		private static DateTime? ParseTimestamp(JToken token)
		{
			if (token == null || token.Type == JTokenType.Null)
				return null;

			if (token.Type == JTokenType.Date)
				return token.Value<DateTime>();

			if (token.Type != JTokenType.String)
				return null;

			return DateTimeOffset.TryParse(
				token.Value<string>(),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces |
				DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
				out var parsed)
				? parsed.UtcDateTime
				: null;
		}

		private static async Task<byte[]> ReadBoundedBodyAsync(
			Stream body,
			int maximumBytes,
			CancellationToken cancellationToken)
		{
			using var buffer = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
			var chunk = new byte[Math.Min(8192, maximumBytes + 1)];
			var total = 0;

			while (true)
			{
				var read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
				if (read == 0)
					break;

				total += read;
				if (total > maximumBytes)
					throw new PayloadTooLargeException();

				await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
			}

			return buffer.ToArray();
		}

		private static bool IsJson(string contentType)
		{
			if (string.IsNullOrWhiteSpace(contentType))
				return false;

			var mediaType = contentType.Split(';', 2)[0].Trim();
			return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
		}

		private static bool HasUnsupportedContentEncoding(HttpRequest request)
		{
			if (!request.Headers.TryGetValue("Content-Encoding", out var encodings))
				return false;

			return encodings.Any(encoding =>
				!string.IsNullOrWhiteSpace(encoding) &&
				!string.Equals(encoding.Trim(), "identity", StringComparison.OrdinalIgnoreCase));
		}

		private static bool HasTrailingContent(JsonTextReader reader)
		{
			while (reader.Read())
			{
				if (reader.TokenType != JsonToken.Comment)
					return true;
			}

			return false;
		}

		private static DateTime EnsureUtc(DateTime value) =>
			value.Kind switch
			{
				DateTimeKind.Utc => value,
				DateTimeKind.Local => value.ToUniversalTime(),
				_ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
			};

		private static UnitTrackingPayloadParseResult Result(UnitTrackingPayloadParseStatus status) =>
			new() { Status = status };

		private static UnitTrackingPayloadParseResult Invalid(string error) =>
			Invalid(new[] { error });

		private static UnitTrackingPayloadParseResult Invalid(IReadOnlyCollection<string> errors) =>
			new()
			{
				Status = UnitTrackingPayloadParseStatus.Invalid,
				Errors = errors
			};

		private sealed class PayloadTooLargeException : Exception
		{
		}
	}
}
