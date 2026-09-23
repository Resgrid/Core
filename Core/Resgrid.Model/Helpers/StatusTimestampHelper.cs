using System;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Picks the time a status change actually happened from what a client sent. Apps queue statuses while
	/// offline and replay them later; stamping the replay with the server's receive time moves "on scene at"
	/// to whenever the phone found signal again.
	/// </summary>
	public static class StatusTimestampHelper
	{
		/// <summary>How far in the past a client-supplied status time is still accepted (long offline stretches).</summary>
		public static readonly TimeSpan MaxClientAge = TimeSpan.FromDays(7);

		/// <summary>Allowance for device clock skew when a client-supplied status time is ahead of the server.</summary>
		public static readonly TimeSpan MaxClientSkew = TimeSpan.FromMinutes(5);

		/// <summary>
		/// Returns the UTC time of a status change: the client's UTC timestamp when it is present, zone-aware and
		/// plausible, otherwise <paramref name="nowUtc"/>. <paramref name="timestampUtc"/> is documented as UTC,
		/// so an unzoned value there is read as UTC. <paramref name="timestamp"/> is device-local and is only used
		/// when it carries its zone (an ISO-8601 value ending in Z or an offset), since an unzoned local time
		/// can't be placed on the timeline.
		/// </summary>
		public static DateTime ResolveStatusTimeUtc(DateTime? timestampUtc, DateTime? timestamp, DateTime nowUtc)
		{
			var candidate = ToUtc(timestampUtc, unspecifiedIsUtc: true) ?? ToUtc(timestamp, unspecifiedIsUtc: false);

			if (!candidate.HasValue)
				return nowUtc;

			if (candidate.Value > nowUtc.Add(MaxClientSkew) || candidate.Value < nowUtc.Subtract(MaxClientAge))
				return nowUtc;

			// Skew inside the allowance still must not put a status in the future.
			return candidate.Value > nowUtc ? nowUtc : candidate.Value;
		}

		private static DateTime? ToUtc(DateTime? value, bool unspecifiedIsUtc)
		{
			if (!value.HasValue || value.Value == DateTime.MinValue || value.Value == DateTime.MaxValue)
				return null;

			switch (value.Value.Kind)
			{
				case DateTimeKind.Utc:
					return value.Value;
				case DateTimeKind.Local:
					return value.Value.ToUniversalTime();
				default:
					return unspecifiedIsUtc ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null;
			}
		}
	}
}
