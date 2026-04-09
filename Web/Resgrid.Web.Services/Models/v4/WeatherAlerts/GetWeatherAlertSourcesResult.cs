using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class GetWeatherAlertSourcesResult : StandardApiResponseV4Base
	{
		public List<WeatherAlertSourceResultData> Data { get; set; }

		public GetWeatherAlertSourcesResult()
		{
			Data = new List<WeatherAlertSourceResultData>();
		}
	}
}
