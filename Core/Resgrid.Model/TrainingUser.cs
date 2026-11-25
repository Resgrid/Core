using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("TrainingUsers")]
	public class TrainingUser : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingUserId { get; set; }

		[Required]
		[ForeignKey("Training"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int TrainingId { get; set; }

		public virtual Training Training { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		public bool Viewed { get; set; }

		public DateTime? ViewedOn { get; set; }

		public bool Complete { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>
		/// The score of the member's latest quiz attempt (T-D1: always written, a failed retake included).
		/// </summary>
		public double Score { get; set; }

		/// <summary>
		/// The latest quiz attempt's result, stored when it was taken and never recomputed from the current quiz (T-D2,
		/// T-D20). Null when the training has no quiz or no attempt was made. <see cref="Complete"/> is true only for a
		/// finished training: a passed quiz, or a training without a quiz that was opened.
		/// </summary>
		public bool? Passed { get; set; }

		/// <summary>When this member was assigned (or re-assigned by a repeat or a material edit), UTC.</summary>
		public DateTime? AssignedOn { get; set; }

		/// <summary>The member's due moment, UTC: the end of the department-local day (TrainingDueDates).</summary>
		public DateTime? DueOn { get; set; }

		/// <summary>How the member came to be assigned (<see cref="TrainingAssignmentSources"/>).</summary>
		public int AssignmentSource { get; set; }

		/// <summary>The assignment notice was sent (or skipped as too old), UTC.</summary>
		public DateTime? NotifiedOn { get; set; }

		/// <summary>The last due reminder, UTC, and the lead (days) it was sent for: a lead is reminded once.</summary>
		public DateTime? RemindedOn { get; set; }

		public int? LastReminderLeadDays { get; set; }

		/// <summary>The last overdue notice, UTC (repeating cadence: <see cref="Training.OverdueRepeatDays"/>).</summary>
		public DateTime? OverdueNotifiedOn { get; set; }

		/// <summary>An officer removed this completed assignee: the completion is kept, the member is no longer tracked.</summary>
		public DateTime? RemovedOn { get; set; }

		public string RemovedByUserId { get; set; }

		/// <summary>Extension (R-C2): the due date before the first extension, why, who approved it and when.</summary>
		public DateTime? OriginalDueOn { get; set; }

		public string ExtensionReason { get; set; }

		public string ExtendedByUserId { get; set; }

		public DateTime? ExtendedOn { get; set; }

		/// <summary>Exemption (R-C2): shown as exempt, never as complete; optional expiry, UTC.</summary>
		public DateTime? ExemptOn { get; set; }

		public string ExemptReason { get; set; }

		public string ExemptByUserId { get; set; }

		public DateTime? ExemptUntil { get; set; }

		/// <summary>Quiz attempts submitted in the current cycle (the attempt limit counts these).</summary>
		public int AttemptCount { get; set; }

		/// <summary>The assignment cycle: 0 for the first, then one more for each repeat or material re-assignment.</summary>
		public int Cycle { get; set; }

		/// <summary>True while an exemption is in force.</summary>
		public bool IsExemptOn(DateTime utcNow) => ExemptOn.HasValue && (!ExemptUntil.HasValue || ExemptUntil.Value > utcNow);

		/// <summary>An assignment the member still has to do: not removed, not complete, not exempt.</summary>
		public bool IsOpenOn(DateTime utcNow) => !RemovedOn.HasValue && !Complete && !IsExemptOn(utcNow);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingUserId; }
			set { TrainingUserId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingUsers";

		[NotMapped]
		public string IdName => "TrainingUserId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Training", "User" };
	}
}
