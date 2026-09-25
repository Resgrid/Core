using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Queue;

namespace Resgrid.Model.AiDispatch
{
	/// <summary>
	/// Terminal states of one Enrich-mode attempt (enhanced-ai-addon-plan.md §4). Every state leaves the deterministic call in place;
	/// only Applied changes it.
	/// </summary>
	public static class AiDispatchOutcomes
	{
		public const string InProgress = "InProgress";
		public const string Applied = "Applied";
		public const string NoChange = "NoChange";
		public const string LowConfidence = "LowConfidence";
		public const string InvalidOutput = "InvalidOutput";
		public const string Unavailable = "Unavailable";
		public const string Busy = "Busy";
		public const string BudgetExhausted = "BudgetExhausted";
		public const string ProtectionUnsupported = "ProtectionUnsupported";
		public const string CallNotActive = "CallNotActive";
		/// <summary>The department's sender allowlist did not include the message's sender; the call was still created.</summary>
		public const string SenderNotAllowed = "SenderNotAllowed";
		/// <summary>The department's monthly AI dispatch token cap was reached.</summary>
		public const string DispatchCapReached = "DispatchCapReached";
	}

	/// <summary>
	/// Metadata-only audit of one enrichment (M0240, <c>AiDispatchAudits</c>). It never stores the message, the prompt, the model's
	/// reply or any extracted value: those live only on the call, under the call's own protection. The unique
	/// (DepartmentId, CallId) index makes the row the idempotency claim for at-least-once delivery.
	/// </summary>
	public sealed class AiDispatchAuditRow : IEntity
	{
		public string AiDispatchAuditId { get; set; }
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string Mode { get; set; }
		public string Outcome { get; set; }
		public decimal? Confidence { get; set; }
		/// <summary>Comma-separated call field names written, e.g. "Type,Address"; never their values.</summary>
		public string AppliedFields { get; set; }
		/// <summary>Model-returned identifiers or values that failed the allowlist or the verbatim-text rule.</summary>
		public int RejectedCount { get; set; }
		public int? RelatedCallId { get; set; }
		public string PromptVersion { get; set; }
		public string ModelName { get; set; }
		public string ModelRevision { get; set; }
		public string RuntimeDigest { get; set; }
		public int InputTokens { get; set; }
		public int OutputTokens { get; set; }
		public int LatencyMs { get; set; }
		public DateTime CreatedOnUtc { get; set; }
		public DateTime? CompletedOnUtc { get; set; }

		[NotMapped, JsonIgnore] public object IdValue { get => AiDispatchAuditId; set => AiDispatchAuditId = (string)value; }
		[NotMapped] public string TableName => "AiDispatchAudits";
		[NotMapped] public string IdName => "AiDispatchAuditId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An allowlisted department choice offered to the model (call type or priority).</summary>
	public sealed record AiDispatchChoice(int Id, string Name);

	/// <summary>A recent active call offered to the model as a possible duplicate; only its id may come back.</summary>
	public sealed record AiDispatchCandidateCall(int CallId, string Number, string Name, string Address, int MinutesAgo);

	/// <summary>Everything the model is shown for one call. <see cref="Message"/> is untrusted inbound text.</summary>
	public sealed record AiDispatchContext(string Message, IReadOnlyList<AiDispatchChoice> CallTypes, IReadOnlyList<AiDispatchChoice> Priorities,
		IReadOnlyList<AiDispatchCandidateCall> Candidates);

	/// <summary>The model's structured reply before validation. Every field is optional and untrusted.</summary>
	public sealed class AiDispatchProposal
	{
		public bool? IsDispatch { get; set; }
		public double? Confidence { get; set; }
		public string Title { get; set; }
		public int? CallTypeId { get; set; }
		public int? PriorityId { get; set; }
		public string Address { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string IncidentNumber { get; set; }
		public int? PossibleDuplicateOfCallId { get; set; }
		public string Summary { get; set; }
	}

	/// <summary>A proposal after validation: identifiers are allowlisted and extracted strings appear verbatim in the message.</summary>
	public sealed record AiDispatchEnrichment(bool IsDispatch, double Confidence, string Title, AiDispatchChoice CallType, AiDispatchChoice Priority,
		string Address, string ContactName, string ContactNumber, string IncidentNumber, AiDispatchCandidateCall RelatedCall, string Summary, int RejectedCount);

	public interface IAiDispatchEnrichmentService
	{
		/// <summary>Host switch, rollout flags, module switch and entitlement (paid add-on or operator-listed self-hosted department) all allow AI dispatch.</summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>Enriches one deterministic call created from an AI-format dispatch message. Returns the outcome; never throws for model or policy failures.</summary>
		Task<string> EnrichAsync(AiDispatchQueueItem item, CancellationToken cancellationToken);
	}

	public interface IAiDispatchAuditRepository
	{
		/// <summary>Inserts the claim row; false when this call already has one (another delivery owns or finished it).</summary>
		Task<bool> TryClaimAsync(AiDispatchAuditRow row, CancellationToken cancellationToken);
		Task CompleteAsync(AiDispatchAuditRow row, CancellationToken cancellationToken);
		/// <summary>Newest first, with the numbers of the calls each row refers to.</summary>
		Task<List<AiDispatchAuditListItem>> GetRecentAsync(int departmentId, int take, CancellationToken cancellationToken);
		/// <summary>Removes the department's rows created before <paramref name="cutoffUtc"/> (its audit retention setting).</summary>
		Task<int> PruneAsync(int departmentId, DateTime cutoffUtc, CancellationToken cancellationToken);
	}

	/// <summary>
	/// Background admission on the shared inference slots (M0237 <c>AiAdmission</c> lock, <c>AiUsageLedger</c>). A background turn
	/// starts only when no turn at all is live, so interactive work always goes first, and it draws on the department's monthly
	/// token budget. Completion uses <see cref="IAiUsageMeter.CompleteAsync"/>.
	/// </summary>
	public interface IAiBackgroundAdmission
	{
		/// <param name="featureMonthlyLimit">Optional per-feature cap inside the department budget (e.g. the AI dispatch token cap).</param>
		Task<AiUsageReservation> ReserveBackgroundAsync(int departmentId, string feature, string tier, DateTime nowUtc, int tokens, int monthlyLimit,
			CancellationToken cancellationToken, int? featureMonthlyLimit = null);

		/// <summary>Tokens the feature has used or holds in reservation for the department in the calendar month of <paramref name="nowUtc"/>.</summary>
		Task<long> GetFeatureUsageAsync(int departmentId, string feature, DateTime nowUtc, CancellationToken cancellationToken);
	}
}
