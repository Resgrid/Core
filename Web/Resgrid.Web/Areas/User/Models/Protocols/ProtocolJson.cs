using System;
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	public class ProtocolJson
	{
		public int Id { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public string Code { get; set; }

		public bool IsDisabled { get; set; }

		public string Description { get; set; }

		public string ProtocolText { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int MinimumWeight { get; set; }

		public string UpdatedByUserId { get; set; }

		public List<ProtocolTriggerJson> Triggers { get; set; }

		public List<ProtocolTriggerAttachmentJson> Attachments { get; set; }

		public List<ProtocolTriggerQuestionJson> Questions { get; set; }

		public int State { get; set; }
	}

	public class ProtocolTriggerJson
	{
		public int Id { get; set; }

		public int Type { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public int? Priority { get; set; }

		public string CallType { get; set; }

		public string Geofence { get; set; }
	}

	public class ProtocolTriggerAttachmentJson
	{
		public int Id { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }
	}

	public class ProtocolTriggerQuestionJson
	{
		public int Id { get; set; }

		public string Question { get; set; }

		public List<ProtocolQuestionAnswerJson> Answers { get; set; }
	}

	public class ProtocolQuestionAnswerJson
	{
		public int Id { get; set; }

		public string Answer { get; set; }

		public int Weight { get; set; }
	}
}
