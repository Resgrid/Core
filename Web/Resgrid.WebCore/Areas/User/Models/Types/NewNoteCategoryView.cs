using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Types
{
	public class NewNoteCategoryView
	{
		[Required]
		public string Name { get; set; }
		public string Message { get; set; }
	}
}
