using Resgrid.Web.Services.Areas.HelpPage.ModelDescriptions;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Departments
{
	[ModelName("RespondingOptionResultV3")]
	public class RespondingOptionResult
	{
		public int Id { get; set; }
		public int Typ { get; set; }
		public string Nme { get; set; }
	}
}