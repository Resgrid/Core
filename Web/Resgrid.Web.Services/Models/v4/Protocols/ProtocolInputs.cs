using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Protocols
{
	/// <summary>
	/// A protocol to create (Protocols/SaveProtocol) or update in place (Protocols/UpdateProtocol). On update, triggers, questions
	/// and answers are matched by their ids: rows with an id are changed, rows without one (0 or blank) are added, and the
	/// protocol's rows that are left out are removed. An id that is not one of this protocol's rows is refused.
	/// </summary>
	public class ProtocolInput
	{
		/// <summary>The protocol to update (UpdateProtocol only; ignored by SaveProtocol).</summary>
		public int ProtocolId { get; set; }

		/// <summary>Name, up to 100 characters.</summary>
		public string Name { get; set; }

		/// <summary>Code, up to 16 letters, digits, spaces and . _ / - (stored upper case).</summary>
		public string Code { get; set; }

		/// <summary>Rich text; up to 500 characters of text (markup not counted). Sanitized on save.</summary>
		public string Description { get; set; }

		/// <summary>Rich text, sanitized on save.</summary>
		public string ProtocolText { get; set; }

		/// <summary>The score the answers must reach for a suggested protocol to apply.</summary>
		public int MinimumWeight { get; set; }

		/// <summary>A disabled protocol is never suggested or attached.</summary>
		public bool IsDisabled { get; set; }

		public List<ProtocolTriggerInput> Triggers { get; set; } = new List<ProtocolTriggerInput>();

		public List<ProtocolQuestionInput> Questions { get; set; } = new List<ProtocolQuestionInput>();

		/// <summary>UpdateProtocol: attachments of this protocol to remove. Files are added through the web editor.</summary>
		public List<int> RemoveAttachmentIds { get; set; } = new List<int>();
	}

	public class ProtocolTriggerInput
	{
		/// <summary>The trigger's id on update; 0 for a new one.</summary>
		public int ProtocolTriggerId { get; set; }

		/// <summary>0 call priority, 1 call type, 2 call priority and type.</summary>
		public int Type { get; set; }

		/// <summary>UTC. The trigger applies to calls logged at or after this time.</summary>
		public DateTime? StartsOn { get; set; }

		/// <summary>UTC. The trigger applies to calls logged at or before this time.</summary>
		public DateTime? EndsOn { get; set; }

		/// <summary>The department call priority id (types 0 and 2).</summary>
		public int? Priority { get; set; }

		/// <summary>The department call type id (types 1 and 2); null means "no type".</summary>
		public int? CallTypeId { get; set; }
	}

	public class ProtocolQuestionInput
	{
		public int ProtocolQuestionId { get; set; }

		public string Question { get; set; }

		public List<ProtocolAnswerInput> Answers { get; set; } = new List<ProtocolAnswerInput>();
	}

	public class ProtocolAnswerInput
	{
		public int ProtocolQuestionAnswerId { get; set; }

		public string Answer { get; set; }

		public int Weight { get; set; }
	}

	/// <summary>Protocols/SetDisabled.</summary>
	public class SetProtocolDisabledInput
	{
		public int ProtocolId { get; set; }

		public bool Disabled { get; set; }
	}

	/// <summary>The saved protocol (SaveProtocol, UpdateProtocol, SetDisabled), or the reasons it was refused.</summary>
	public class SaveProtocolResult : StandardApiResponseV4Base
	{
		public ProtocolResultData Data { get; set; }

		/// <summary>Validation errors (resource keys), empty when saved.</summary>
		public List<string> Errors { get; set; } = new List<string>();
	}

	/// <summary>Protocols/GetProtocolsForCall: every enabled protocol evaluated for a call's priority, type and time.</summary>
	public class GetProtocolsForCallResult : StandardApiResponseV4Base
	{
		public List<ProtocolCallMatchData> Data { get; set; } = new List<ProtocolCallMatchData>();
	}

	/// <summary>A protocol evaluated for a call: the same matcher the web call forms use.</summary>
	public class ProtocolCallMatchData
	{
		public string ProtocolId { get; set; }

		public string Name { get; set; }

		public string Code { get; set; }

		public int MinimumWeight { get; set; }

		/// <summary>0 inactive (no trigger matched; it can still be picked), 1 active (matched, no questions), 2 pending (matched, questions to answer).</summary>
		public int State { get; set; }

		/// <summary>The most specific trigger that matched, when one did.</summary>
		public string MatchedTriggerId { get; set; }

		/// <summary>That trigger's type (0 priority, 1 type, 2 priority and type).</summary>
		public int? MatchedTriggerType { get; set; }

		public List<ProtocolTriggerQuestionResult> Questions { get; set; } = new List<ProtocolTriggerQuestionResult>();
	}
}
