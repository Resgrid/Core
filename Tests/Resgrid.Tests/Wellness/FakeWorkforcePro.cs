using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Wellness
{
	/// <summary>
	/// The Workforce Pro gate for the wellness test worlds. By default the department is licensed (the flag on, a running add-on with 4 packs)
	/// and every member of the worlds holds a seat, so the wellness tests written before the gate keep testing the wellness rules; the gate
	/// tests switch the flag off, end the add-on or take seats away.
	/// </summary>
	internal sealed class FakeWorkforcePro : IWorkforceProAccessService
	{
		/// <summary>Every user id the wellness test worlds use.</summary>
		public static readonly string[] KnownUsers =
		{
			"admin-user", "officer-user", "member-user", "peer-user", "hidden-user", "wellness-user", "designated-user", "group-admin",
			"other-user", "outside-user", "owner"
		};

		public bool Offered { get; set; } = true;
		public WorkforceProSubscriptionStates State { get; set; } = WorkforceProSubscriptionStates.Active;
		public int Packs { get; set; } = 4;

		/// <summary>The seat holders; null: every known user holds one.</summary>
		public HashSet<string> Seats { get; set; }

		/// <summary>How often the full evaluation was asked for, per department.</summary>
		public readonly Dictionary<int, int> Asked = new Dictionary<int, int>();

		public void Lapse() => State = WorkforceProSubscriptionStates.None;

		/// <summary>A renewal failed and the seven-day dunning grace is running: Workforce Pro keeps working with the packs last paid for.</summary>
		public void FailRenewal() => State = WorkforceProSubscriptionStates.DunningGrace;

		public void SwitchOff() => Offered = false;

		public void OnlySeats(params string[] userIds) => Seats = new HashSet<string>(userIds, StringComparer.OrdinalIgnoreCase);

		public Task<bool> IsOfferedAsync(int departmentId) => Task.FromResult(Offered);

		public Task<bool> HasActiveAddonAsync(int departmentId) => Task.FromResult(WorkforceProSubscription.IsEntitled(State));

		public Task<WorkforceProAccess> GetAccessAsync(int departmentId)
		{
			Asked[departmentId] = Asked.TryGetValue(departmentId, out var n) ? n + 1 : 1;
			if (!Offered)
				return Task.FromResult(WorkforceProAccess.NotOffered(departmentId));
			var licensed = WorkforceProSubscription.IsEntitled(State);
			return Task.FromResult(new WorkforceProAccess
			{
				DepartmentId = departmentId,
				Offered = true,
				State = State,
				Packs = licensed ? Packs : 0,
				PackSize = 25,
				GraceEndsUtc = State == WorkforceProSubscriptionStates.DunningGrace ? DateTime.UtcNow.AddDays(3) : null,
				SeatHolderUserIds = licensed ? new HashSet<string>(Seats ?? KnownUsers.AsEnumerable(), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			});
		}

		public void Invalidate(int departmentId)
		{
		}
	}
}
