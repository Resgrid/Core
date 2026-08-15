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
			DateTimeStyles = System.Globalization.DateTimeStyles.AdjustToUniversal;
			DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'";
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
