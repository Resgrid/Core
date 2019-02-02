using System;
using Resgrid.Model;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Documents
{
	public class EditDocumentView
	{
		public string UserId { get; set; }
		public Document Document { get; set; }
		public int DocumentId { get; set; }
		public string Message { get; set; }

		[Required]
		public string Name { get; set; }

		public string Category { get; set; }

		public SelectList Categories { get; set; }  

		public string AdminOnly { get; set; }

		public string Description { get; set; }
	}
}