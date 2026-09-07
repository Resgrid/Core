using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Definition management (RMS plan sections 4.1 and 5.4, RMS-1B). Locked system definitions are readable here but
	/// never editable; department definitions follow Draft -> Published -> Retired with immutable published versions.
	/// Management needs ManageRecordDefinitions; publish and retire need PublishRecordDefinitions.
	/// </summary>
	public interface IRecordDefinitionsService
	{
		/// <summary>Every definition the department can see: locked system entries plus its own, with version state.</summary>
		Task<List<RecordDefinitionSummary>> ListAsync(int departmentId, bool includeRetired = false);

		/// <summary>Published department definitions a user may start a Record on (retired and draft-only excluded).</summary>
		Task<List<RmsRecordDefinitionVersion>> GetPublishedAsync(int departmentId);

		Task<RecordDefinitionAggregate> GetAsync(int departmentId, string definitionKey);

		/// <summary>The exact pinned version a Record renders against; null when the department has no such version.</summary>
		Task<RmsRecordDefinitionVersion> GetVersionAsync(int departmentId, string definitionKey, int version);

		Task<RmsRecordDefinitionVersion> GetVersionByIdAsync(int departmentId, string versionId);

		/// <summary>The current published version (what a new Record pins), or null.</summary>
		Task<RmsRecordDefinitionVersion> GetCurrentPublishedAsync(int departmentId, string definitionKey);

		/// <summary>Creates a definition with version 1 as a draft, blank, cloned from a product template/pack, or cloned from another department definition.</summary>
		Task<RecordDefinitionAggregate> CreateAsync(int departmentId, string userId, RecordDefinitionCreateInput input, CancellationToken cancellationToken = default);

		/// <summary>Opens a new draft version from the current published version (editing a published definition never mutates it).</summary>
		Task<RmsRecordDefinitionVersion> OpenDraftAsync(int departmentId, string userId, string definitionKey, CancellationToken cancellationToken = default);

		/// <summary>ETag-guarded draft save of schema, lifecycle, numbering and policies.</summary>
		Task<RmsRecordDefinitionVersion> SaveDraftAsync(int departmentId, string userId, string definitionKey, int version, long expectedRowVersion, RecordDefinitionDraftInput input, CancellationToken cancellationToken = default);

		/// <summary>Structural, rule (cycle), classification, capability and policy validation of a draft document.</summary>
		Task<RecordDefinitionValidation> ValidateAsync(int departmentId, RecordDefinitionDraftInput input);

		Task<RecordDefinitionImpactPreview> ImpactPreviewAsync(int departmentId, string definitionKey, int version);

		/// <summary>Freezes the draft: checksum, capability floor, materialized fields, current version pointer, trigger 113.</summary>
		Task<RmsRecordDefinitionVersion> PublishAsync(int departmentId, string userId, string definitionKey, int version, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Stops new Records on the definition; historical Records stay usable. Trigger 114.</summary>
		Task<RmsRecordDefinition> RetireAsync(int departmentId, string userId, string definitionKey, long expectedRowVersion, string reason, CancellationToken cancellationToken = default);

		/// <summary>Deletes an unused draft version (never a published one).</summary>
		Task<bool> DeleteDraftAsync(int departmentId, string userId, string definitionKey, int version, CancellationToken cancellationToken = default);

		Task<List<RmsRecordDefinitionVersion>> HistoryAsync(int departmentId, string definitionKey);

		Task<RecordDefinitionDiff> DiffAsync(int departmentId, string definitionKey, int fromVersion, int toVersion);

		/// <summary>Migrates compatible draft Records to a newer version through an explicit mapping; finalized Records never move.</summary>
		Task<RecordDefinitionMigrationResult> MigrateDraftsAsync(int departmentId, string userId, string definitionKey, int fromVersion, int toVersion, List<RecordDefinitionFieldMapping> mapping, bool preview, CancellationToken cancellationToken = default);
	}
}
