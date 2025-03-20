using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Types
{
	public class NewContactNoteCategoryView
	{
		[Required]
		public string Name { get; set; }
		public string Color { get; set; }
		public string Message { get; set; }
		public string TypeId { get; set; }
	}
}
