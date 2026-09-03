using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The single seam for Record value reads and writes (RMS plan section 5.9.1, "Single service seam for all
	/// value reads/writes"). In RMS-1 the typed Logs-parity fields live on RmsOperationalRecordDetail and this is
	/// the only caller of the details repository; RMS-1B's RmsRecordValues rows join it here, and Protected Data
	/// enrollment builds its protected persistence clone here, before the repository write, without touching
	/// RecordsService. Everything the seam does today is inert: IsProtected stays false, the envelope stays null,
	/// the catalog version reads 0, and a stray envelope without the flag is refused rather than stored.
	/// </summary>
	public interface IRmsRecordValueService
	{
		Task<RmsOperationalRecordDetail> GetDraftAsync(int departmentId, string recordId);

		Task<RmsOperationalRecordDetail> GetByRevisionAsync(int departmentId, string recordId, string revisionId);

		Task<IEnumerable<RmsOperationalRecordDetail>> GetDraftsForRecordsAsync(int departmentId, IEnumerable<string> recordIds);

		Task<RmsOperationalRecordDetail> InsertAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default);

		Task<RmsOperationalRecordDetail> UpdateAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default);

		Task<RmsOperationalRecordDetail> SaveOrUpdateAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default);
	}
}
