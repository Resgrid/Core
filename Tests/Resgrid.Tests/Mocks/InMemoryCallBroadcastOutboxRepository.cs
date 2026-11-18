using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// <see cref="ICallBroadcastOutboxRepository"/> over a list, with the same conditional state changes as the SQL (claim only an
	/// open row, end or hand back only under the claim that holds it).
	/// </summary>
	public class InMemoryCallBroadcastOutboxRepository : ICallBroadcastOutboxRepository
	{
		private readonly object _gate = new object();

		public List<CallBroadcastOutboxEntry> Rows { get; } = new List<CallBroadcastOutboxEntry>();

		/// <summary>Set to make the next insert throw (the row cannot be written).</summary>
		public bool FailInserts { get; set; }

		public Task InsertEntryAsync(CallBroadcastOutboxEntry entry, CancellationToken cancellationToken = default)
		{
			if (FailInserts)
				throw new InvalidOperationException("insert failed");

			lock (_gate)
				Rows.Add(Copy(entry));

			return Task.CompletedTask;
		}

		public Task<CallBroadcastOutboxEntry> GetEntryAsync(string callBroadcastOutboxId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Copy(Find(callBroadcastOutboxId)));
		}

		public Task<IEnumerable<string>> GetOpenIdsForCallAsync(int callId, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult<IEnumerable<string>>(Rows.Where(x => x.CallId == callId && IsOpen(x, nowUtc)).OrderBy(x => x.CreatedOn)
					.Select(x => x.CallBroadcastOutboxId).ToList());
		}

		public Task<IEnumerable<string>> GetDueIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult<IEnumerable<string>>(Rows.Where(x => IsDue(x, nowUtc)).OrderBy(x => x.NextAttemptOn).Take(take)
					.Select(x => x.CallBroadcastOutboxId).ToList());
		}

		public Task<bool> TryClaimAsync(string callBroadcastOutboxId, string claimToken, DateTime nowUtc, DateTime leaseExpiresOnUtc, bool ignoreSchedule,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Find(callBroadcastOutboxId);
				if (row == null || !(ignoreSchedule ? IsOpen(row, nowUtc) : IsDue(row, nowUtc)))
					return Task.FromResult(false);

				row.State = (int)CallBroadcastOutboxStates.Sending;
				row.ClaimToken = claimToken;
				row.LeaseExpiresOn = leaseExpiresOnUtc;
				row.Attempts++;
				return Task.FromResult(true);
			}
		}

		public Task<bool> CompleteAsync(string callBroadcastOutboxId, string claimToken, CallBroadcastOutboxStates state, DateTime nowUtc, string error,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Find(callBroadcastOutboxId);
				if (row == null || row.State != (int)CallBroadcastOutboxStates.Sending || row.ClaimToken != claimToken)
					return Task.FromResult(false);

				row.State = (int)state;
				row.CompletedOn = nowUtc;
				row.LastError = error;
				row.LeaseExpiresOn = null;
				return Task.FromResult(true);
			}
		}

		public Task<bool> RetryLaterAsync(string callBroadcastOutboxId, string claimToken, DateTime nextAttemptOnUtc, string error,
			CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Find(callBroadcastOutboxId);
				if (row == null || row.State != (int)CallBroadcastOutboxStates.Sending || row.ClaimToken != claimToken)
					return Task.FromResult(false);

				row.State = (int)CallBroadcastOutboxStates.Pending;
				row.NextAttemptOn = nextAttemptOnUtc;
				row.LastError = error;
				row.ClaimToken = null;
				row.LeaseExpiresOn = null;
				return Task.FromResult(true);
			}
		}

		public Task<int> SupersedePendingForCallAsync(int callId, DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var rows = Rows.Where(x => x.CallId == callId && x.State == (int)CallBroadcastOutboxStates.Pending).ToList();
				foreach (var row in rows)
				{
					row.State = (int)CallBroadcastOutboxStates.Superseded;
					row.CompletedOn = nowUtc;
				}

				return Task.FromResult(rows.Count);
			}
		}

		public Task<int> PurgeCompletedBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default)
		{
			lock (_gate)
				return Task.FromResult(Rows.RemoveAll(x => x.CompletedOn.HasValue && x.CompletedOn.Value < beforeUtc));
		}

		public CallBroadcastOutboxEntry Single() => Rows.Single();

		private CallBroadcastOutboxEntry Find(string id) => Rows.FirstOrDefault(x => string.Equals(x.CallBroadcastOutboxId, id, StringComparison.OrdinalIgnoreCase));

		private static bool IsOpen(CallBroadcastOutboxEntry row, DateTime nowUtc)
		{
			return row.State == (int)CallBroadcastOutboxStates.Pending ||
				(row.State == (int)CallBroadcastOutboxStates.Sending && row.LeaseExpiresOn.HasValue && row.LeaseExpiresOn.Value < nowUtc);
		}

		private static bool IsDue(CallBroadcastOutboxEntry row, DateTime nowUtc)
		{
			return (row.State == (int)CallBroadcastOutboxStates.Pending && row.NextAttemptOn <= nowUtc) ||
				(row.State == (int)CallBroadcastOutboxStates.Sending && row.LeaseExpiresOn.HasValue && row.LeaseExpiresOn.Value < nowUtc);
		}

		private static CallBroadcastOutboxEntry Copy(CallBroadcastOutboxEntry row)
		{
			if (row == null)
				return null;

			return new CallBroadcastOutboxEntry
			{
				CallBroadcastOutboxId = row.CallBroadcastOutboxId,
				DepartmentId = row.DepartmentId,
				CallId = row.CallId,
				UserIds = row.UserIds,
				GroupIds = row.GroupIds,
				UnitIds = row.UnitIds,
				RoleIds = row.RoleIds,
				State = row.State,
				Attempts = row.Attempts,
				NextAttemptOn = row.NextAttemptOn,
				ClaimToken = row.ClaimToken,
				LeaseExpiresOn = row.LeaseExpiresOn,
				LastError = row.LastError,
				CreatedByUserId = row.CreatedByUserId,
				CreatedOn = row.CreatedOn,
				CompletedOn = row.CompletedOn
			};
		}
	}
}
