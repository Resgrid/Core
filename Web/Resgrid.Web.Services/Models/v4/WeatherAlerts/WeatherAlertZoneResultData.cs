namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class WeatherAlertZoneResultData
	{
		public string WeatherAlertZoneId { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string ZoneCode { get; set; }
		public string CenterGeoLocation { get; set; }
		public double RadiusMiles { get; set; }
		public bool IsActive { get; set; }
		public bool IsPrimary { get; set; }
	}
}
