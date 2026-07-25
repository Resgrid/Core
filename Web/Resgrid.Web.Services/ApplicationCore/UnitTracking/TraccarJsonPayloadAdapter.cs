using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Tracking;

namespace Resgrid.Web.Services.ApplicationCore.UnitTracking
{
	internal static class TraccarJsonPayloadAdapter
	{
		internal const string PinnedVersion = "6.14.5";
		private const decimal KnotsToMetersPerSecond = 0.514444m;

		public static UnitTrackingPayloadParseResult Parse(
			JObject root,
			JsonSerializer serializer,
			DateTime receivedOn)
		{
			TraccarPositionDataInput input;
			try
			{
				input = root.ToObject<TraccarPositionDataInput>(serializer);
			}
			catch (JsonException)
			{
				return Result(UnitTrackingPayloadParseStatus.Malformed);
			}

			var errors = Validate(input);
			if (errors.Count > 0)
				return Invalid(errors);

			var position = input.Position;
			var attributes = position.Attributes;
			var timestamp = FirstTimestamp(
				position.FixTime,
				position.DeviceTime,
				position.ServerTime);
			var hasDeviceTimestamp = timestamp.HasValue;
			var uniqueId = input.Device.UniqueId.Trim();

			return new UnitTrackingPayloadParseResult
			{
				Status = UnitTrackingPayloadParseStatus.Success,
				ReportedDeviceIdentifier = uniqueId,
				Positions = new[]
				{
					new CanonicalTrackingPosition
					{
						EventId = CreateEventId(input, timestamp),
						TimestampUtc = hasDeviceTimestamp
							? EnsureUtc(timestamp.Value)
							: EnsureUtc(receivedOn),
						ReceivedOnUtc = EnsureUtc(receivedOn),
						Latitude = position.Latitude.Value,
						Longitude = position.Longitude.Value,
						AccuracyMeters = position.Accuracy,
						AltitudeMeters = position.Altitude,
						SpeedMetersPerSecond = position.SpeedKnots.HasValue
							? position.SpeedKnots.Value * KnotsToMetersPerSecond
							: null,
						HeadingDegrees = position.Course,
						Satellites = Attribute<int>(attributes, "sat"),
						Hdop = Attribute<decimal>(attributes, "hdop"),
						BatteryPercent = Attribute<decimal>(attributes, "batteryLevel"),
						ExternalPowerVolts = Attribute<decimal>(attributes, "power"),
						Ignition = Attribute<bool>(attributes, "ignition"),
						IsMoving = Attribute<bool>(attributes, "motion"),
						AlarmCode = StringAttribute(attributes, "alarm"),
						TimestampSource = hasDeviceTimestamp
							? TrackingTimestampSource.Device
							: TrackingTimestampSource.Server,
						IsValidFix = position.Valid ?? true
					}
				}
			};
		}

		private static IReadOnlyCollection<string> Validate(TraccarPositionDataInput input)
		{
			var errors = new List<string>();
			if (input?.Position == null)
				errors.Add("position is required.");
			if (input?.Device == null)
				errors.Add("device is required.");
			if (errors.Count > 0)
				return errors;

			if (!input.Position.DeviceId.HasValue || input.Position.DeviceId.Value <= 0)
				errors.Add("position.deviceId is required.");
			if (!input.Device.Id.HasValue || input.Device.Id.Value <= 0)
				errors.Add("device.id is required.");
			if (input.Position.DeviceId.HasValue &&
			    input.Device.Id.HasValue &&
			    input.Position.DeviceId.Value != input.Device.Id.Value)
				errors.Add("The forwarded Traccar position and device identifiers must match.");
			if (string.IsNullOrWhiteSpace(input.Device.UniqueId))
				errors.Add("device.uniqueId is required.");
			if (!input.Position.Latitude.HasValue)
				errors.Add("position.latitude is required.");
			if (!input.Position.Longitude.HasValue)
				errors.Add("position.longitude is required.");
			if (!FirstTimestamp(
				    input.Position.FixTime,
				    input.Position.DeviceTime,
				    input.Position.ServerTime).HasValue)
				errors.Add("A valid position fixTime, deviceTime, or serverTime is required.");

			return errors;
		}

		private static string CreateEventId(
			TraccarPositionDataInput input,
			DateTime? timestamp)
		{
			if (input.Position.Id.HasValue && input.Position.Id.Value > 0)
				return $"traccar:{PinnedVersion}:position:{input.Position.Id.Value}";

			var position = input.Position;
			var fingerprint = string.Join(
				"|",
				input.Device.UniqueId.Trim(),
				position.DeviceId.Value.ToString(CultureInfo.InvariantCulture),
				position.Protocol?.Trim() ?? string.Empty,
				timestamp.HasValue
					? EnsureUtc(timestamp.Value).ToString("O", CultureInfo.InvariantCulture)
					: string.Empty,
				Invariant(position.Latitude),
				Invariant(position.Longitude),
				Invariant(position.Altitude),
				Invariant(position.SpeedKnots),
				Invariant(position.Course),
				position.Valid.HasValue
					? position.Valid.Value ? "true" : "false"
					: string.Empty);

			var hash = Convert.ToHexString(
					SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
				.ToLowerInvariant();
			return $"traccar:{PinnedVersion}:fingerprint:{hash}";
		}

		private static DateTime? FirstTimestamp(params JToken[] values)
		{
			foreach (var value in values)
			{
				var timestamp = ParseTimestamp(value);
				if (timestamp.HasValue)
					return timestamp;
			}

			return null;
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

		private static T? Attribute<T>(JObject attributes, string name)
			where T : struct
		{
			if (attributes == null ||
			    !attributes.TryGetValue(name, StringComparison.Ordinal, out var value) ||
			    value.Type == JTokenType.Null)
				return null;

			try
			{
				return value.ToObject<T>();
			}
			catch (Exception ex) when (
				ex is JsonException ||
				ex is FormatException ||
				ex is InvalidCastException ||
				ex is OverflowException)
			{
				return null;
			}
		}

		private static string StringAttribute(JObject attributes, string name)
		{
			if (attributes == null ||
			    !attributes.TryGetValue(name, StringComparison.Ordinal, out var value) ||
			    value.Type != JTokenType.String)
				return null;

			var result = value.Value<string>();
			return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
		}

		private static string Invariant(decimal? value) =>
			value?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty;

		private static DateTime EnsureUtc(DateTime value) =>
			value.Kind switch
			{
				DateTimeKind.Utc => value,
				DateTimeKind.Local => value.ToUniversalTime(),
				_ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
			};

		private static UnitTrackingPayloadParseResult Result(UnitTrackingPayloadParseStatus status) =>
			new() { Status = status };

		private static UnitTrackingPayloadParseResult Invalid(IReadOnlyCollection<string> errors) =>
			new()
			{
				Status = UnitTrackingPayloadParseStatus.Invalid,
				Errors = errors
			};

		private sealed class TraccarPositionDataInput
		{
			[JsonProperty("position")]
			public TraccarPositionInput Position { get; set; }

			[JsonProperty("device")]
			public TraccarDeviceInput Device { get; set; }
		}

		private sealed class TraccarPositionInput
		{
			[JsonProperty("id")]
			public long? Id { get; set; }

			[JsonProperty("attributes")]
			public JObject Attributes { get; set; }

			[JsonProperty("deviceId")]
			public long? DeviceId { get; set; }

			[JsonProperty("protocol")]
			public string Protocol { get; set; }

			[JsonProperty("serverTime")]
			public JToken ServerTime { get; set; }

			[JsonProperty("deviceTime")]
			public JToken DeviceTime { get; set; }

			[JsonProperty("fixTime")]
			public JToken FixTime { get; set; }

			[JsonProperty("valid")]
			public bool? Valid { get; set; }

			[JsonProperty("latitude")]
			public decimal? Latitude { get; set; }

			[JsonProperty("longitude")]
			public decimal? Longitude { get; set; }

			[JsonProperty("altitude")]
			public decimal? Altitude { get; set; }

			[JsonProperty("speed")]
			public decimal? SpeedKnots { get; set; }

			[JsonProperty("course")]
			public decimal? Course { get; set; }

			[JsonProperty("accuracy")]
			public decimal? Accuracy { get; set; }
		}

		private sealed class TraccarDeviceInput
		{
			[JsonProperty("id")]
			public long? Id { get; set; }

			[JsonProperty("uniqueId")]
			public string UniqueId { get; set; }
		}
	}
}
