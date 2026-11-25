using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallProtocols
{
	/// <summary>
	/// Depicts a call protocol in the Resgrid system.
	/// </summary>
	public class CallProtocolResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CallProtocolResultData Data { get; set; }
	}

	/// <summary>
	/// Call protocol data
	/// </summary>
	public class CallProtocolResultData
	{
		/// <summary>
		/// Call Protocol Id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Department id this protocol is for
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Name of the protocol
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Protocol quick code
		/// </summary>
		public string Code { get; set; }

		/// <summary>
		/// Is the protocol disabled
		/// </summary>
		public bool IsDisabled { get; set; }

		/// <summary>
		/// Protocol Description
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Actual protocol text
		/// </summary>
		public string ProtocolText { get; set; }

		/// <summary>
		/// UTC of when the protocol was created
		/// </summary>
		public DateTime CreatedOn { get; set; }

		/// <summary>
		/// Who created the procotol
		/// </summary>
		public string CreatedByUserId { get; set; }

		/// <summary>
		/// When/if the procotol was updated
		/// </summary>
		public DateTime? UpdatedOn { get; set; }

		/// <summary>
		/// Minimum weight to activate the procotol based on answers
		/// </summary>
		public int MinimumWeight { get; set; }

		/// <summary>
		/// Who updated the protocol
		/// </summary>
		public string UpdatedByUserId { get; set; }

		public List<ProtocolTriggerResultData> Triggers { get; set; }

		public List<ProtocolTriggerAttachmentResultData> Attachments { get; set; }

		public List<ProtocolTriggerQuestionResultData> Questions { get; set; }

		public int State { get; set; }

		/// <summary>On a call (Calls/GetCallExtraData): the call's protocol row id, for CallProtocols/RemoveCallProtocol.</summary>
		public string CallProtocolId { get; set; }

		/// <summary>On a call: how it came onto the call (0 trigger, 1 picked by hand; null before answers were kept).</summary>
		public int? Source { get; set; }

		/// <summary>On a call: the score recomputed from the recorded answers.</summary>
		public int? Score { get; set; }

		/// <summary>On a call: the questions and the answers chosen, in words, as recorded when it was attached.</summary>
		public List<CallProtocolAnswerResultData> RecordedAnswers { get; set; }
	}

	public class CallProtocolAnswerResultData
	{
		public string Question { get; set; }

		/// <summary>Null when the question was left unanswered.</summary>
		public string Answer { get; set; }

		public int Weight { get; set; }
	}

	public class ProtocolTriggerResultData
	{
		public string Id { get; set; }

		public int Type { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public int? Priority { get; set; }

		public string CallType { get; set; }

		public int? CallTypeId { get; set; }

		public string Geofence { get; set; }
	}

	public class ProtocolTriggerAttachmentResultData
	{
		public string Id { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }

		public long? Size { get; set; }
	}

	public class ProtocolTriggerQuestionResultData
	{
		public string Id { get; set; }

		public string Question { get; set; }

		public List<ProtocolQuestionAnswerResultData> Answers { get; set; }
	}

	public class ProtocolQuestionAnswerResultData
	{
		public string Id { get; set; }

		public string Answer { get; set; }

		public int Weight { get; set; }
	}
}
