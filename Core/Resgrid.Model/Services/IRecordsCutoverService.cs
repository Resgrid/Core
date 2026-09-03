using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Records (RMS) module state and the department cutover (RMS plan section 4.1). The feature flag
	/// selects Records versus Logs in the UI; the append-only RmsDepartmentCutover row is what engages
	/// the legacy Log/UnitLog write guard and what the rollback runbook keys off.
	/// </summary>
	public interface IRecordsCutoverService
	{
		Task<RecordsModuleState> GetModuleStateAsync(int departmentId, bool bypassCache = false);

		/// <summary>Records.System evaluates on for the department.</summary>
		Task<bool> IsRecordsEnabledAsync(int departmentId);

		/// <summary>True once the department has an active cutover row; every legacy Log/UnitLog mutation must then be denied.</summary>
		Task<bool> AreLegacyWritesBlockedAsync(int departmentId);

		/// <summary>Service-boundary guard: throws <see cref="RecordsLegacyWriteBlockedException"/> and audits the attempt when blocked.</summary>
		Task EnsureLegacyWriteAllowedAsync(int departmentId, string context, string userId = null);

		Task<RecordsActivationPreview> GetActivationPreviewAsync(int departmentId);

		/// <summary>
		/// Activates Records for the department in one transaction: cutover row, Permission-row migration
		/// (registry section 4.6), ViewGroupRecords row when the administrator chose LockToGroup, history
		/// event and access audit. Refuses when the flag is off, preflight blocks, or already activated.
		/// </summary>
		Task<RecordsActivationResult> ActivateAsync(int departmentId, string userId, string reason, bool viewGroupRecordsLockToGroup, CancellationToken cancellationToken = default);

		/// <summary>Which rollback outcome the decision frame permits right now.</summary>
		Task<RecordsRollbackOutcome> GetRollbackOutcomeAsync(int departmentId);

		/// <summary>Clean revert only (zero Records rows since activation); anything else returns a failure naming the outcome.</summary>
		Task<RecordsActivationResult> RevertAsync(int departmentId, string userId, string reason, CancellationToken cancellationToken = default);

		Task InvalidateCacheAsync(int departmentId);
	}
}
