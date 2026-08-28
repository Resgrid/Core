using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Read-only ADP sizing scan (plan section 18.2). Counts department-owned rows per cataloged
	/// table and derives the P50–P90 migration estimate plus the projected number of overnight
	/// windows. Used by Enrollment Wizard step 5 (persisted with the acknowledgement record) and
	/// re-run by the migration worker on execution night to detect projection drift.
	/// </summary>
	public interface IAdpSizingService
	{
		Task<AdpSizingResult> RunSizingScanAsync(int departmentId, int windowMinutes, CancellationToken cancellationToken = default);
	}
}
