using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Per-app Field Records rollout telemetry and its dashboard (RMS plan RMS-1D). Clients report bounded, coded
	/// outcomes; the server records them against the authenticated department and member and answers aggregates.
	/// Nothing here reads or returns record content — a rollout number is a count, never a disclosure.
	/// </summary>
	public interface IRecordsFieldRolloutService
	{
		/// <summary>Records a batch from one app; returns how many events were kept after validation and truncation.</summary>
		Task<int> RecordBatchAsync(int departmentId, string userId, RecordFieldRolloutBatch batch, CancellationToken cancellationToken = default);

		/// <summary>Records one server-observed outcome, such as a catalog refusal the client could not report itself.</summary>
		Task RecordAsync(int departmentId, string userId, RmsOriginClient origin, string appVersion, string clientCapability, string eventType, string outcome, CancellationToken cancellationToken = default);

		/// <summary>The dashboard over the last <paramref name="windowDays"/> days. Needs department administration.</summary>
		Task<RecordsFieldRollout> GetAsync(int departmentId, string userId, int windowDays = 30, CancellationToken cancellationToken = default);
	}
}
