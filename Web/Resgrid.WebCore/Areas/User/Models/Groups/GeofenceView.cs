using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class GeofenceView
	{
		public DepartmentGroup Group { get; set; }
		public string Message { get; set; }
		public Coordinates Coordinates { get; set; }
	}
}