using Resgrid.Model;
using Resgrid.Web.Services.Areas.HelpPage.ModelDescriptions;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Groups
{
	[ModelName("GroupInfoResultV3")]
	public class GroupInfoResult
	{
		public int Gid { get; set; }
		public int Typ { get; set; }
		public string Nme { get; set; }
		public string Add { get; set; }
	}
}