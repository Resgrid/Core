namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class PerformCheckInInput
	{
		public int CallId { get; set; }
		public int CheckInType { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public int? UnitId { get; set; }
		public string Note { get; set; }
	}
}
