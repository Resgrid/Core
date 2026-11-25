using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
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
		/// <summary>P3: the block editor's document (ContentJson); null when the draft is P1 html (the html editor and "Convert to blocks").</summary>
		public string ContentJson { get; set; }
		/// <summary>
		/// P4: the case flow builder's decision graph (Dispatch protocols); null when the builder was not on the page (the draft's graph is
		/// kept), blank to remove it.
		/// </summary>
		public string DecisionGraphJson { get; set; }
		public int MinimumWeight { get; set; }
		public bool IsDisabled { get; set; }
		public List<ProtocolTriggerInput> Triggers { get; set; } = new List<ProtocolTriggerInput>();
		public List<ProtocolQuestionInput> Questions { get; set; } = new List<ProtocolQuestionInput>();
		/// <summary>Existing attachments to remove on save (Edit).</summary>
		public List<int> RemoveAttachmentIds { get; set; } = new List<int>();
		/// <summary>P1 library fields, audience and draft settings.</summary>
		public ProtocolLibraryEditorFields Library { get; set; } = new ProtocolLibraryEditorFields();
		/// <summary>The protocol's RowVersion as loaded; a save against an older one is refused (R-P8).</summary>
		public long RowVersion { get; set; }
		/// <summary>The open draft's RowVersion as loaded (0 when there was none).</summary>
		public long DraftRowVersion { get; set; }
		/// <summary>"save" (default) or "submit" (save, then submit the draft for review).</summary>
		public string Command { get; set; }

		// Shown, never bound.
		[BindNever] public int ProtocolId { get; set; }
		[BindNever] public bool IsNew { get; set; }
		[BindNever] public List<DispatchProtocolAttachment> ExistingAttachments { get; set; } = new List<DispatchProtocolAttachment>();
		[BindNever] public List<DepartmentCallPriority> CallPriorities { get; set; } = new List<DepartmentCallPriority>();
		[BindNever] public List<CallType> CallTypes { get; set; } = new List<CallType>();
		/// <summary>P4: groups with a geofence (trigger zones) and the department's POI types (trigger occupancy types).</summary>
		[BindNever] public List<DepartmentGroup> GeofenceGroups { get; set; } = new List<DepartmentGroup>();
		[BindNever] public List<PoiType> PoiTypes { get; set; } = new List<PoiType>();
		[BindNever] public string TimeZoneName { get; set; }
		[BindNever] public ProtocolEditorLists Lists { get; set; } = new ProtocolEditorLists();
		[BindNever] public int? DraftVersionId { get; set; }
		[BindNever] public int? DraftStatus { get; set; }
		[BindNever] public string DraftUpdatedBy { get; set; }
		[BindNever] public DateTime? DraftUpdatedOn { get; set; }
		/// <summary>The draft is in review or approved: its content is locked until it is withdrawn.</summary>
		[BindNever] public bool ContentLocked { get; set; }
		[BindNever] public string CurrentVersionLabel { get; set; }
		[BindNever] public bool CanApprove { get; set; }
		[BindNever] public string Message { get; set; }
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

		// Protocols P4 conditions (shared trigger engine): all optional, all must hold when set.
		public int? GeofenceGroupId { get; set; }
		/// <summary>Department-local days, 0 = Sunday … 6 = Saturday; none = every day.</summary>
		public List<int> Days { get; set; } = new List<int>();
		/// <summary>Department-local "HH:mm"; with <see cref="EndTime"/>. A start after the end runs overnight.</summary>
		public string StartTime { get; set; }
		public string EndTime { get; set; }
		public string Keywords { get; set; }
		public int? PoiTypeId { get; set; }
		/// <summary><see cref="CallSources"/> values (intake classification).</summary>
		public List<int> CallSources { get; set; } = new List<int>();
		/// <summary>The name the trigger already has, kept when its type has since been deleted (shown, and matched by name).</summary>
		public string CallTypeName { get; set; }
	}

	/// <summary>P4: one trigger row's conditions as the _TriggerConditions partial renders them.</summary>
	public class ProtocolTriggerConditionsView
	{
		public string Key { get; set; }
		public ProtocolTriggerInput Trigger { get; set; }
		public List<DepartmentGroup> Groups { get; set; } = new List<DepartmentGroup>();
		public List<PoiType> PoiTypes { get; set; } = new List<PoiType>();
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
