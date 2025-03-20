namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class SaveGeofenceModel
	{
		public int DepartmentGroupId { get; set; }
		public string Color { get; set; }
		public string GeoFence { get; set; }

		public bool Success { get; set; }
		public string Message { get; set; }
	}
}