using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DispatchProtocols")]
	public class DispatchProtocol : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		/// <summary>Longest code a protocol can carry: room for determinant-style codes (09-E-01, 26A01) and SOG numbers (SOG-204).</summary>
		public const int CodeMaxLength = 16;

		/// <summary>Longest description, counted on its text rather than its markup.</summary>
		public const int DescriptionTextMaxLength = 500;

		[Required]
		[MaxLength(CodeMaxLength)]
		public string Code { get; set; }

		public bool IsDisabled { get; set; }

		/// <summary>
		/// A deleted protocol stays as a row because the calls it was used on cite it. It is left out of lists, matching and
		/// new calls; a call that already carries it still shows it.
		/// </summary>
		public bool IsDeleted { get; set; }

		public DateTime? DeletedOn { get; set; }

		/// <summary>Rich text. Its text, not its markup, is limited to <see cref="DescriptionTextMaxLength"/> (validated on save).</summary>
		public string Description { get; set; }

		public string ProtocolText { get; set; }

		[Required]
		public DateTime CreatedOn { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int MinimumWeight { get; set; }

		[Required]
		public string UpdatedByUserId { get; set; }

		public virtual ICollection<DispatchProtocolTrigger> Triggers { get; set; }

		public virtual ICollection<DispatchProtocolAttachment> Attachments { get; set; }

		public virtual ICollection<DispatchProtocolQuestion> Questions { get; set; }

		// ---- P1 library and lifecycle (M0343) ----------------------------------------------------------------------------

		public const int NumberMaxLength = 32;
		public const int TagsMaxLength = 500;

		/// <summary><see cref="ProtocolKinds"/>; every protocol before P1 is Dispatch.</summary>
		public int Kind { get; set; }

		public int? CategoryId { get; set; }

		/// <summary>The department's number for it (document numbering tokens, e.g. SOG-2026-014); optional.</summary>
		[MaxLength(NumberMaxLength)]
		public string Number { get; set; }

		/// <summary>Comma separated tags.</summary>
		[MaxLength(TagsMaxLength)]
		public string Tags { get; set; }

		/// <summary>Who keeps it current: receives review reminders and member suggestions.</summary>
		public string OwnerUserId { get; set; }

		/// <summary><see cref="ProtocolStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The version in force (Published), or the last one before it was retired.</summary>
		public int? CurrentVersionId { get; set; }

		/// <summary>Days between reviews; null for no review cycle.</summary>
		public int? ReviewIntervalDays { get; set; }

		public DateTime? NextReviewOn { get; set; }

		/// <summary>The last publish or "reviewed, no change" stamp.</summary>
		public DateTime? LastReviewedOn { get; set; }

		/// <summary><see cref="ProtocolReviewReminderStages"/> sent for the current <see cref="NextReviewOn"/>.</summary>
		public int ReviewReminderStage { get; set; }

		/// <summary>Bumped on every library write; an edit against an older value is refused (R-P8).</summary>
		public long RowVersion { get; set; }

		/// <summary>Keep this protocol out of every AI index and answer (decision 14, licensed content).</summary>
		public bool ExcludeFromAi { get; set; }

		/// <summary>Licensed (third-party) content: never public, never shared on (P4/P5 guardrail, stored now).</summary>
		public bool IsLicensedContent { get; set; }

		/// <summary>Legal hold: none of its versions is purged by retention, whatever the kind's period (R-P10).</summary>
		public bool LegalHold { get; set; }

		// ---- P2 distribution and acknowledgment (M0354) -------------------------------------------------------------------

		/// <summary>
		/// Days a member has to acknowledge a version, counted from the day they were assigned it (publish, or the day they entered
		/// the audience, R-P3); null for the default (<see cref="ProtocolAcknowledgmentRules.DefaultDueDays"/>).
		/// </summary>
		public int? AckDueDays { get; set; }

		/// <summary>Where this protocol sits in a new member's onboarding bundle (lower first; ties by code).</summary>
		public int AckBundleOrder { get; set; }

		/// <summary>A baseline Training whose quiz gates acknowledgment (free quiz-gated acknowledgment); frozen per version on publish.</summary>
		public int? AckTrainingId { get; set; }

		/// <summary>Comma separated standards tags (CFAI, CAAS, CALEA, NFPA, ISO 45001, custom) for the proof-of-compliance report (LMS).</summary>
		[MaxLength(TagsMaxLength)]
		public string StandardsTags { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsBulletin => Kind == (int)ProtocolKinds.Bulletin;

		/// <summary>Audience rules, loaded by the library service when asked for (never saved through this row).</summary>
		[NotMapped]
		public List<ProtocolAudience> Audiences { get; set; }

		/// <summary>The version in force, loaded by the library service when asked for.</summary>
		[NotMapped]
		public ProtocolVersion CurrentVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsPublished => Status == (int)ProtocolStatuses.Published && CurrentVersionId.HasValue;

		[NotMapped]
		public ProtocolStates State { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchProtocolId; }
			set { DispatchProtocolId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchProtocols";

		[NotMapped]
		public string IdName => "DispatchProtocolId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Triggers", "Attachments", "Questions", "State", "Audiences", "CurrentVersion", "IsPublished", "IsBulletin" };
	}
}
