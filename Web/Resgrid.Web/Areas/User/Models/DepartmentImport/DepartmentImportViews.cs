using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.DepartmentImport
{
	/// <summary>What every workspace page shares: the workspace, its summary strip and the active support grant.</summary>
	public abstract class ImportPageView
	{
		public ImportWorkspace Workspace { get; set; }
		public ImportWorkspaceSummary Summary { get; set; }
		public ImportSupportGrant ActiveGrant { get; set; }
		public bool CanManage { get; set; }
		public IImportCatalog Catalog { get; set; }
		/// <summary>The workspace's sources: the products it is moving from (glossary drawer) and its continuation, if any.</summary>
		public List<ImportSource> Sources { get; set; } = new List<ImportSource>();
	}

	public sealed class ImportIndexView
	{
		public List<ImportWorkspace> Workspaces { get; set; } = new List<ImportWorkspace>();
		public bool CanManage { get; set; }
		public ImportStoreHealth StoreHealth { get; set; }
		public bool RunInProgress { get; set; }
	}

	public sealed class ImportOverviewView : ImportPageView
	{
		public List<ImportIssueGroup> TopIssues { get; set; } = new List<ImportIssueGroup>();
		public List<ImportSupportRequest> Requests { get; set; } = new List<ImportSupportRequest>();
		public List<ImportSuggestion> Suggestions { get; set; } = new List<ImportSuggestion>();
		public List<ImportEvent> Events { get; set; } = new List<ImportEvent>();
		/// <summary>Accepted Configure values the next approval writes, and whether any Configure decision was made.</summary>
		public int ConfigurationPending { get; set; }
		public bool ConfigurationDecided { get; set; }
	}

	public sealed class ImportFilesView : ImportPageView
	{
		public List<ImportArtifact> Artifacts { get; set; } = new List<ImportArtifact>();
		public List<ImportDataset> Datasets { get; set; } = new List<ImportDataset>();
		public IReadOnlyList<string> Exportable { get; set; } = new List<string>();
		public int ChunkSizeBytes { get; set; }
		/// <summary>The earlier import this workspace continues, or null.</summary>
		public ImportSource Continuation { get; set; }
		public List<ImportContinuationCandidate> ContinuationCandidates { get; set; } = new List<ImportContinuationCandidate>();
		/// <summary>True once this workspace wrote rows: its Source IDs then stay where they are.</summary>
		public bool ContinuationLocked { get; set; }
	}

	public sealed class ImportDatasetView : ImportPageView
	{
		public ImportMappingView Mapping { get; set; }
		public List<ImportIssueGroup> Issues { get; set; } = new List<ImportIssueGroup>();
		public List<ImportColumnMap> SavedMaps { get; set; } = new List<ImportColumnMap>();
	}

	public sealed class ImportValuesView : ImportPageView
	{
		public ImportDataset Dataset { get; set; }
		public ImportFieldContract Field { get; set; }
		public ImportEntityContract Contract { get; set; }
		public List<ImportValueCandidate> Candidates { get; set; } = new List<ImportValueCandidate>();
	}

	public sealed class ImportIssuesView : ImportPageView
	{
		public List<ImportIssueGroup> Groups { get; set; } = new List<ImportIssueGroup>();
		public List<ImportDataset> Datasets { get; set; } = new List<ImportDataset>();
		public string DatasetId { get; set; }
		public bool OpenOnly { get; set; } = true;
	}

	/// <summary>The Configure step (wizard plan section 6.3 step 9).</summary>
	public sealed class ImportConfigureView : ImportPageView
	{
		public ImportConfigurationView Configuration { get; set; }
		/// <summary>Admin Assist impact previews of the module and permission rows that would change, by item key.</summary>
		public Dictionary<string, Resgrid.Model.AdminAssist.ConfigurationImpactReport> Impacts { get; set; } =
			new Dictionary<string, Resgrid.Model.AdminAssist.ConfigurationImpactReport>(StringComparer.Ordinal);
		/// <summary>The group to open ("department", "terminology"...), from the last decision.</summary>
		public string OpenGroup { get; set; }
	}

	/// <summary>A Configure step decision as posted: the value's parts by kind, assembled by the controller.</summary>
	public sealed class ImportConfigureInput
	{
		public string ItemKey { get; set; }
		/// <summary>"accept" (the posted value), "suggestion" (accept the suggestion) or "reject".</summary>
		public string Decision { get; set; }
		public string Value { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public string Address1 { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public int? Month { get; set; }
		public int? Day { get; set; }
		public int? Action { get; set; }
		public bool LockToGroup { get; set; }
		public List<int> Roles { get; set; } = new List<int>();
	}

	public sealed class ImportReviewView : ImportPageView
	{
		public ImportPreview Preview { get; set; }
		public bool ManagedMigration { get; set; }
		/// <summary>Replace is offered: Resgrid support holds Prepare access or more (support-assisted, section 5.2).</summary>
		public bool ReplaceAvailable { get; set; }
	}

	public sealed class ImportHistoryView : ImportPageView
	{
		public List<ImportRun> Runs { get; set; } = new List<ImportRun>();
		public List<ImportDataset> Datasets { get; set; } = new List<ImportDataset>();
		public Dictionary<string, List<ImportReconciliation>> Reconciliations { get; set; } = new Dictionary<string, List<ImportReconciliation>>();
		public Dictionary<string, ImportRollbackPreview> Rollbacks { get; set; } = new Dictionary<string, ImportRollbackPreview>();
	}

	public sealed class ImportVerifyView : ImportPageView
	{
		public ImportActivationChecklist Checklist { get; set; }
		public List<Invite> Invitations { get; set; } = new List<Invite>();
		/// <summary>Activation steps and invitation sends Resgrid staff requested on the department's behalf (section 17.8).</summary>
		public List<ImportManagedActivationView> ManagedRuns { get; set; } = new List<ImportManagedActivationView>();
	}

	public sealed class ImportSupportView : ImportPageView
	{
		public List<ImportSupportRequest> Requests { get; set; } = new List<ImportSupportRequest>();
		public List<ImportMessage> Messages { get; set; } = new List<ImportMessage>();
		public List<ImportSupportGrant> Grants { get; set; } = new List<ImportSupportGrant>();
		public List<ImportSuggestion> Suggestions { get; set; } = new List<ImportSuggestion>();
		public bool ManagedMigration { get; set; }
		/// <summary>The grant dialog's starting length: the department's "Default support access (days)" setting.</summary>
		public int SupportGrantDefaultDays { get; set; } = DepartmentImportConfig.DefaultSupportGrantDays;
		public string RequestDatasetId { get; set; }
		public string RequestRunId { get; set; }
		public string RequestIssueCode { get; set; }
	}

	/// <summary>Import & Migration settings (DepartmentSettingTypes.DepartmentImportConfig); field names equal the Admin Assist locations.</summary>
	public sealed class ImportSettingsView
	{
		public bool CanManage { get; set; }
		public int ArtifactRetentionDaysAfterSuccess { get; set; }
		public int ArtifactRetentionDaysAfterFailure { get; set; }
		public int StagingRetentionDaysAfterActivation { get; set; }
		public bool RequireSecondApprover { get; set; }
		public int DefaultWriteMode { get; set; }
		public int SupportGrantDefaultDays { get; set; }
		public bool Saved { get; set; }
	}

	public sealed class ImportCreateInput
	{
		public int Mode { get; set; } = (int)ImportWorkspaceMode.NewDepartment;
		public string Name { get; set; }
		public string InternalReference { get; set; }
		public System.DateTime? TargetActivationDate { get; set; }
	}

	public sealed class ImportUploadStartInput
	{
		public string WorkspaceId { get; set; }
		public string FileName { get; set; }
		public long SizeBytes { get; set; }
		public string Sha256 { get; set; }
		public string ContentType { get; set; }
		public string SourceId { get; set; }
	}

	public sealed class ImportMappingInput
	{
		public string DatasetId { get; set; }
		public int ExpectedRevision { get; set; }
		public ImportColumnMapBody Body { get; set; }
	}

	public sealed class ImportValuesInput
	{
		public string DatasetId { get; set; }
		public string FieldKey { get; set; }
		public List<ImportValueDecision> Decisions { get; set; } = new List<ImportValueDecision>();
	}

	public sealed class ImportIssueActionInput
	{
		public string WorkspaceId { get; set; }
		/// <summary>accept, exclude, reopen, set.</summary>
		public string Action { get; set; }
		public List<string> IssueIds { get; set; } = new List<string>();
		public string Value { get; set; }
	}

	public sealed class ImportApproveInput
	{
		public string WorkspaceId { get; set; }
		public List<string> DatasetIds { get; set; } = new List<string>();
		public int WriteMode { get; set; } = (int)ImportWriteMode.CreateOnly;
		public List<string> Acknowledged { get; set; } = new List<string>();
		public bool SuppressExpiredCertificationNotices { get; set; } = true;
	}

	public sealed class ImportInviteInput
	{
		public string WorkspaceId { get; set; }
		public List<string> UserIds { get; set; } = new List<string>();
		public bool IncludeExistingAccounts { get; set; }
		public System.DateTime? SendAfter { get; set; }
	}
}
