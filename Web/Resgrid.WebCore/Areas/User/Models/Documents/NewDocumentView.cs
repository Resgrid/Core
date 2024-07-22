using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Documents
{
	public class NewDocumentView
	{
		public string Message { get; set; }

		[Required]
		public string Name { get; set; }

		public string Category { get; set; }

		public SelectList Categories { get; set; }

		public string AdminOnly { get; set; }

		public string Description { get; set; }
	}
}
