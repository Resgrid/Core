using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Security notices to the account holder (passkey plan section 6.4) through a durable user-level outbox, behind
	/// <c>TwoFactorConfig.SecurityNoticesEnabled</c> (off). A notice is
	/// queued after the change it reports has committed, delivered at once where possible, and retried by worker 13 until it
	/// is sent or fails for good. Queueing and delivery never throw into the caller: a lost notice is logged and audited,
	/// and never undoes the change.
	/// </summary>
	public interface ISecurityNoticeService
	{
		Task QueueAsync(SecurityNoticeRequest request, CancellationToken cancellationToken = default);

		/// <summary>As <see cref="QueueAsync"/>, unless the user already got this kind of notice within <paramref name="within"/>.</summary>
		Task QueueOnceAsync(SecurityNoticeRequest request, TimeSpan within, CancellationToken cancellationToken = default);

		/// <summary>Sends due notices (retries), up to <paramref name="batchSize"/>; returns how many were sent.</summary>
		Task<int> DeliverDueAsync(int batchSize, CancellationToken cancellationToken = default);
	}
}
