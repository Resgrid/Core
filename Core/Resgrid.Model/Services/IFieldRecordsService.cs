using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Field Records contract for the four operational apps (RMS plan RMS-1D): minimum-version preflight,
	/// the FieldRecordCatalogV1 manifest filtered by department, app, version, flags, role, verified context,
	/// Protected Data state and each definition's client surface; server-calculated prefill with provenance; and
	/// the bounded sync bundle. Every filter is applied server-side from the authenticated principal; a client
	/// cannot widen what it receives by changing a query value.
	/// </summary>
	public interface IFieldRecordsService
	{
		Task<FieldRecordPreflight> PreflightAsync(int departmentId, string userId, RmsOriginClient origin, string appVersion, string clientCapability);

		/// <summary>Verifies that the caller may act in the claimed Call / Unit / group / command context for this app.</summary>
		Task<FieldRecordContextVerification> VerifyContextAsync(int departmentId, string userId, RmsOriginClient origin, FieldRecordContext context);

		Task<FieldRecordCatalog> GetCatalogAsync(int departmentId, string userId, FieldRecordCatalogRequest request);

		/// <summary>Prefill for one catalog entry; refused when the entry is not in the caller's catalog for the same request.</summary>
		Task<FieldRecordPrefill> PrefillAsync(int departmentId, string userId, FieldRecordCatalogRequest request, string definitionKey, int version);

		Task<FieldRecordSyncBundle> SyncAsync(int departmentId, string userId, FieldRecordSyncRequest request, CancellationToken cancellationToken = default);
	}
}
