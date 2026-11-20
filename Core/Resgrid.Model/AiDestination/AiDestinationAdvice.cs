using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where a piece of destination advice stands (persisted).</summary>
	public enum AiDestinationAdviceStates
	{
		/// <summary>Shown on the call; nobody acted on it yet.</summary>
		Open = 1,

		/// <summary>A person set the call's destination to the recommendation.</summary>
		Applied = 2,

		/// <summary>A person dismissed it.</summary>
		Dismissed = 3,

		/// <summary>Newer advice replaced it, or the call's destination already matches it.</summary>
		Superseded = 4
	}

	/// <summary>What asked for a destination review (persisted).</summary>
	public enum AiDestinationReviewTriggers
	{
		/// <summary>A person pressed "Ask Enhanced AI".</summary>
		OnDemand = 1,

		/// <summary>The call was created.</summary>
		CallCreated = 2,

		/// <summary>The call's nature, note, type or priority changed.</summary>
		CallUpdated = 3,

		/// <summary>A call note was added.</summary>
		NoteAdded = 4,

		/// <summary>A destination's availability or capabilities changed (diversion, capability down).</summary>
		AvailabilityChanged = 5
	}

	/// <summary>
	/// Enhanced AI's destination advice for a call (M0323): the POI it recommends from the rule-ranked candidates, the
	/// capabilities it believes the patient or incident needs (read from the nature, note and call notes), its reasoning and
	/// confidence. Advice never changes the destination by itself (plan D4): a person applies or dismisses it. One Open row per
	/// call at a time; a new one supersedes the old. No foreign keys; purged with the department.
	/// </summary>
	[Table("AiDestinationAdvices")]
	public class AiDestinationAdvice : IEntity
	{
		[Key]
		[Required]
		public string AiDestinationAdviceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>The call's destination POI when the advice was made (null = none or a free destination).</summary>
		public int? CurrentPoiId { get; set; }

		/// <summary>The recommended destination POI; null when the advice is "keep the current destination".</summary>
		public int? RecommendedPoiId { get; set; }

		/// <summary>Comma-separated POI capability ids the model judged necessary (only ids the department defines).</summary>
		public string RequiredCapabilityIds { get; set; }

		/// <summary>Short reasoning shown to dispatchers and crews (model output; never applied automatically).</summary>
		[MaxLength(2000)]
		public string Rationale { get; set; }

		/// <summary>0..1.</summary>
		public double Confidence { get; set; }

		/// <summary>True when the recommendation differs from <see cref="CurrentPoiId"/> (an upgrade or change, not a confirmation).</summary>
		public bool IsChange { get; set; }

		/// <summary>An <see cref="AiDestinationReviewTriggers"/> value.</summary>
		public int ReviewTrigger { get; set; }

		/// <summary>An <see cref="AiDestinationAdviceStates"/> value.</summary>
		public int State { get; set; } = (int)AiDestinationAdviceStates.Open;

		/// <summary>SHA-256 of the inputs (call text digest, candidates, current destination) so an unchanged call is not re-asked.</summary>
		[MaxLength(64)]
		public string InputDigest { get; set; }

		[MaxLength(64)]
		public string PromptVersion { get; set; }

		[MaxLength(100)]
		public string ModelRevision { get; set; }

		public DateTime CreatedOn { get; set; }

		public string ResolvedByUserId { get; set; }

		public DateTime? ResolvedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return AiDestinationAdviceId; }
			set { AiDestinationAdviceId = (string)value; }
		}

		[NotMapped]
		public string TableName => "AiDestinationAdvices";

		[NotMapped]
		public string IdName => "AiDestinationAdviceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A request to review a call's destination (M0323), queued by the triggers and processed by worker 81. At most one open
	/// (unprocessed) row per call: later triggers fold into it, and it is due <c>AiDestinationConfig.DebounceSeconds</c> after
	/// <see cref="RequestedOn"/>, so a burst of notes costs one model call. A trigger that arrives while a worker holds the row moves
	/// <see cref="RequestedOn"/> forward, and the worker then leaves the row open for another run.
	/// </summary>
	[Table("AiDestinationReviews")]
	public class AiDestinationReview : IEntity
	{
		[Key]
		[Required]
		public string AiDestinationReviewId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>An <see cref="AiDestinationReviewTriggers"/> value (the latest).</summary>
		public int ReviewTrigger { get; set; }

		public DateTime RequestedOn { get; set; }

		/// <summary>When a worker claimed it (lease); null while waiting.</summary>
		public DateTime? ClaimedOn { get; set; }

		public DateTime? ProcessedOn { get; set; }

		[MaxLength(200)]
		public string Outcome { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return AiDestinationReviewId; }
			set { AiDestinationReviewId = (string)value; }
		}

		[NotMapped]
		public string TableName => "AiDestinationReviews";

		[NotMapped]
		public string IdName => "AiDestinationReviewId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
