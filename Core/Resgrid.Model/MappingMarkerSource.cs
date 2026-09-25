using System;

namespace Resgrid.Model
{
	public enum MappingMarkerSource { None, LocationPing, Status }

	/// <summary>Shared v4 map marker selection. TTL expires pings; a status location remains a fallback.</summary>
	public static class MappingMarkerSelection
	{
		public static MappingMarkerSource Select(DateTime? pingOn, DateTime? statusOn, bool statusHasLocation,
			int pingTtlMinutes, bool allowStatusWithoutLocationToOverwrite, DateTime nowUtc)
			=> Select(pingOn, statusOn, () => statusHasLocation, pingTtlMinutes, allowStatusWithoutLocationToOverwrite, nowUtc);

		public static MappingMarkerSource Select(DateTime? pingOn, DateTime? statusOn, Func<bool> statusHasLocation,
			int pingTtlMinutes, bool allowStatusWithoutLocationToOverwrite, DateTime nowUtc)
		{
			if (pingOn.HasValue && pingTtlMinutes > 0 && nowUtc.AddMinutes(-pingTtlMinutes) > pingOn.Value) pingOn = null;
			if (pingOn.HasValue)
			{
				if (!statusOn.HasValue || pingOn.Value > statusOn.Value) return MappingMarkerSource.LocationPing;
				if (statusHasLocation()) return MappingMarkerSource.Status;
				return allowStatusWithoutLocationToOverwrite ? MappingMarkerSource.None : MappingMarkerSource.LocationPing;
			}
			return statusOn.HasValue && statusHasLocation() ? MappingMarkerSource.Status : MappingMarkerSource.None;
		}
	}
}
