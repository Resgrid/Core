using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CommunicationTestResults")]
	public class CommunicationTestResult : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid CommunicationTestResultId { get; set; }

		[Required]
		public Guid CommunicationTestRunId { get; set; }

		[ForeignKey("CommunicationTestRunId")]
		public virtual CommunicationTestRun CommunicationTestRun { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		public int Channel { get; set; }

		[MaxLength(500)]
		public string ContactValue { get; set; }

		[MaxLength(200)]
		public string ContactCarrier { get; set; }

		public int VerificationStatus { get; set; }

		/// <summary>
		/// Whether the member had this channel switched on in their own notification settings when
		/// the run was built. Recorded on the row rather than read back off the live profile so the
		/// report describes the run as it happened -- a member who turns SMS on the day after a test
		/// must not make that run look like it should have texted them. NULL on runs built before
		/// this was recorded; the report falls back to the current profile for those.
		/// </summary>
		public bool? ChannelEnabled { get; set; }

		/// <summary>
		/// The member's staffing level (their last UserState) when the run was built, or NULL when
		/// they had never set one. Stored with <see cref="StaffingLevelText"/> so a report read
		/// months later still shows the level the run actually saw.
		/// </summary>
		public int? StaffingLevel { get; set; }

		/// <summary>
		/// Display name of <see cref="StaffingLevel"/> as the department had it configured at run
		/// time. Snapshotted because a department can rename or delete a custom staffing level.
		/// </summary>
		[MaxLength(50)]
		public string StaffingLevelText { get; set; }

		/// <summary>
		/// Whether the department's Suppress (Mute) Staffing Levels setting muted this member for
		/// this run. Suppressed rows are still written and still reported -- the point of the report
		/// is to show who a real dispatch would and would not reach -- but nothing is sent to them.
		/// </summary>
		public bool Suppressed { get; set; }

		public bool SendAttempted { get; set; }

		public bool SendSucceeded { get; set; }

		public DateTime? SentOn { get; set; }

		public bool Responded { get; set; }

		public DateTime? RespondedOn { get; set; }

		[MaxLength(128)]
		public string ResponseToken { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommunicationTestResultId == Guid.Empty ? null : (object)CommunicationTestResultId.ToString(); }
			set { CommunicationTestResultId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "CommunicationTestResults";

		[NotMapped]
		public string IdName => "CommunicationTestResultId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CommunicationTestRun" };
	}
}
