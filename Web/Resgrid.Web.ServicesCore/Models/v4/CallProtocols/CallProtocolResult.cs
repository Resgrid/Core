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
	}

	public class ProtocolTriggerResultData
	{
		public string Id { get; set; }

		public int Type { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public int? Priority { get; set; }

		public string CallType { get; set; }

		public string Geofence { get; set; }
	}

	public class ProtocolTriggerAttachmentResultData
	{
		public string Id { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }
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
