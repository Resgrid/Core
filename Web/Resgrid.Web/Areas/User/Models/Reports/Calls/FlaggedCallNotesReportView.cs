using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Calls
{
	public class FlaggedCallNotesReportView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime? Start { get; set; }
		public DateTime? End { get; set; }
		public List<FlaggedCallNoteRow> Notes { get; set; }
		public List<FlaggedCallImageRow> Images { get; set; }
		public List<FlaggedCallFileRow> Files { get; set; }

		public FlaggedCallNotesReportView()
		{
			Notes = new List<FlaggedCallNoteRow>();
			Images = new List<FlaggedCallImageRow>();
			Files = new List<FlaggedCallFileRow>();
		}
	}

	public class FlaggedCallNoteRow
	{
		public int CallNoteId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public string CallType { get; set; }
		public string CallAddress { get; set; }
		public DateTime CallLoggedOn { get; set; }
		public string NoteText { get; set; }
		public DateTime NoteTimestamp { get; set; }
		public string NoteAuthorName { get; set; }
		public DateTime? FlaggedOn { get; set; }
		public string FlaggedReason { get; set; }
		public string FlaggedByName { get; set; }
	}

	public class FlaggedCallImageRow
	{
		public int CallAttachmentId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public string CallType { get; set; }
		public string CallAddress { get; set; }
		public DateTime CallLoggedOn { get; set; }
		public string FileName { get; set; }
		public DateTime? ImageTimestamp { get; set; }
		public string ImageAuthorName { get; set; }
		public DateTime? FlaggedOn { get; set; }
		public string FlaggedReason { get; set; }
		public string FlaggedByName { get; set; }
	}

	public class FlaggedCallFileRow
	{
		public int CallAttachmentId { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public string CallType { get; set; }
		public string CallAddress { get; set; }
		public DateTime CallLoggedOn { get; set; }
		public string FileName { get; set; }
		public string FileTypeName { get; set; }
		public DateTime? FileTimestamp { get; set; }
		public string FileAuthorName { get; set; }
		public DateTime? FlaggedOn { get; set; }
		public string FlaggedReason { get; set; }
		public string FlaggedByName { get; set; }
	}
}
