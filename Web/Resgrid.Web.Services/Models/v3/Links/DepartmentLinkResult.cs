namespace Resgrid.Web.Services.Controllers.Version3.Models.Links
{
	public class DepartmentLinkResult
	{
		public int LinkId { get; set; }

		public string DepartmentName { get; set; }

		public string Color { get; set; }

		public bool ShareCalls { get; set; }

		public bool ShareUnits { get; set; }

		public bool SharePersonnel { get; set; }

		public bool ShareOrders { get; set; }
	}
}