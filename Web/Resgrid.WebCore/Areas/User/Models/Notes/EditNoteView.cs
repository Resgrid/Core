using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;


namespace Resgrid.Web.Areas.User.Models.Notes
{
	public class EditNoteView
	{
		public int NoteId { get; set; }
		public string Message { get; set; }

		[Required]
		public string Title { get; set; }

		[Required]
		public string Body { get; set; }
		public string IsAdminOnly { get; set; }
		public string Category { get; set; }
		public SelectList Categories { get; set; }
	}
}
