using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomMaps
{
	public class CustomMapNewView : BaseUserModel
	{
		public IndoorMap Map { get; set; }
		public string Message { get; set; }

		public CustomMapNewView()
		{
			Map = new IndoorMap();
		}
	}
}
