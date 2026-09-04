using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The "who owes me a report" axis (RMS plan section 4.7, Accountability): open Records, overdue reviews,
	/// returned-not-corrected Records and time-to-finalize pivoted by person, station/group or unit, with a bounded
	/// reminder. Rows are scoped by the same visibility rule as the queue, so a group-scoped viewer sees only
	/// what the queue would show.
	/// </summary>
	public interface IRecordsAccountabilityService
	{
		/// <summary>At most this many reminders leave in one action, and a Record is reminded at most once per day.</summary>
		int MaxRemindersPerAction { get; }

		Task<RecordsAccountabilityReport> BuildAsync(int departmentId, string viewerUserId, RecordsAccountabilityPivot pivot, int windowDays);

		Task<RecordsReminderResult> SendReminderAsync(int departmentId, string senderUserId, string recordId, CancellationToken cancellationToken = default);

		Task<List<RecordsReminderResult>> SendRemindersAsync(int departmentId, string senderUserId, IEnumerable<string> recordIds, CancellationToken cancellationToken = default);
	}
}
