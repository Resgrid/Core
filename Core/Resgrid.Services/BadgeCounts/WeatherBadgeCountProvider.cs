using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// The department's active, unexpired Extreme and Severe weather alerts (one grouped COUNT, cached and dropped by
	/// WeatherAlertService on every change). The badge is their sum; the breakdown keeps them apart.
	/// </summary>
	public class WeatherBadgeCountProvider : IBadgeCountProvider
	{
		private readonly IWeatherAlertService _weatherAlertService;

		public WeatherBadgeCountProvider(IWeatherAlertService weatherAlertService)
		{
			_weatherAlertService = weatherAlertService;
		}

		public string Key => BadgeCountKeys.Weather;

		public string RequiredPolicy => "WeatherAlert_View";

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			var counts = await _weatherAlertService.GetActiveSevereAlertCountsAsync(context.DepartmentId) ?? new WeatherAlertSeverityCounts();

			return new BadgeCount(Key, counts.Extreme + counts.Severe, new Dictionary<string, int> { ["extreme"] = counts.Extreme, ["severe"] = counts.Severe });
		}
	}
}
