using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	public static class DispatchDisplayHelper
	{
		public static async Task<string> GetLocalizedCallPriorityAsync(int departmentId, int priority, IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> localizer)
		{
			switch ((CallPriority)priority)
			{
				case CallPriority.Low:
					return localizer["CallPriorityLow"].Value;
				case CallPriority.Medium:
					return localizer["CallPriorityMedium"].Value;
				case CallPriority.High:
					return localizer["CallPriorityHigh"].Value;
				case CallPriority.Emergency:
					return localizer["CallPriorityEmergency"].Value;
				default:
					return await CallPriorityHelper.CallPriorityToString(departmentId, priority);
			}
		}

		public static string GetLocalizedCallState(int state, IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> localizer, IStringLocalizer<Resgrid.Localization.Common> commonLocalizer)
		{
			switch ((CallStates)state)
			{
				case CallStates.Active:
					return commonLocalizer["Active"].Value;
				case CallStates.Cancelled:
					return localizer["Cancelled"].Value;
				case CallStates.Closed:
					return commonLocalizer["Closed"].Value;
				case CallStates.Unfounded:
					return commonLocalizer["Unfounded"].Value;
				default:
					return commonLocalizer["Unknown"].Value;
			}
		}

		public static string GetLocalizedCallVideoFeedType(CallVideoFeedTypes type, IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> localizer)
		{
			switch (type)
			{
				case CallVideoFeedTypes.Drone:
					return localizer["VideoFeedDrone"].Value;
				case CallVideoFeedTypes.FixedCamera:
					return localizer["VideoFeedFixedCamera"].Value;
				case CallVideoFeedTypes.BodyCam:
					return localizer["VideoFeedBodyCam"].Value;
				case CallVideoFeedTypes.TrafficCam:
					return localizer["VideoFeedTrafficCam"].Value;
				case CallVideoFeedTypes.WeatherCam:
					return localizer["VideoFeedWeatherCam"].Value;
				case CallVideoFeedTypes.SatelliteFeed:
					return localizer["VideoFeedSatellite"].Value;
				case CallVideoFeedTypes.WebCam:
					return localizer["VideoFeedWebCam"].Value;
				default:
					return localizer["VideoFeedOther"].Value;
			}
		}

		public static string GetLocalizedCheckInTimerTargetType(CheckInTimerTargetType type, IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> localizer, IStringLocalizer<Resgrid.Localization.Common> commonLocalizer)
		{
			switch (type)
			{
				case CheckInTimerTargetType.Personnel:
					return commonLocalizer["Personnel"].Value;
				case CheckInTimerTargetType.UnitType:
					return localizer["CheckInTimerTargetUnitType"].Value;
				case CheckInTimerTargetType.IC:
					return "IC";
				case CheckInTimerTargetType.PAR:
					return "PAR";
				case CheckInTimerTargetType.HazmatExposure:
					return localizer["CheckInTimerTargetHazmatExposure"].Value;
				case CheckInTimerTargetType.SectorRotation:
					return localizer["CheckInTimerTargetSectorRotation"].Value;
				case CheckInTimerTargetType.Rehab:
					return localizer["CheckInTimerTargetRehab"].Value;
				default:
					return commonLocalizer["Unknown"].Value;
			}
		}

		public static string GetLocalizedOverrideState(bool isFromOverride, IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> localizer, IStringLocalizer<Resgrid.Localization.Common> commonLocalizer)
		{
			return isFromOverride ? localizer["Override"].Value : commonLocalizer["Default"].Value;
		}
	}
}
