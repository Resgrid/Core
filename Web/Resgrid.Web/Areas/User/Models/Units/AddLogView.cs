using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class AddLogView : BaseUserModel
	{
		public string UnitName { get; set; }
		public UnitLog Log { get; set; }
	}
}