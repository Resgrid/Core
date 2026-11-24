using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// The CAD email update ledger (CallEmailUpdates, M0330) and the call lookups and lock the CAD update engine needs
	/// (cad-email-call-updates-plan.md §4.3, §4.7).
	/// </summary>
	public interface ICallEmailUpdatesRepository : IRepository<CallEmailUpdate>
	{
		/// <summary>The ledger rows of one call, oldest first.</summary>
		Task<IEnumerable<CallEmailUpdate>> GetByCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The ledger rows an email (its provider message id) left under one CAD call ID: how a redelivery of an email whose update
		/// already went through is recognised.
		/// </summary>
		Task<IEnumerable<CallEmailUpdate>> GetByMessageAsync(int departmentId, string cadCallId, string messageId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves one row from <paramref name="fromOutcome"/> to <paramref name="toOutcome"/> (the update it announced went through).
		/// False when the row was not in that outcome.
		/// </summary>
		Task<bool> SetOutcomeAsync(string callEmailUpdateId, int fromOutcome, int toOutcome, CancellationToken cancellationToken = default);

		/// <summary>
		/// The department's open (Active) and Pending calls, not deleted, whose external identifier or incident number is
		/// <paramref name="cadCallId"/> (trimmed, case ignored) or that a ledger row ties to it.
		/// </summary>
		Task<IEnumerable<Call>> GetOpenCallsForCadCallIdAsync(int departmentId, string cadCallId, CancellationToken cancellationToken = default);

		/// <summary>As <see cref="GetOpenCallsForCadCallIdAsync"/>, for calls closed (any closed state) at or after <paramref name="closedSinceUtc"/>.</summary>
		Task<IEnumerable<Call>> GetClosedCallsForCadCallIdAsync(int departmentId, string cadCallId, DateTime closedSinceUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Serialises the handling of one CAD call ID across hosts: a session lock on its own connection (sp_getapplock /
		/// pg_try_advisory_lock), held until the returned handle is disposed. Null when another holder kept it past
		/// <paramref name="timeout"/>.
		/// </summary>
		Task<IAsyncDisposable> AcquireCadCallLockAsync(int departmentId, string cadCallId, TimeSpan timeout, CancellationToken cancellationToken = default);
	}
}
