using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Configure step (wizard plan sections 6.3 step 9 and 7.5): the configuration diff grouped by outcome, with each
	/// value's origin (preset, imported data or the administrator), the administrator's accept, edit and reject decisions,
	/// and the commit-time write and rollback of the accepted values. Suggestions are computed on every read from the
	/// operating profile and the workspace's data sets; only decisions are stored.
	/// </summary>
	public interface IImportConfigurationService
	{
		/// <summary>The Configure step for a department administrator (checked here).</summary>
		Task<ImportConfigurationView> GetAsync(int departmentId, string userId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The same view for Resgrid staff (plan section 17): the caller has checked the staff role and grant. Permissions can't
		/// be previewed for staff, so <see cref="ImportConfigurationView.PermissionPreviewAvailable"/> is false. Suggestions
		/// worked out from staged values (the middle of the imported stations) are left out unless
		/// <paramref name="stagedValuesVisible"/> (Inspect or more, section 17.2).
		/// </summary>
		Task<ImportConfigurationView> GetForStaffAsync(int departmentId, string workspaceId, bool stagedValuesVisible, CancellationToken cancellationToken = default);

		/// <summary>
		/// Accepts a value for an item (the suggestion or the actor's own) or rejects the suggestion. Refused while a commit or
		/// rollback run of the workspace is open, so an approved digest never changes under a pending run. An administrator
		/// is checked here; Resgrid staff (<see cref="ImportActorKind.ResgridStaff"/>, checked by the staff service) decide as
		/// drafts on the department's behalf and never decide a permission.
		/// </summary>
		Task<ImportConfigurationChange> DecideAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, ImportConfigurationDecision decision,
			CancellationToken cancellationToken = default);

		/// <summary>Accepts every suggestion still waiting for a decision (optionally in one group); returns how many.</summary>
		Task<int> AcceptSuggestionsAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string group,
			CancellationToken cancellationToken = default);

		/// <summary>Forgets the decision on an item (it shows its suggestion again, or nothing). Staff may forget only their own.</summary>
		Task<bool> ResetAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string itemKey, CancellationToken cancellationToken = default);

		/// <summary>Validates (PNG or JPEG, at most 2 MB, readable) and stores a logo for the workspace; accepting it is a separate decision.</summary>
		Task<ImportArtifact> UploadLogoAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string fileName, string contentType,
			byte[] data, CancellationToken cancellationToken = default);

		/// <summary>
		/// Accepted values that differ from the department's current configuration (what the next approval would write), and how
		/// many of them cannot be written as they stand (a document next number without a pattern, a missing logo...).
		/// </summary>
		Task<ImportConfigurationPending> GetPendingAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Commit run: writes the run's approved values in catalog order, journaling each before-value first.</summary>
		Task<ImportConfigurationApplyResult> ApplyAsync(ImportRun run, string actingUserId, Func<ImportChangeJournalEntry, CancellationToken, Task> journal,
			CancellationToken cancellationToken = default);

		/// <summary>Rollback run: restores one configuration journal entry while nobody has changed the setting since.</summary>
		Task<ImportRollbackEntryResult> RollbackAsync(ImportRun rollbackRun, string actingUserId, ImportChangeJournalEntry entry, CancellationToken cancellationToken = default);

		/// <summary>Reconciliation (L10): the run's written values still read back as written.</summary>
		Task<(long Expected, long Actual)> VerifyAsync(ImportRun run, CancellationToken cancellationToken = default);
	}

	public sealed class ImportConfigurationDecision
	{
		public string ItemKey { get; set; }
		/// <summary>True accepts <see cref="Value"/> (or the suggestion when <see cref="UseSuggestion"/>); false rejects the suggestion.</summary>
		public bool Accept { get; set; } = true;
		public bool UseSuggestion { get; set; }
		public string Value { get; set; }
	}

	public sealed class ImportConfigurationPending
	{
		public List<ImportConfigurationChange> Changes { get; set; } = new List<ImportConfigurationChange>();
		/// <summary>Item key -> value-free issue code for accepted values that cannot be written as they stand.</summary>
		public Dictionary<string, string> Issues { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		/// <summary>Every decision of the workspace (accepted, rejected or applied): the Configure step was worked through.</summary>
		public int Decided { get; set; }
	}

	public sealed class ImportConfigurationApplyResult
	{
		public int Written { get; set; }
		public int Unchanged { get; set; }
		public int Failed { get; set; }
		/// <summary>The groups written (drives the run's cache, security and app effects).</summary>
		public HashSet<string> Groups { get; set; } = new HashSet<string>(StringComparer.Ordinal);
	}

	public sealed class ImportConfigurationView
	{
		public ImportWorkspace Workspace { get; set; }
		/// <summary>The operating profile the suggestions come from (step 2); null when the department has not saved one.</summary>
		public DepartmentOperatingProfile Profile { get; set; }
		/// <summary>False while a commit or rollback run of the workspace is open.</summary>
		public bool CanEdit { get; set; }
		/// <summary>Admin Assist can preview permission impact for this administrator (required to accept a permission change).</summary>
		public bool PermissionPreviewAvailable { get; set; }
		public List<ImportConfigurationGroupView> Groups { get; set; } = new List<ImportConfigurationGroupView>();
		/// <summary>Accepted values that differ from what the department has now.</summary>
		public int Pending { get; set; }
		/// <summary>Suggestions still waiting for a decision.</summary>
		public int Suggestions { get; set; }
		/// <summary>The department's roles, for permissions granted to selected roles.</summary>
		public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();
		/// <summary>The workspace's uploaded logos, newest first.</summary>
		public List<ImportArtifact> Logos { get; set; } = new List<ImportArtifact>();
	}

	public sealed class ImportConfigurationGroupView
	{
		public string Key { get; set; }
		public List<ImportConfigurationRowView> Rows { get; set; } = new List<ImportConfigurationRowView>();
		public int Pending { get; set; }
		public int Suggestions { get; set; }
	}

	public sealed class ImportConfigurationRowView
	{
		public ImportConfigurationItem Item { get; set; }
		/// <summary>The department's current canonical value (empty = Resgrid's default or not set).</summary>
		public string Current { get; set; }
		/// <summary>The computed suggestion, or null.</summary>
		public string Suggested { get; set; }
		public ImportConfigurationOrigin? SuggestedOrigin { get; set; }
		public string SuggestedOriginKey { get; set; }
		/// <summary>The stored decision, or null.</summary>
		public ImportConfigurationChange Change { get; set; }
		/// <summary>Accepted and different from the current value: the next approval writes it.</summary>
		public bool Pending { get; set; }
		/// <summary>A suggestion that differs from the current value and has no decision yet.</summary>
		public bool AwaitingDecision { get; set; }
		/// <summary>Why the accepted value cannot be written as it stands (value-free code), or null.</summary>
		public string IssueCode { get; set; }
	}
}
