using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Protocols
{
	/// <summary>
	/// Result containing all the data required to populate the New Call form
	/// </summary>
	public class GetAllProtocolsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ProtocolResultData> Data { get; set; }
	}

	/// <summary>
	/// Details of a protocol
	/// </summary>
	public class ProtocolResultData
	{
		/// <summary>
		/// Protocol id
		/// </summary>
		public string ProtocolId { get; set; }

		/// <summary>
		/// Department id
		/// </summary>
		public int DepartmentId { get; set; }

		/// <summary>
		/// Name of the Protocol
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Protocol code
		/// </summary>
		public string Code { get; set; }

		/// <summary>
		/// This this protocol disabled
		/// </summary>
		public bool IsDisabled { get; set; }

		/// <summary>
		/// Protocol description
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Text of the protocol
		/// </summary>
		public string ProtocolText { get; set; }

		/// <summary>
		/// UTC date and time when the Protocol was created
		/// </summary>
		public DateTime CreatedOn { get; set; }

		/// <summary>
		/// UserId of the user who created the protocol
		/// </summary>
		public string CreatedByUserId { get; set; }

		/// <summary>
		/// UTC timestamp of when the Protocol was updated
		/// </summary>
		public DateTime? UpdatedOn { get; set; }

		/// <summary>
		/// Minimum triggering Weight of the Protocol
		/// </summary>
		public int MinimumWeight { get; set; }

		/// <summary>
		/// UserId that last updated the Protocol
		/// </summary>
		public string UpdatedByUserId { get; set; }

		/// <summary>
		/// Triggers used to activate this Protocol
		/// </summary>
		public List<ProtocolTriggerResult> Triggers { get; set; }

		/// <summary>
		/// Attachments for this Protocol
		/// </summary>
		public List<ProtocolTriggerAttachmentResult> Attachments { get; set; }

		/// <summary>
		/// Questions used to determine if this Protocol needs to be used or not
		/// </summary>
		public List<ProtocolTriggerQuestionResult> Questions { get; set; }

		/// <summary>
		/// State type
		/// </summary>
		public int State { get; set; }
	}

	public class ProtocolTriggerResult
	{
		public string ProtocolTriggerId { get; set; }

		public int Type { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public int? Priority { get; set; }

		public string CallType { get; set; }

		public string Geofence { get; set; }
	}

	public class ProtocolTriggerAttachmentResult
	{
		public string ProtocolTriggerAttachmentId { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }
	}

	public class ProtocolTriggerQuestionResult
	{
		public string ProtocolTriggerQuestionId { get; set; }

		public string Question { get; set; }

		public List<ProtocolQuestionAnswerResult> Answers { get; set; }
	}

	public class ProtocolQuestionAnswerResult
	{
		public string ProtocolQuestionAnswerId { get; set; }

		public string Answer { get; set; }

		public int Weight { get; set; }
	}
}
