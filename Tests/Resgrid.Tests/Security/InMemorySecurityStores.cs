using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>Same observable rules as SecurityNoticeRepository: a lease claims a notice, and only the lease holder records a result.</summary>
	internal sealed class InMemorySecurityNoticeRepository : ISecurityNoticeRepository
	{
		private readonly object _gate = new();
		public readonly List<SecurityNotice> Rows = new();

		public Task InsertAsync(SecurityNotice notice, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows.Add(Copy(notice));
			return Task.CompletedTask;
		}

		public Task<SecurityNotice> GetAsync(string securityNoticeId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.SingleOrDefault(r => r.SecurityNoticeId == securityNoticeId)));
		}

		public Task<bool> TryClaimAsync(string securityNoticeId, string owner, DateTime utcNow, DateTime leaseUntilUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.SecurityNoticeId == securityNoticeId && Due(r, utcNow));
				if (row == null)
					return Task.FromResult(false);
				row.LeaseOwner = owner;
				row.LeaseUntilUtc = leaseUntilUtc;
				return Task.FromResult(true);
			}
		}

		public Task<IReadOnlyList<SecurityNotice>> ClaimDueAsync(string owner, DateTime utcNow, DateTime leaseUntilUtc, int batchSize,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var due = Rows.Where(r => Due(r, utcNow)).OrderBy(r => r.NextAttemptOnUtc).Take(batchSize).ToList();
				due.ForEach(r => { r.LeaseOwner = owner; r.LeaseUntilUtc = leaseUntilUtc; });
				return Task.FromResult<IReadOnlyList<SecurityNotice>>(due.Select(Copy).ToList());
			}
		}

		public Task<bool> MarkSentAsync(string securityNoticeId, string owner, DateTime utcNow, CancellationToken cancellationToken = default) =>
			Finish(securityNoticeId, owner, r => { r.State = (int)SecurityNoticeState.Sent; r.SentOnUtc = utcNow; r.LastFailure = null; });

		public Task<bool> MarkRetryAsync(string securityNoticeId, string owner, DateTime nextAttemptOnUtc, string failure, CancellationToken cancellationToken = default) =>
			Finish(securityNoticeId, owner, r => { r.NextAttemptOnUtc = nextAttemptOnUtc; r.LastFailure = failure; });

		public Task<bool> MarkFailedAsync(string securityNoticeId, string owner, string failure, CancellationToken cancellationToken = default) =>
			Finish(securityNoticeId, owner, r => { r.State = (int)SecurityNoticeState.Failed; r.LastFailure = failure; });

		public Task<bool> ExistsSinceAsync(string userId, SecurityNoticeKind kind, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Rows.Any(r => r.UserId == userId && r.Kind == (int)kind && r.CreatedOnUtc > sinceUtc &&
					r.NoticeState != SecurityNoticeState.Failed));
		}

		public Task<int> PurgeFinishedBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.NoticeState != SecurityNoticeState.Pending && r.CreatedOnUtc < utcCutoff));
		}

		public Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.UserId == userId));
		}

		private static bool Due(SecurityNotice r, DateTime utcNow) =>
			r.NoticeState == SecurityNoticeState.Pending && r.NextAttemptOnUtc <= utcNow && (r.LeaseUntilUtc == null || r.LeaseUntilUtc < utcNow);

		private Task<bool> Finish(string id, string owner, Action<SecurityNotice> change)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.SecurityNoticeId == id && r.NoticeState == SecurityNoticeState.Pending && r.LeaseOwner == owner);
				if (row == null)
					return Task.FromResult(false);
				change(row);
				row.Attempts++;
				row.LeaseOwner = null;
				row.LeaseUntilUtc = null;
				return Task.FromResult(true);
			}
		}

		private static SecurityNotice Copy(SecurityNotice row) => row == null ? null : (SecurityNotice)typeof(object)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
	}

	/// <summary>Same observable rules as FactorRecoveryTransactionRepository.</summary>
	internal sealed class InMemoryFactorRecoveryTransactionRepository : IFactorRecoveryTransactionRepository
	{
		private readonly object _gate = new();
		public readonly List<FactorRecoveryTransaction> Rows = new();

		/// <summary>Runs just before a completion is attempted, standing in for a concurrent request that gets there first.</summary>
		public Action BeforeTryComplete;

		public Task InsertAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows.Add(Copy(transaction));
			return Task.CompletedTask;
		}

		public Task<FactorRecoveryTransaction> GetBySecretHashAsync(byte[] secretHash, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.SingleOrDefault(r => r.SecretHash.AsSpan().SequenceEqual(secretHash))));
		}

		public Task RecordFailedAttemptAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.FactorRecoveryTransactionId == transactionId && r.RecoveryState == FactorRecoveryState.Pending);
				if (row != null)
				{
					row.Attempts++;
					if (row.Attempts >= row.MaxAttempts)
						row.State = (int)FactorRecoveryState.Exhausted;
				}
			}

			return Task.CompletedTask;
		}

		public Task<bool> TryCompleteAsync(string transactionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				BeforeTryComplete?.Invoke();
				var row = Rows.SingleOrDefault(r => r.FactorRecoveryTransactionId == transactionId && r.RecoveryState == FactorRecoveryState.Pending &&
					r.ExpiresOnUtc > utcNow && r.Attempts < r.MaxAttempts);
				if (row == null)
					return Task.FromResult(false);
				row.State = (int)FactorRecoveryState.Completed;
				row.CompletedOnUtc = utcNow;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryCancelAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.FactorRecoveryTransactionId == transactionId && r.RecoveryState == FactorRecoveryState.Pending);
				if (row == null)
					return Task.FromResult(false);
				row.State = (int)FactorRecoveryState.Canceled;
				return Task.FromResult(true);
			}
		}

		public Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.ExpiresOnUtc < utcCutoff));
		}

		public Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.UserId == userId));
		}

		private static FactorRecoveryTransaction Copy(FactorRecoveryTransaction row) => row == null ? null : (FactorRecoveryTransaction)typeof(object)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
	}
	/// <summary>Same observable rules as MfaActivityRepository: newest first, and a row is reported once.</summary>
	internal sealed class InMemoryMfaActivityRepository : IMfaActivityRepository
	{
		private readonly object _gate = new();
		public readonly List<MfaActivity> Rows = new();

		private static MfaActivity Copy(MfaActivity a) => a == null ? null : (MfaActivity)a.GetType()
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(a, null);

		public Task InsertAsync(MfaActivity activity, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows.Add(Copy(activity));
			return Task.CompletedTask;
		}

		public Task<int> CountDeniedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.Count(r => r.UserId == userId && !r.Successful && r.OccurredOnUtc >= sinceUtc));
		}

		public Task<IReadOnlyList<MfaActivity>> GetRecentAsync(string userId, DateTime sinceUtc, int take, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult<IReadOnlyList<MfaActivity>>(Rows.Where(r => r.UserId == userId && r.OccurredOnUtc >= sinceUtc)
					.OrderByDescending(r => r.OccurredOnUtc).Take(take).Select(Copy).ToList());
		}

		public Task<MfaActivity> GetAsync(string mfaActivityId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.SingleOrDefault(r => r.MfaActivityId == mfaActivityId)));
		}

		public Task<bool> TryMarkReportedAsync(string mfaActivityId, string userId, DateTime reportedOnUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.MfaActivityId == mfaActivityId && r.UserId == userId && r.ReportedOnUtc == null);
				if (row == null)
					return Task.FromResult(false);
				row.ReportedOnUtc = reportedOnUtc;
				return Task.FromResult(true);
			}
		}

		public Task<int> PurgeBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.OccurredOnUtc < utcCutoff));
		}

		public Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.UserId == userId));
		}
	}
}
