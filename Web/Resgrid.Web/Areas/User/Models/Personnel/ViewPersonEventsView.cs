using Resgrid.Model;
using Resgrid.Web.Areas.User.Models;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class ViewPersonEventsView : BaseUserModel
	{
		public string UserId { get; set; }
		public string PersonName { get; set; }
		public bool ConfirmClearAll { get; set; }
		public string Message { get; set; }
		public string OSMKey { get; set; }
		public double CenterLat { get; set; }
		public double CenterLon { get; set; }
	}
}

