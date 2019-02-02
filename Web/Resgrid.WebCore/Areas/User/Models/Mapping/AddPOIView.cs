using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class AddPOIView
	{
		public int TypeId { get; set; }
		public string Message { get; set; }
		public Poi Poi { get; set; }
	}
}