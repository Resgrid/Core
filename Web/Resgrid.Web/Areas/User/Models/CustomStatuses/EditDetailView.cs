using System.Collections.Generic;
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

		/// <summary>The other active options in the same set, offered as next statuses.</summary>
		public List<CustomStateDetail> Siblings { get; set; } = new List<CustomStateDetail>();

		/// <summary>Posted next-status option ids; empty clears the restriction.</summary>
		public List<int> NextStateDetailIds { get; set; } = new List<int>();
	}
}
