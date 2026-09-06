using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The typed value model behind department definitions (RMS plan section 5.3): parsing every posted value against
	/// the pinned field type, the bounded rule language, draft/revision storage in RmsRecordValues, and the safe
	/// projections (search text, Workflow block, print rows, export cells). Locked system definitions never come here.
	/// </summary>
	public interface IRecordTypedValuesService
	{
		/// <summary>Parses and validates inputs against the version. Draft saves tolerate incompleteness; finalizing applies requiredness and rules.</summary>
		Task<RecordValueValidation> ValidateAsync(int departmentId, RmsRecordDefinitionVersion version, List<RecordValueInput> inputs, bool finalizing);

		/// <summary>Evaluates the version's Show/Require rules over a value set.</summary>
		RecordRuleEvaluation EvaluateRules(RecordDefinitionSchema schema, RecordValueSet values);

		/// <summary>Replaces the working-draft rows (groups and values) for a Record inside the caller's transaction.</summary>
		Task<RecordValueSet> SaveDraftValuesAsync(int departmentId, string userId, string recordId, RmsRecordDefinitionVersion version, List<RecordValueInput> inputs, CancellationToken cancellationToken = default);

		/// <summary>Hydrates the draft (revisionId null) or an immutable revision, rendered against the pinned version, withholding restricted cells for the caller.</summary>
		Task<RecordValueSet> HydrateAsync(int departmentId, string recordId, string revisionId, RmsRecordDefinitionVersion version, bool canViewRestricted);

		/// <summary>Copies the working-draft rows into revision-bound rows (finalize/amend) inside the caller's transaction.</summary>
		Task CopyDraftToRevisionAsync(int departmentId, string recordId, string revisionId, CancellationToken cancellationToken = default);

		/// <summary>Restores the working draft from a revision (abandon amendment) inside the caller's transaction.</summary>
		Task RestoreDraftFromRevisionAsync(int departmentId, string userId, string recordId, string revisionId, RmsRecordDefinitionVersion version, CancellationToken cancellationToken = default);

		Task<int> DeleteDraftAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);

		/// <summary>Searchable, non-protected, non-restricted text for the search projection (RMS plan section 5.10).</summary>
		string ToSearchText(RecordDefinitionSchema schema, RecordValueSet values);

		/// <summary>The record.fields.* block: WorkflowExposed fields only, restricted/protected never.</summary>
		Dictionary<string, object> ToWorkflowBlock(RecordDefinitionSchema schema, RecordValueSet values);

		/// <summary>Snapshot form pinned to the version's labels: section label -> (field label -> display) or a row array for repeating sections.</summary>
		Dictionary<string, object> ToSnapshot(RecordDefinitionSchema schema, RecordValueSet values);

		/// <summary>Display summary for lists (first searchable short text values), bounded length.</summary>
		string ToDisplaySummary(RecordDefinitionSchema schema, RecordValueSet values);
	}
}
