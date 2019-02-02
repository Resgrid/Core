using System.ComponentModel.DataAnnotations;
using Resgrid.Web.Services.Areas.HelpPage.ModelDescriptions;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	[ModelName("NewCallInputV3")]
	public class NewCallInput
	{
		[Required]
		public string Pri { get; set; }
		[Required]
		public string Nme { get; set; }
		[Required(ErrorMessage = "The Nature of call field is required.")]
		public string Noc { get; set; }
		public string Not { get; set; }
		public string Add { get; set; }
		public string Geo { get; set; }

		public string Cid { get; set; }
		public string Typ { get; set; }
		public string W3W { get; set; }
		public string Dis { get; set; }
		public string CNme { get; set; }
		public string CNum { get; set; }
	}
}