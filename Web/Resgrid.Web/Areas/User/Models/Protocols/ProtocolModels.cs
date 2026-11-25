using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>The protocol library (Protocols/Index).</summary>
	public class ProtocolIndexModel
	{
		public List<ProtocolLibraryRow> Rows { get; set; } = new List<ProtocolLibraryRow>();
		public int Total { get; set; }
		public int DisabledCount { get; set; }
		/// <summary>Distinct protocols used on at least one call logged in the last 30 days.</summary>
		public int UsedOnCallsCount { get; set; }
		public string Query { get; set; }
		/// <summary>"", "enabled" or "disabled".</summary>
		public string Status { get; set; }
		public bool CanCreate { get; set; }
		public bool CanModify { get; set; }
		public bool CanDelete { get; set; }
		public string Message { get; set; }
	}

	public class ProtocolLibraryRow
	{
		public DispatchProtocol Protocol { get; set; }
		/// <summary>One short line per trigger ("Priority: High", "Type: Structure Fire").</summary>
		public List<string> Triggers { get; set; } = new List<string>();
		public int CallsLast30Days { get; set; }
	}

	/// <summary>
	/// The protocol editor, shared by New and Edit. The protocol's own id is never bound from the form (Edit takes it from the
	/// route); child rows carry their ids so they are updated in place, and the server checks each one is this protocol's.
	/// </summary>
	public class ProtocolEditorModel
	{
		public string Name { get; set; }
		public string Code { get; set; }
		public string Description { get; set; }
		public string ProtocolText { get; set; }
		public int MinimumWeight { get; set; }
		public bool IsDisabled { get; set; }
		public List<ProtocolTriggerInput> Triggers { get; set; } = new List<ProtocolTriggerInput>();
		public List<ProtocolQuestionInput> Questions { get; set; } = new List<ProtocolQuestionInput>();
		/// <summary>Existing attachments to remove on save (Edit).</summary>
		public List<int> RemoveAttachmentIds { get; set; } = new List<int>();

		// Shown, never bound.
		[BindNever] public int ProtocolId { get; set; }
		[BindNever] public bool IsNew { get; set; }
		[BindNever] public List<DispatchProtocolAttachment> ExistingAttachments { get; set; } = new List<DispatchProtocolAttachment>();
		[BindNever] public List<DepartmentCallPriority> CallPriorities { get; set; } = new List<DepartmentCallPriority>();
		[BindNever] public List<CallType> CallTypes { get; set; } = new List<CallType>();
		[BindNever] public string TimeZoneName { get; set; }
	}

	public class ProtocolTriggerInput
	{
		/// <summary>The row's key in the posted collection (the "Triggers.Index" convention), so rows can be added and removed in the page.</summary>
		public string Key { get; set; }
		/// <summary>0 for a new trigger.</summary>
		public int Id { get; set; }
		public int Type { get; set; }
		/// <summary>Department local date and time from the picker (yyyy-MM-ddTHH:mm); stored in UTC.</summary>
		public string StartsOn { get; set; }
		public string EndsOn { get; set; }
		public int? Priority { get; set; }
		/// <summary>Null is "no type".</summary>
		public int? CallTypeId { get; set; }
		/// <summary>The name the trigger already has, kept when its type has since been deleted (shown, and matched by name).</summary>
		public string CallTypeName { get; set; }
	}

	public class ProtocolQuestionInput
	{
		public string Key { get; set; }
		public int Id { get; set; }
		public string Text { get; set; }
		public List<ProtocolAnswerInput> Answers { get; set; } = new List<ProtocolAnswerInput>();
	}

	public class ProtocolAnswerInput
	{
		public string Key { get; set; }
		public int Id { get; set; }
		public string Text { get; set; }
		public int Weight { get; set; }
	}

	/// <summary>The protocol detail page (Protocols/View).</summary>
	public class ViewProtocolModel
	{
		public DispatchProtocol Protocol { get; set; }
		public Department Department { get; set; }
		public List<string> TriggerLines { get; set; } = new List<string>();
		public int CallsLast30Days { get; set; }
		public bool CanModify { get; set; }
		public bool CanDelete { get; set; }
		public string CreatedByName { get; set; }
		public string UpdatedByName { get; set; }
		public string Message { get; set; }
	}

	/// <summary>A protocol as the call forms' protocol panel reads it: no text, no attachment bytes.</summary>
	public class ProtocolJson
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string Description { get; set; }
		public int MinimumWeight { get; set; }
		/// <summary>0 inactive, 1 active (matched, no questions), 2 pending (matched, questions to answer).</summary>
		public int State { get; set; }
		/// <summary>The matched trigger in words ("Matched: Structure Fire · Priority High"); empty when none matched.</summary>
		public string Reason { get; set; }
		public int? MatchedTriggerType { get; set; }
		/// <summary>The protocol has questions. A protocol that did not match comes without them; the panel reads them when it is picked.</summary>
		public bool HasQuestions { get; set; }
		public List<ProtocolQuestionJson> Questions { get; set; } = new List<ProtocolQuestionJson>();
		public List<ProtocolAttachmentJson> Attachments { get; set; } = new List<ProtocolAttachmentJson>();
	}

	public class ProtocolQuestionJson
	{
		public int Id { get; set; }
		public string Question { get; set; }
		public List<ProtocolAnswerJson> Answers { get; set; } = new List<ProtocolAnswerJson>();
	}

	public class ProtocolAnswerJson
	{
		public int Id { get; set; }
		public string Answer { get; set; }
		public int Weight { get; set; }
	}

	public class ProtocolAttachmentJson
	{
		public int Id { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
		public long? Size { get; set; }
	}

	/// <summary>The web GetProtocol JSON: the protocol without attachment bytes (D20).</summary>
	public class ProtocolDetailJson : ProtocolJson
	{
		public bool IsDisabled { get; set; }
		public string ProtocolText { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
	}
}
