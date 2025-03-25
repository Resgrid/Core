using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CustomStatuses
{
	public class EditDetailView
	{
		public CustomStateDetail Detail { get; set; }
		public CustomStateDetailTypes DetailType { get; set; }
		public SelectList DetailTypes { get; set; }
		public CustomStateNoteTypes NoteType { get; set; }
		public SelectList NoteTypes { get; set; }
		public ActionBaseTypes BaseType { get; set; }
		public SelectList BaseTypes { get; set; }
	}
}
