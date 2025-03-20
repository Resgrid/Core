using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Areas.User.Models.Forms
{
	public class NewFormModel
	{
		public string Message { get; set; }

		[Required]
		public string FormName { get; set; }

		[Required]
		public string Data { get; set; }

		public FormTypes FormType { get; set; }

		public SelectList FormTypes { get; set; }
	}
}
