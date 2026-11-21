using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Outcomes of an Enhanced AI destination review, an "Ask Enhanced AI" request, an apply or a dismiss (stored on
	/// <see cref="AiDestinationReview.Outcome"/> and returned by <see cref="Services.IAiDestinationAdvisorService"/>). Every
	/// outcome except <see cref="Applied"/> leaves the call's destination exactly as it was.
	/// </summary>
	public static class AiDestinationOutcomes
	{
		/// <summary>New advice was written.</summary>
		public const string Advised = "Advised";

		/// <summary>Nothing the model reads changed since the last advice, so it was not asked again.</summary>
		public const string Unchanged = "Unchanged";

		/// <summary>The host switch (AiDestinationConfig.Enabled) is off.</summary>
		public const string Disabled = "Disabled";

		/// <summary>The department may not use the feature (flag, Enhanced AI add-on or module switch).</summary>
		public const string NotEntitled = "NotEntitled";

		/// <summary>The operator's self-hosted model is not configured, reviewed and pinned.</summary>
		public const string Unconfigured = "Unconfigured";

		/// <summary>The department is enrolled in Advanced Data Protection; call content never reaches inference until the broker purpose exists (plan D9).</summary>
		public const string ProtectionUnsupported = "ProtectionUnsupported";

		/// <summary>The call is not open (Active or Pending), deleted, or another department's.</summary>
		public const string CallNotOpen = "CallNotOpen";

		/// <summary>A background review of a call with no destination POI and no matching destination rule (only "Ask Enhanced AI" covers those).</summary>
		public const string NotApplicable = "NotApplicable";

		/// <summary>The call has no usable location to rank destinations from.</summary>
		public const string NoLocation = "NoLocation";

		/// <summary>The department has no destination the call could go to.</summary>
		public const string NoCandidates = "NoCandidates";

		/// <summary>No inference slot came free in time; a background review is retried on a later pass.</summary>
		public const string Busy = "Busy";

		/// <summary>The department's monthly Enhanced AI token budget is used up.</summary>
		public const string BudgetExhausted = "BudgetExhausted";

		/// <summary>The model did not answer in time, or failed.</summary>
		public const string Unavailable = "Unavailable";

		/// <summary>The model's reply failed validation (an id outside the lists it was shown, no confidence, a link in the reasoning).</summary>
		public const string InvalidOutput = "InvalidOutput";

		/// <summary>A person set the call's destination to the recommendation.</summary>
		public const string Applied = "Applied";

		/// <summary>A person dismissed the advice.</summary>
		public const string Dismissed = "Dismissed";

		/// <summary>The advice does not exist in the department.</summary>
		public const string NotFound = "NotFound";

		/// <summary>The advice was already applied, dismissed or superseded.</summary>
		public const string NotOpen = "NotOpen";

		/// <summary>The advice is "keep the current destination", so there is nothing to apply.</summary>
		public const string NothingToApply = "NothingToApply";

		/// <summary>The recommended destination is no longer one of the department's destinations.</summary>
		public const string DestinationGone = "DestinationGone";

		/// <summary>The recommended destination reports itself closed now, so the advice cannot be applied (it stays open; ask again).</summary>
		public const string DestinationClosed = "DestinationClosed";

		/// <summary>
		/// The call's type, priority, nature, note or location changed while the model was answering, so the answer was discarded
		/// (nothing written or announced); a background review runs again for the current call.
		/// </summary>
		public const string CallChanged = "CallChanged";
	}

	/// <summary>The result of an advisor operation: the outcome and, when there is one, the advice it concerns.</summary>
	public class AiDestinationAdviceOutcome
	{
		public string Outcome { get; set; }

		public AiDestinationAdvice Advice { get; set; }

		public static AiDestinationAdviceOutcome Of(string outcome, AiDestinationAdvice advice = null) =>
			new AiDestinationAdviceOutcome { Outcome = outcome, Advice = advice };

		/// <summary>True when the operation produced or acted on advice (advised, unchanged with advice, applied, dismissed).</summary>
		public bool Success => Advice != null && (Outcome == AiDestinationOutcomes.Advised || Outcome == AiDestinationOutcomes.Unchanged ||
			Outcome == AiDestinationOutcomes.Applied || Outcome == AiDestinationOutcomes.Dismissed);
	}

	/// <summary>Display names for a piece of advice (a destination or capability deleted since reads as null / is left out).</summary>
	public class AiDestinationAdviceDetails
	{
		public string CurrentPoiName { get; set; }

		public string RecommendedPoiName { get; set; }

		/// <summary>The recommended destination's current availability (<see cref="PoiAvailabilityStatuses"/>), null when there is none.</summary>
		public int? RecommendedPoiStatus { get; set; }

		public List<KeyValuePair<string, string>> RequiredCapabilities { get; set; } = new List<KeyValuePair<string, string>>();
	}

	/// <summary>What one worker 81 pass did.</summary>
	public class AiDestinationReviewSweepResult
	{
		public int Claimed { get; set; }

		public int Advised { get; set; }

		public int Retried { get; set; }

		public Dictionary<string, int> Outcomes { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

		public override string ToString()
		{
			var parts = new List<string>();
			foreach (var outcome in Outcomes)
				parts.Add(outcome.Key + "=" + outcome.Value);

			return $"Claimed {Claimed}, advised {Advised}, retried {Retried}" + (parts.Count == 0 ? "." : " (" + string.Join(", ", parts) + ").");
		}
	}
}
