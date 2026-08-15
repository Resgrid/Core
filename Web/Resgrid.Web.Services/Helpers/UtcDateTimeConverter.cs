using System;
using Newtonsoft.Json.Converters;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Serialises a <see cref="DateTime"/> that is known to hold a UTC instant with an explicit "Z".
	///
	/// Values read back from the repositories carry <see cref="DateTimeKind.Unspecified"/>, and
	/// Newtonsoft's default round-trip handling writes those with no zone marker at all
	/// ("2026-08-12T13:05:22"). Every JavaScript client then parses that as *local* time, so a unit
	/// whose status changed a moment ago reads as hours old in any department that is not on UTC.
	///
	/// Apply this to response properties whose value is a UTC instant. Do not apply it to the
	/// department-local companions (e.g. CurrentStatusTimestamp) -- those really are local wall time
	/// and marking them UTC would shift them the other way.
	/// </summary>
	public class UtcDateTimeConverter : IsoDateTimeConverter
	{
		public UtcDateTimeConverter()
		{
			// AssumeUniversal is what makes a zone-less "2026-08-12T13:05:22" read back as the same
			// instant. Without it AdjustToUniversal assumes *local* and shifts the value by whatever
			// offset the server happens to run on.
			DateTimeStyles = System.Globalization.DateTimeStyles.AssumeUniversal |
							 System.Globalization.DateTimeStyles.AdjustToUniversal;
			DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'";
			Culture = System.Globalization.CultureInfo.InvariantCulture;
		}

		public override object ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
		{
			var underlyingType = Nullable.GetUnderlyingType(objectType) ?? objectType;

			// The base converter reads with ParseExact against DateTimeFormat, so the "...fffZ" *write*
			// format would reject every inbound string not written exactly that way -- the zone-less
			// values the repositories produce, whole-second values, anything carrying an offset. Parse
			// those here instead; the styles above are what make a zone-less value keep its instant.
			if (reader.TokenType == Newtonsoft.Json.JsonToken.String && underlyingType == typeof(DateTime))
			{
				var dateText = reader.Value?.ToString();

				if (!String.IsNullOrWhiteSpace(dateText))
					return NormalizeToUtc(DateTime.Parse(dateText, Culture, DateTimeStyles));
			}

			return NormalizeToUtc(base.ReadJson(reader, objectType, existingValue, serializer));
		}

		/// <summary>
		/// A JsonToken.Date comes back from the reader untouched, so whatever Kind the reader inferred
		/// survives. Normalised the same way WriteJson does, otherwise a value only round-trips when
		/// the reader happened to see a "Z".
		/// </summary>
		private static object NormalizeToUtc(object value)
		{
			if (value is DateTime dateTime && dateTime.Kind != DateTimeKind.Utc)
			{
				return dateTime.Kind == DateTimeKind.Unspecified
					? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
					: dateTime.ToUniversalTime();
			}

			return value;
		}

		public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
		{
			if (value is DateTime dateTime && dateTime.Kind != DateTimeKind.Utc)
			{
				// Unspecified means "we already know this is UTC, the store just did not say so".
				// Local means the process timezone leaked in; converting is the honest fix.
				value = dateTime.Kind == DateTimeKind.Unspecified
					? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
					: dateTime.ToUniversalTime();
			}

			base.WriteJson(writer, value, serializer);
		}
	}
}
