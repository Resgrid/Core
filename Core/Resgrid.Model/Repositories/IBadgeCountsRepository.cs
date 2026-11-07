using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>A member's certification records that need attention.</summary>
	public sealed class CertificationAttentionCounts
	{
		/// <summary>Expired by status or past their expiry date.</summary>
		public int Expired { get; set; }

		/// <summary>Not expired, expiring on or before the horizon.</summary>
		public int Expiring { get; set; }
	}

	/// <summary>A member's shift trade requests still in play.</summary>
	public sealed class ShiftTradeCounts
	{
		/// <summary>Requests sent to the member they have not answered (no offer, not declined).</summary>
		public int AwaitingMe { get; set; }

		/// <summary>The member's own requests nobody has taken yet, or waiting for a supervisor's approval.</summary>
		public int Mine { get; set; }
	}

	/// <summary>
	/// The member badge counts (v4 Counts/GetBadgeCounts) as single COUNT statements, one per list: the same rules the
	/// list endpoints apply, without loading the rows. Both dialects; read-only.
	/// </summary>
	public interface IBadgeCountsRepository
	{
		/// <param name="today">The department-local date.</param>
		/// <param name="horizonEnd">The last date that still counts as expiring.</param>
		Task<CertificationAttentionCounts> GetCertificationAttentionAsync(int departmentId, string userId, DateTime today, DateTime horizonEnd);

		/// <summary>Open deployments (planned, standby, active, demobilizing) the member is on the current roster of.</summary>
		Task<int> CountOpenDeploymentsForUserAsync(int departmentId, string userId);

		/// <summary>
		/// Scheduled checklist occurrences the member can start now (in progress, awaiting witness, scheduled or missed),
		/// on an active schedule assigned to them, not someone else's run. With <paramref name="unitId"/> only the unit's own
		/// checks; otherwise every check except other members' personal ones.
		/// </summary>
		Task<int> CountDueChecklistsAsync(int departmentId, string userId, int? unitId, DateTime nowUtc);

		/// <summary>
		/// Shift days in [<paramref name="dayStart"/>, <paramref name="dayEnd"/>) the member is on the roster of: an own signup
		/// that was not denied or traded away, a standing assignment they are not off from that day, or a day they took over
		/// through a completed trade.
		/// </summary>
		Task<int> CountShiftDaysForUserAsync(int departmentId, string userId, DateTime dayStart, DateTime dayEnd);

		/// <summary>The member's trade requests for days from <paramref name="fromDay"/> on.</summary>
		Task<ShiftTradeCounts> GetShiftTradeCountsAsync(int departmentId, string userId, DateTime fromDay);
	}

	/// <summary>Work orders open and assigned to a member (WorkOrderRepository, which owns the assignment rules).</summary>
	public interface IWorkOrderCountRepository
	{
		/// <summary>Not deleted, not in a terminal status, assigned to the user or one of <paramref name="roleIds"/>.</summary>
		Task<int> CountOpenAssignedAsync(int departmentId, string userId, int[] roleIds);
	}
}
