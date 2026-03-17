using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.IndoorMaps
{
	public class IndoorMapNewView : BaseUserModel
	{
		public IndoorMap IndoorMap { get; set; }
		public string Message { get; set; }

		public IndoorMapNewView()
		{
			IndoorMap = new IndoorMap();
		}
	}
}
