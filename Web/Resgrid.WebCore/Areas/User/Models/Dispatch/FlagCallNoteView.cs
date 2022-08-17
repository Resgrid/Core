using System;

namespace Resgrid.WebCore.Areas.User.Models.Dispatch
{
	public class FlagCallNoteView
	{
		public string Message { get; set; }
		public int CallId { get; set; }
		public int CallNoteId { get; set; }
		public string CallNote { get; set; }
		public string AddedOn { get; set; }
		public string AddedBy { get; set; }
		public bool IsFlagged { get; set; }
		public string FlagNote { get; set; }
		public string FlaggedOn { get; set; }
		public string FlaggedBy { get; set; }
	}
}
