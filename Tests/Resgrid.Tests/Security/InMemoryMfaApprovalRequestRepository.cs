using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>Same observable rules as MfaApprovalRequestRepository: one pending request per user, every transition guarded.</summary>
	internal sealed class InMemoryMfaApprovalRequestRepository : IMfaApprovalRequestRepository
	{
		private readonly object _gate = new();
		public readonly List<MfaApprovalRequest> Rows = new();

		public Task<bool> TryInsertPendingAsync(MfaApprovalRequest request, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				foreach (var stale in Rows.Where(r => r.UserId == request.UserId && r.RequestState == MfaApprovalRequestState.Pending && r.ExpiresOnUtc <= utcNow))
					Transition(stale, MfaApprovalRequestState.Expired);

				if (Rows.Any(r => r.UserId == request.UserId && r.RequestState == MfaApprovalRequestState.Pending))
					return Task.FromResult(false);

				var row = Copy(request);
				row.State = (int)MfaApprovalRequestState.Pending;
				row.Version = 1;
				row.Attempts = 0;
				Rows.Add(row);
				return Task.FromResult(true);
			}
		}

		public Task<MfaApprovalRequest> GetAsync(string approvalRequestId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.SingleOrDefault(r => r.MfaApprovalRequestId == approvalRequestId)));
		}

		public Task<MfaApprovalRequest> GetPendingForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Copy(Rows.Where(r => r.UserId == userId && r.RequestState == MfaApprovalRequestState.Pending && r.ExpiresOnUtc > utcNow)
					.OrderByDescending(r => r.CreatedOnUtc).FirstOrDefault()));
		}

		public Task<IReadOnlyList<MfaApprovalRequest>> GetRecentForUserAsync(string userId, int take, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult<IReadOnlyList<MfaApprovalRequest>>(Rows.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedOnUtc)
					.Take(take).Select(Copy).ToList());
		}

		public Task<int> CountCreatedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.Count(r => r.UserId == userId && r.CreatedOnUtc > sinceUtc));
		}

		public Task<MfaApprovalRequestState?> RecordWrongNumberAsync(string approvalRequestId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Pending(approvalRequestId, utcNow);
				if (row == null)
					return Task.FromResult<MfaApprovalRequestState?>(null);

				row.Attempts++;
				row.Version++;
				if (row.Attempts >= row.MaxAttempts)
				{
					row.State = (int)MfaApprovalRequestState.Denied;
					row.EndReason = (int)MfaApprovalEndReason.TooManyAttempts;
					row.DecidedOnUtc = utcNow;
				}

				return Task.FromResult<MfaApprovalRequestState?>(row.RequestState);
			}
		}

		public Task<bool> TryApproveAsync(string approvalRequestId, string approverSessionId, string approverPasskeyId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Pending(approvalRequestId, utcNow);
				if (row == null || row.Attempts >= row.MaxAttempts)
					return Task.FromResult(false);

				Transition(row, MfaApprovalRequestState.Approved, utcNow);
				row.ApproverSessionId = approverSessionId;
				row.ApproverPasskeyId = approverPasskeyId;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryDenyAsync(string approvalRequestId, MfaApprovalEndReason reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Pending(approvalRequestId, utcNow);
				if (row == null)
					return Task.FromResult(false);

				Transition(row, MfaApprovalRequestState.Denied, utcNow, reason);
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryCancelAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.MfaApprovalRequestId == approvalRequestId && r.RequestState == MfaApprovalRequestState.Pending &&
					r.RequesterKind == (int)requesterKind && r.RequesterId == requesterId);
				if (row == null)
					return Task.FromResult(false);

				Transition(row, MfaApprovalRequestState.Canceled, utcNow, MfaApprovalEndReason.CanceledByRequester);
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryConsumeAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, DateTime utcNow,
			TimeSpan graceAfterExpiry, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.MfaApprovalRequestId == approvalRequestId && r.RequestState == MfaApprovalRequestState.Approved &&
					r.RequesterKind == (int)requesterKind && r.RequesterId == requesterId && r.ExpiresOnUtc > utcNow - graceAfterExpiry);
				if (row == null)
					return Task.FromResult(false);

				Transition(row, MfaApprovalRequestState.Consumed);
				row.ConsumedOnUtc = utcNow;
				return Task.FromResult(true);
			}
		}

		public Task<int> CancelPendingForUserAsync(string userId, MfaApprovalEndReason reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var rows = Rows.Where(r => r.UserId == userId && r.RequestState == MfaApprovalRequestState.Pending).ToList();
				rows.ForEach(r => Transition(r, MfaApprovalRequestState.Canceled, utcNow, reason));
				return Task.FromResult(rows.Count);
			}
		}

		public Task<int> PurgeCreatedBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.CreatedOnUtc < utcCutoff));
		}

		private MfaApprovalRequest Pending(string id, DateTime utcNow) =>
			Rows.SingleOrDefault(r => r.MfaApprovalRequestId == id && r.RequestState == MfaApprovalRequestState.Pending && r.ExpiresOnUtc > utcNow);

		private static void Transition(MfaApprovalRequest row, MfaApprovalRequestState state, DateTime? decidedOn = null, MfaApprovalEndReason? reason = null)
		{
			row.State = (int)state;
			row.Version++;
			if (decidedOn != null)
				row.DecidedOnUtc = decidedOn;
			if (reason != null)
				row.EndReason = (int)reason;
		}

		private static MfaApprovalRequest Copy(MfaApprovalRequest row) => row == null ? null : (MfaApprovalRequest)typeof(object)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
	}
}
