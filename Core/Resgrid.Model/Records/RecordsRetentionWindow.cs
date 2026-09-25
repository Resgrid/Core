using System;

namespace Resgrid.Model
{
	/// <summary>Shared calendar-year boundary. Zero, unknown or unrepresentable dates never expire.</summary>
	public static class RecordsRetentionWindow
	{
		public static bool HasExpired(DateTime? finalized, int years, DateTime now) =>
			finalized.HasValue && years > 0 && years <= 9999 - finalized.Value.Year && finalized.Value.AddYears(years) <= now;
	}
}
