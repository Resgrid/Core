using Resgrid.Model;
using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Protocols
{
	public class ProtocolResult
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

		public List<ProtocolTriggerResult> Triggers { get; set; }

		public List<ProtocolTriggerAttachmentResult> Attachments { get; set; }

		public List<ProtocolTriggerQuestionResult> Questions { get; set; }

		public int State { get; set; }

		public static ProtocolResult Convert(DispatchProtocol dp)
		{
			var protocol = new ProtocolResult();
			protocol.Id = dp.DispatchProtocolId;
			protocol.DepartmentId = dp.DepartmentId;
			protocol.Name = dp.Name;
			protocol.Code = dp.Code;
			protocol.IsDisabled = dp.IsDisabled;
			protocol.Description = dp.Description;
			protocol.ProtocolText = dp.ProtocolText;
			protocol.CreatedOn = dp.CreatedOn;
			protocol.CreatedByUserId = dp.CreatedByUserId;
			protocol.UpdatedOn = dp.UpdatedOn;
			protocol.MinimumWeight = dp.MinimumWeight;
			protocol.UpdatedByUserId = dp.UpdatedByUserId;
			protocol.State = (int)dp.State;
			protocol.Triggers = new List<ProtocolTriggerResult>();
			protocol.Attachments = new List<ProtocolTriggerAttachmentResult>();
			protocol.Questions = new List<ProtocolTriggerQuestionResult>();

			foreach (var t in dp.Triggers)
			{
				var trigger = new ProtocolTriggerResult();
				trigger.Id = t.DispatchProtocolTriggerId;
				trigger.Type = t.Type;
				trigger.StartsOn = t.StartsOn;
				trigger.EndsOn = t.EndsOn;
				trigger.Priority = t.Priority;
				trigger.CallType = t.CallType;
				trigger.Geofence = t.Geofence;

				protocol.Triggers.Add(trigger);
			}

			foreach (var a in dp.Attachments)
			{
				var attachment = new ProtocolTriggerAttachmentResult();
				attachment.Id = a.DispatchProtocolAttachmentId;
				attachment.FileName = a.FileName;
				attachment.FileType = a.FileType;

				protocol.Attachments.Add(attachment);
			}

			foreach (var q in dp.Questions)
			{
				var question = new ProtocolTriggerQuestionResult();
				question.Id = q.DispatchProtocolQuestionId;
				question.Question = q.Question;
				question.Answers = new List<ProtocolQuestionAnswerResult>();

				foreach (var a in q.Answers)
				{
					var answer = new ProtocolQuestionAnswerResult();
					answer.Id = a.DispatchProtocolQuestionAnswerId;
					answer.Answer = a.Answer;
					answer.Weight = a.Weight;

					question.Answers.Add(answer);
				}

				protocol.Questions.Add(question);
			}


			return protocol;
		}
	}

	public class ProtocolTriggerResult
	{
		public int Id { get; set; }

		public int Type { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public int? Priority { get; set; }

		public string CallType { get; set; }

		public string Geofence { get; set; }
	}

	public class ProtocolTriggerAttachmentResult
	{
		public int Id { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }
	}

	public class ProtocolTriggerQuestionResult
	{
		public int Id { get; set; }

		public string Question { get; set; }

		public List<ProtocolQuestionAnswerResult> Answers { get; set; }
	}

	public class ProtocolQuestionAnswerResult
	{
		public int Id { get; set; }

		public string Answer { get; set; }

		public int Weight { get; set; }
	}
}
