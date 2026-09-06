using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Bulk operations over an authorized, paged Records selection (RMS plan section 4.7): assign-for-review, and the
	/// compiled print / bundle packet that reuses the scheduled-PDF delivery path. Bulk void and bulk delete are not
	/// offered; immutability is not negotiable for convenience.
	/// </summary>
	public interface IRecordsBulkPacketService
	{
		/// <summary>Compiles the selection into one stored export run (30-day retention, sealed under ADP) and optionally emails it.</summary>
		Task<RecordsBulkResult> BuildPacketAsync(int departmentId, string userId, RecordsBulkPacketRequest request, CancellationToken cancellationToken = default);

		/// <summary>Assigns a reviewer to every selected Record that is awaiting review; the rest are reported as skipped.</summary>
		Task<RecordsBulkResult> AssignForReviewAsync(int departmentId, string userId, RecordsBulkAssignRequest request, CancellationToken cancellationToken = default);

		/// <summary>A stored packet with its bytes unsealed for download; null when missing, expired or not a bulk packet. Needs ExportRecords.</summary>
		Task<RmsExportRun> GetPacketAsync(int departmentId, string userId, string runId, CancellationToken cancellationToken = default);
	}
}
