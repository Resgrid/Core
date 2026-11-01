using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>notes.board (plan §7.6): the newest department notes as plain-text previews (stored HTML reduced server-side).</summary>
	public sealed class NotesBoardWidgetData
	{
		public List<NotesBoardItem> Items { get; set; } = new List<NotesBoardItem>();
	}

	public sealed class NotesBoardItem
	{
		public int NoteId { get; set; }

		public string Title { get; set; }

		/// <summary>Plain text, at most 200 characters (an ellipsis marks a cut).</summary>
		public string Preview { get; set; }

		public string Category { get; set; }

		/// <summary>The note's own color (a CSS color or empty).</summary>
		public string Color { get; set; }

		public bool IsAdminOnly { get; set; }

		public DateTime AddedOnUtc { get; set; }

		public DateTime? ExpiresOnUtc { get; set; }

		public string Url { get; set; }
	}
}
