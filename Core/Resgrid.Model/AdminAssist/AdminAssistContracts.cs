using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public interface IConfigurationSnapshotProvider
	{
		Task<ConfigurationSnapshot> ReadAsync(AdminAssistActor actor, CancellationToken cancellationToken = default);
	}
	public sealed record AdminAssistSearchHit(string Id, string TitleKey, string Excerpt, string SourcePath, string Anchor, string PackVersion, string Locale);
	public interface IAdminAssistReferenceSearch
	{
		IReadOnlyList<AdminAssistSearchHit> Search(string query, string locale, int take = 20);
	}
	public interface IAdminAssistEvidenceSource
	{
		string SourceId { get; }
		IReadOnlyList<string> EvidenceIds { get; }
		Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime asOfUtc, CancellationToken cancellationToken);
	}
	public interface IAdminAssistAccessService
	{
		Task<bool> CanAccessAsync(AdminAssistActor actor, bool setup, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<CapabilityAccess>> GetCapabilitiesAsync(AdminAssistActor actor, CancellationToken cancellationToken = default);
		Task<CapabilityAccess> GetCapabilityAsync(AdminAssistActor actor, string capabilityId, CancellationToken cancellationToken = default);
	}
	public interface IConfigurationRule
	{
		ConfigurationRuleDefinition Definition { get; }
		ConfigurationFinding Evaluate(ConfigurationSnapshot snapshot, DateTime nowUtc, TimeSpan maximumAge);
	}
	public interface IAdminAssistService
	{
		Task<AdminAssistOverview> GetOverviewAsync(AdminAssistActor actor, bool setup, CancellationToken cancellationToken = default);
		Task<SetupWorkspace> UpdateSetupAsync(AdminAssistActor actor, SetupProgressCommand command, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<AdminAssistHistoryItem>> GetHistoryAsync(AdminAssistActor actor, int skip, int take, CancellationToken cancellationToken = default);
	}
	public sealed record SetupWorkspace(int DepartmentId, long Revision, SetupMode Mode,
		IReadOnlyDictionary<string, SetupAreaChoice> Areas, IReadOnlyList<string> LearnedCapabilityIds,
		IReadOnlyList<string> InterestedCapabilityIds, string CatalogVersion, DateTime? ReviewedOnUtc, bool SetupPromptDismissed = false,
		IReadOnlyDictionary<string, SetupAreaReason> AreaReasons = null, SetupReviewEvidence ReviewEvidence = null, DateTime? RevisitOnUtc = null, long ScopeRevision = 0);
	public sealed record SetupProgressCommand(long ExpectedRevision, string Operation, string TargetId = null,
		string Choice = null, string CatalogVersion = null, string ReasonCode = null, string EvidenceRevision = null, DateTime? RevisitOnUtc = null)
	{
		[System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
		public SetupReviewEvidence ReviewEvidence { get; init; }
	}
	public enum SetupAreaReason { OutsideMission, OtherSystem, PartnerManaged, NoCurrentNeed }
	public sealed record SetupReviewEvidence(string CatalogVersion, string SnapshotRevision, DateTime AsOfUtc, int Required, int Verified, int Failed, int Unknown, long ScopeRevision = 0);
	public sealed record AdminAssistOverview(string CatalogVersion, SetupWorkspace Workspace,
		ConfigurationReport Report, IReadOnlyList<CapabilityAccess> Access, IReadOnlyList<CapabilitySetupAssessment> CapabilitySetup = null, SetupPlan SetupPlan = null);
	public enum CapabilitySetupState { NotAssessed, NotConfigured, ConfigurationPresent, ChecksPassed, NeedsAttention }
	public sealed record CapabilitySetupAssessment(string CapabilityId, CapabilitySetupState State, string OpportunityKey,
		string GuidanceKey, IReadOnlyList<string> RuleIds, string SnapshotRevision, DateTime AsOfUtc);
	public sealed record AdminAssistHistoryItem(string Id, string ActorId, DateTime OccurredOnUtc,
		string Source, string Action, string SubjectId, string BeforeCode, string AfterCode, long Revision);
	public interface IAdminAssistRepository
	{
		Task<IReadOnlyList<AdminAssistFindingRow>> GetFindingsAsync(int departmentId, CancellationToken cancellationToken);
		Task SaveFindingAsync(AdminAssistFindingRow row, long expectedRevision, CancellationToken cancellationToken);
		Task SaveDailySummaryAsync(int departmentId, ConfigurationReport report, string catalogVersion, CancellationToken cancellationToken);
		Task LockConfigurationAsync(int departmentId, CancellationToken cancellationToken);
		Task<bool> ValidateOperatingProfileReferencesAsync(int departmentId, DepartmentOperatingProfile profile, DateTime asOfUtc, CancellationToken cancellationToken);
		Task<long> AppendConfigurationChangeAsync(int departmentId, string actorId, string binding, string before, string after, string correlationId, CancellationToken cancellationToken);
		Task<long> GetConfigurationRevisionAsync(int departmentId, CancellationToken cancellationToken);
		Task<SetupWorkspace> GetWorkspaceAsync(int departmentId, string userId, string catalogVersion, CancellationToken cancellationToken);
		Task<SetupWorkspace> UpdateWorkspaceAsync(AdminAssistActor actor, SetupProgressCommand command, CancellationToken cancellationToken);
		Task<IReadOnlyList<AdminAssistHistoryItem>> GetHistoryAsync(int departmentId, string actorId, int skip, int take, CancellationToken cancellationToken);
	}
	public sealed class AdminAssistConcurrencyException : Exception
	{
		public AdminAssistConcurrencyException() : base("The setup workspace changed. Reload before saving.") { }
	}
}
