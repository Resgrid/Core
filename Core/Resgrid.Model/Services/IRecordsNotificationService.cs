using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Delivers the RMS-owned notification EventTypes 31-33 (RMS plan section 4.7). Return-for-correction is
	/// author-targeted: it goes straight to the record's author and never consults department notification
	/// settings, which is why the types stay [NotMapped]. Invoked by the notification worker
	/// (NotificationBroadcastLogic) from the NotificationItem the Records service enqueues after commit.
	/// </summary>
	public interface IRecordsNotificationService
	{
		/// <summary>Notifies the author that their record was returned; false when nothing was sent (record missing, not Returned, no author).</summary>
		Task<bool> NotifyReturnedForCorrectionAsync(int departmentId, string recordId, CancellationToken cancellationToken = default);

		/// <summary>EventTypes 33: the destination rejected the author's incident report; codes and field paths only, plus a link.</summary>
		Task<bool> NotifySubmissionRejectedAsync(int departmentId, string reportId, CancellationToken cancellationToken = default);

		/// <summary>
		/// EventTypes 32 (RMS-3, worker 42): an obligation on a Record has gone overdue. Targeted at whoever the
		/// obligation rests with — the reviewer for a review, the author for a correction or a resubmission — and
		/// carries the reference, the obligation and how late it is. Never record content.
		/// </summary>
		Task<bool> NotifyObligationOverdueAsync(int departmentId, string recordId, RmsRecordObligation obligation, CancellationToken cancellationToken = default);
	}
}
