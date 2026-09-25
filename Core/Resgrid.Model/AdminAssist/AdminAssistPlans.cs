using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	// Shared proposal contract for Admin Assist and the onboarding configuration diff. There is no apply command.
	public sealed record ConfigurationChangeSet(string Version, string Source, string SourceId, IReadOnlyList<ConfigurationChange> Changes);
	public sealed record ConfigurationChange(string Id, string CatalogId, bool? Boolean, decimal? Number,
		IReadOnlyList<string> Prerequisites);
	public sealed record PlanDispatchScenario(int CallId, DateTime SimulationTimeUtc);
	public sealed record PlanDraftRequest(string Goal, string TemplateId = null, ConfigurationChangeSet ChangeSet = null, PlanDispatchScenario DispatchScenario = null);
	public sealed record PlanCreateCommand(PlanDraftRequest Draft, string PreviewDigest);
	public sealed record PlanReference(string PlanId, long ExpectedRevision);
	public sealed record PlanCommand(string PlanId, long ExpectedRevision, string Operation, string PreviewDigest = null,
		string StepId = null, bool? Shared = null, DateTime? WindowUtc = null, string Fallback = null,
		bool PropagationChecked = false, bool BehaviorTested = false);
	public sealed record PlanAttestation(string StepId, string ActorId, DateTime CheckedOnUtc, string EvidenceDigest,
		bool PropagationChecked, bool BehaviorTested);
	public sealed record PlanReview(string ActorId, DateTime ReviewedOnUtc, string PreviewDigest, DateTime WindowUtc, string Fallback);
	public sealed record PlanContent(PlanDraftRequest Draft, IReadOnlyList<string> OperatingPacksAtCreation,
		IReadOnlyDictionary<string, string> States, IReadOnlyDictionary<string, string> InitialValues,
		IReadOnlyList<PlanAttestation> Attestations, PlanReview Review = null);
	public sealed record PlanVerification(string Saved, string Rule, string Propagated, string BehaviorTested,
		string EvidenceDigest, DateTime CheckedOnUtc, DateTime? RecheckAfterUtc);
	public sealed record PlanStepView(ConfigurationChange Change, string LabelKey, string State, string CurrentValue,
		string ProposedValue, string RationaleKey, string InstructionsKey, string Destination, string RollbackKey,
		ConfigurationImpactReport Impact, PlanVerification Verification);
	public sealed record PlanImpact(string Risk, int? UniqueAffectedPeople, IReadOnlyList<ConfigurationImpactRule> FinalRuleChanges,
		IReadOnlyList<string> LimitKeys, string CommunicationDraftKey)
	{
		public IReadOnlyList<ConfigurationImpactMetric> FinalMetrics { get; init; } = Array.Empty<ConfigurationImpactMetric>();
	}
	public sealed record PlanView(string Id, long Revision, bool Owned, bool Shared, string Status, DateTime CreatedOnUtc,
		DateTime UpdatedOnUtc, string Goal, ConfigurationChangeSet ChangeSet, IReadOnlyList<string> OperatingPacksAtCreation,
		IReadOnlyList<PlanStepView> Steps, PlanImpact Impact, string CatalogVersion, string SnapshotRevision,
		long ScopeRevision, DateTime AsOfUtc, string PreviewDigest, PlanReview Review);
	public sealed record PlanTemplate(string Id, string LabelKey, string DescriptionKey, ConfigurationChangeSet ChangeSet);
	public sealed record PlanListItem(string Id, long Revision, bool Shared, bool Owned, string Status, DateTime UpdatedOnUtc);
	public sealed record PlanPdf(string Filename, string ContentType, string Base64);
	// Narrow interface supplied to model tools: both operations read and return transient evidence only.
	public interface IAdminAssistPlanQueries
	{
		Task<PlanView> DraftAsync(AdminAssistActor actor, PlanDraftRequest request, CancellationToken ct);
		Task<PlanVerification> VerifyStepAsync(AdminAssistActor actor, string planId, string stepId, CancellationToken ct);
	}
	public interface IAdminAssistPlans
	{
		Task<IReadOnlyList<PlanTemplate>> TemplatesAsync(AdminAssistActor actor, CancellationToken ct);
		Task<PlanView> CreateAsync(AdminAssistActor actor, PlanCreateCommand command, CancellationToken ct);
		Task<PlanView> ReadAsync(AdminAssistActor actor, PlanReference reference, CancellationToken ct);
		Task<IReadOnlyList<PlanListItem>> ListAsync(AdminAssistActor actor, CancellationToken ct);
		Task<PlanView> CommandAsync(AdminAssistActor actor, PlanCommand command, CancellationToken ct);
		Task<PlanPdf> ExportAsync(AdminAssistActor actor, PlanCommand command, CancellationToken ct);
	}
	public interface IAdminAssistPlanProtection
	{
		Task RequireAsync(AdminAssistActor actor, CancellationToken ct);
		Task ProtectAsync(AdminAssistActor actor, AdminAssistPlanRow row, PlanContent content, CancellationToken ct);
		Task<PlanContent> ReadAsync(AdminAssistActor actor, AdminAssistPlanRow row, CancellationToken ct);
	}
	public interface IAdminAssistPlanStore
	{
		Task<AdminAssistPlanRow> ReadPlanAsync(AdminAssistActor actor, string id, CancellationToken ct);
		Task<IReadOnlyList<AdminAssistPlanRow>> ListPlansAsync(AdminAssistActor actor, DateTime closedAfter, CancellationToken ct);
		Task SavePlanAsync(AdminAssistActor actor, AdminAssistPlanRow row, long expectedRevision, string expectedConfigurationRevision, long expectedScopeRevision, CancellationToken ct);
	}
	[Table("AdminAssistPlans")]
	public sealed class AdminAssistPlanRow : IEntity
	{
		[Key, DatabaseGenerated(DatabaseGeneratedOption.None)] public string Id { get; set; }
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public DateTime CreatedOnUtc { get; set; }
		public DateTime UpdatedOnUtc { get; set; }
		public DateTime? ClosedOnUtc { get; set; }
		public long Revision { get; set; }
		public bool Shared { get; set; }
		public bool Deleted { get; set; }
		public string Status { get; set; }
		public string Content { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		[NotMapped] public string TableName => "AdminAssistPlans";
		[NotMapped] public string IdName => nameof(Id);
		[NotMapped] public object IdValue { get => Id; set => Id = value?.ToString(); }
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { nameof(IdValue), nameof(TableName), nameof(IdName), nameof(IdType), nameof(IgnoredProperties) };
	}
}
