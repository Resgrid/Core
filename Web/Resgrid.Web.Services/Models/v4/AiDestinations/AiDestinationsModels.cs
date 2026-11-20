using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.AiDestinations
{
	/// <summary>A call's Enhanced AI destination advice, or why there is none.</summary>
	public class AiDestinationAdviceResult : StandardApiResponseV4Base
	{
		public AiDestinationAdviceResultData Data { get; set; } = new AiDestinationAdviceResultData();
	}

	public class AiDestinationAdviceResultData
	{
		/// <summary>The department may use destination advice now (feature, add-on, operator model, not Advanced Data Protection).</summary>
		public bool Available { get; set; }

		/// <summary>
		/// What the request did, or why it did nothing: Advised, Unchanged, Applied, Dismissed, Disabled, NotEntitled, Unconfigured,
		/// ProtectionUnsupported, CallNotOpen, NoLocation, NoCandidates, Busy, BudgetExhausted, Unavailable, InvalidOutput, NotFound,
		/// NotOpen, NothingToApply, DestinationGone, DestinationClosed, CallChanged. Null on GetAdvice.
		/// </summary>
		public string Outcome { get; set; }

		/// <summary>The caller may edit the call, so may ask, apply and dismiss.</summary>
		public bool CanApply { get; set; }

		/// <summary>The call's open advice (or the advice the operation acted on); null when there is none.</summary>
		public AiDestinationAdviceData Advice { get; set; }
	}

	public class AiDestinationAdviceData
	{
		public string Id { get; set; }

		public int CallId { get; set; }

		/// <summary>The call's destination POI when the advice was made; null = none or a free destination.</summary>
		public int? CurrentPoiId { get; set; }

		public string CurrentPoiName { get; set; }

		/// <summary>The recommended destination POI; null = keep the current destination.</summary>
		public int? RecommendedPoiId { get; set; }

		public string RecommendedPoiName { get; set; }

		/// <summary>The recommended destination's current availability (0 Open, 1 Limited, 2 Diversion, 3 Closed); null = open, nothing reported.</summary>
		public int? RecommendedPoiStatus { get; set; }

		/// <summary>The capabilities the model judged necessary (department capability ids and names).</summary>
		public List<AiDestinationCapabilityData> RequiredCapabilities { get; set; } = new List<AiDestinationCapabilityData>();

		/// <summary>The model's short reasoning (at most 400 characters). Never applied automatically.</summary>
		public string Rationale { get; set; }

		/// <summary>0..1.</summary>
		public double Confidence { get; set; }

		/// <summary>The recommendation differs from the current destination.</summary>
		public bool IsChange { get; set; }

		/// <summary>
		/// True when the advice is a change whose confidence meets the current AiDestinationConfig.MinimumConfidence threshold. A comparison
		/// made when the advice is read, not a record of delivery: whether a call note or push went out was decided against the threshold in
		/// force when the advice was written.
		/// </summary>
		public bool IsConfident { get; set; }

		/// <summary>1 Open, 2 Applied, 3 Dismissed, 4 Superseded.</summary>
		public int State { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime CreatedOn { get; set; }

		/// <summary>What asked for it: 1 OnDemand, 2 CallCreated, 3 CallUpdated, 4 NoteAdded, 5 AvailabilityChanged.</summary>
		public int Trigger { get; set; }
	}

	public class AiDestinationCapabilityData
	{
		public string Id { get; set; }

		public string Name { get; set; }
	}

	/// <summary>Ask Enhanced AI for the call's destination now.</summary>
	public class RequestAiDestinationAdviceInput
	{
		[Required]
		[Range(1, int.MaxValue)]
		public int CallId { get; set; }
	}

	/// <summary>Set the call's destination to the advice's recommendation.</summary>
	public class ApplyAiDestinationAdviceInput
	{
		[Required]
		[MaxLength(36)]
		public string AdviceId { get; set; }
	}

	/// <summary>Dismiss the advice; the reason (optional) is written to the call's notes when the advice was a change.</summary>
	public class DismissAiDestinationAdviceInput
	{
		[Required]
		[MaxLength(36)]
		public string AdviceId { get; set; }

		[MaxLength(200)]
		public string Reason { get; set; }
	}
}
