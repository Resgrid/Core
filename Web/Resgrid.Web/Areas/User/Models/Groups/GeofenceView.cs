using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class GeofenceView
	{
		public DepartmentGroup Group { get; set; }
		public string Message { get; set; }
		public Coordinates Coordinates { get; set; }

		/// <summary>The boundary re-serialized from its parsed points ([{"lat":..,"lng":..}]); safe to write into a script block.</summary>
		public string GeofenceJson { get; set; } = "[]";
	}
}