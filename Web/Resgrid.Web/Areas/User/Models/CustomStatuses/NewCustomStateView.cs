using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomStatuses
{
	public class NewCustomStateView
	{
		public string Message { get; set; }
		public CustomStateTypes Type { get; set; }
		public CustomState State { get; set; }
	}
}