using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A member's Workforce Pro seat in one department (shifts-scheduling-modernization-plan.md §7.2 "per-member switch"): a manager switched
	/// Workforce Pro on for the member, so their wellness and workplace-exposure records can be created and edited while the department holds
	/// enough packs. Seats are per department. A seat held by a member who is no longer active (deleted, disabled or hidden) is dormant and not
	/// counted. Switching a seat off never deletes a record: the member's records stay readable (and exportable) by the member and those
	/// already permitted, only no longer editable. No foreign keys; UnlinkedDepartmentCleanup removes the rows with the department.
	/// </summary>
	[Table("WorkforceProSeats")]
	public class WorkforceProSeat : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int WorkforceProSeatId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>When the seat was switched on, UTC. Seats are licensed in this order when a department holds more enabled members than seats.</summary>
		public DateTime EnabledOn { get; set; }

		public string EnabledByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WorkforceProSeatId; }
			set { WorkforceProSeatId = (int)value; }
		}

		[NotMapped]
		public string TableName => "WorkforceProSeats";

		[NotMapped]
		public string IdName => "WorkforceProSeatId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// What Core last saw of a department's Workforce Pro add-on (one row per department that ever held it; the LMS's LmsAddonStates shape):
	/// the packs and paid-through date of its last interval, whether its renewal was turned off, and when it ended. The Billing API answers
	/// only intervals still running, so this is how the gate tells a renewal that failed (the dunning grace,
	/// WorkforceProAddonConfig.DunningGraceDays) from one that was cancelled, refunded or charged back. Written by the gate only when something
	/// changed. No foreign keys; UnlinkedDepartmentCleanup removes the row with the department.
	/// </summary>
	[Table("WorkforceProAddonStates")]
	public class WorkforceProAddonState : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int WorkforceProAddonStateId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Packs in force on the last interval seen.</summary>
		public int Packs { get; set; }

		/// <summary>End of the last paid interval seen, UTC.</summary>
		public DateTime? PaidThroughUtc { get; set; }

		/// <summary>The last interval seen had its renewal turned off (it ends at <see cref="PaidThroughUtc"/>, with no dunning grace).</summary>
		public bool RenewalCancelled { get; set; }

		/// <summary>When Core last saw a running interval, UTC.</summary>
		public DateTime? LastActiveOn { get; set; }

		/// <summary>
		/// When the add-on ended, UTC (the end of the paid month, the end of the dunning grace, or the moment a revoked interval disappeared);
		/// null while it runs or is in its grace. Once set, no later read grants the grace again until a running interval is seen.
		/// </summary>
		public DateTime? LapsedSinceUtc { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return WorkforceProAddonStateId; }
			set { WorkforceProAddonStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "WorkforceProAddonStates";

		[NotMapped]
		public string IdName => "WorkforceProAddonStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Where a department's Workforce Pro add-on stands for the gate.</summary>
	public enum WorkforceProSubscriptionStates
	{
		/// <summary>Billing could not answer: callers fail closed (read-only).</summary>
		Unknown = 0,

		/// <summary>
		/// Never bought, cancelled and run out, refunded or charged back, a failed renewal whose dunning grace ran out, or billing is not
		/// configured (self-hosted): no add-on.
		/// </summary>
		None = 1,

		/// <summary>A paid interval is running.</summary>
		Active = 2,

		/// <summary>
		/// A renewal failed and the dunning grace is running (WorkforceProAddonConfig.DunningGraceDays, 7): Workforce Pro keeps working with
		/// the packs last paid for while the provider retries the payment.
		/// </summary>
		DunningGrace = 3
	}

	/// <summary>The Workforce Pro subscription states that license the add-on.</summary>
	public static class WorkforceProSubscription
	{
		/// <summary>A running interval or the dunning grace: Workforce Pro works.</summary>
		public static bool IsEntitled(WorkforceProSubscriptionStates state) =>
			state == WorkforceProSubscriptionStates.Active || state == WorkforceProSubscriptionStates.DunningGrace;
	}

	/// <summary>What a department may do with Workforce Pro features (wellness and workplace exposure today).</summary>
	public enum WorkforceProAccessLevels
	{
		/// <summary>The Workforce.Pro flag is off: the features are invisible and their routes 404. Data is kept.</summary>
		None = 0,

		/// <summary>Offered but not licensed (never bought, cancelled or lapsed, or billing could not answer): existing records stay readable.</summary>
		ReadOnly = 1,

		/// <summary>Licensed: records can be created and edited for members holding a seat.</summary>
		Full = 2
	}

	/// <summary>The refusal codes of the Workforce Pro gate (the web and v4 layers map them like any <see cref="MemberWellnessException"/> code).</summary>
	public static class WorkforceProCodes
	{
		/// <summary>The Workforce.Pro flag is off for the department (routes answer 404).</summary>
		public const string Unavailable = "workforce_pro_unavailable";

		/// <summary>The department holds no running Workforce Pro add-on: records are read-only.</summary>
		public const string Required = "workforce_pro_required";

		/// <summary>The member the record is about holds no Workforce Pro seat: their records are read-only.</summary>
		public const string SeatRequired = "workforce_pro_seat_required";
	}

	/// <summary>
	/// A Workforce Pro gate refusal. A <see cref="MemberWellnessException"/>, so every wellness controller maps it to its existing error shape
	/// (the web's flashed "Error.{code}" message, the v4 API's 400 with the code); <see cref="WorkforceProCodes.Unavailable"/> is answered
	/// with 404 by the controllers before it can be thrown.
	/// </summary>
	public class WorkforceProAccessException : MemberWellnessException
	{
		public WorkforceProAccessException(string code) : base(code)
		{
		}

		public bool IsUnavailable => Code == WorkforceProCodes.Unavailable;
	}

	/// <summary>
	/// One evaluation of the Workforce Pro gate for a department (IWorkforceProAccessService): the fresh operator flag, the live add-on window
	/// (no entitlement cache) with its packs, and the members holding a licensed seat. The rules every wellness service enforces:
	/// <list type="bullet">
	/// <item>flag off: nothing (<see cref="EnsureReadable"/> refuses with <see cref="WorkforceProCodes.Unavailable"/>);</item>
	/// <item>flag on, not licensed: read-only; nothing is created, edited or deleted and no sweep, alert or notice runs
	/// (<see cref="EnsureWritable"/> refuses with <see cref="WorkforceProCodes.Required"/>);</item>
	/// <item>licensed: a record about a member is created or edited only while that member holds a seat (<see cref="EnsureWritableFor"/>
	/// refuses with <see cref="WorkforceProCodes.SeatRequired"/>). Officers acting on others need their permission, not a seat.</item>
	/// </list>
	/// Reading stays with the record rules: a member always reads their own records, and officers read what their role opens.
	/// </summary>
	public sealed class WorkforceProAccess
	{
		public int DepartmentId { get; init; }

		/// <summary>The Workforce.Pro flag is on for the department (fresh evaluation).</summary>
		public bool Offered { get; init; }

		public WorkforceProSubscriptionStates State { get; init; }

		/// <summary>Packs in force (0 without a running interval or the dunning grace).</summary>
		public int Packs { get; init; }

		public int PackSize { get; init; }

		/// <summary>End of the running interval (or of the last one, while in the dunning grace), UTC.</summary>
		public DateTime? PaidThroughUtc { get; init; }

		/// <summary>The running interval has its renewal turned off (it ends at <see cref="PaidThroughUtc"/>).</summary>
		public bool RenewalCancelled { get; init; }

		/// <summary>When the dunning grace ends, UTC, while <see cref="State"/> is <see cref="WorkforceProSubscriptionStates.DunningGrace"/>.</summary>
		public DateTime? GraceEndsUtc { get; init; }

		/// <summary>The active members holding a licensed seat now (enabled, active, within the purchased seats in enable order); empty unless licensed.</summary>
		public HashSet<string> SeatHolderUserIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The flag on and a running add-on, or a failed renewal inside its dunning grace.</summary>
		public bool Licensed => Offered && WorkforceProSubscription.IsEntitled(State);

		/// <summary>A renewal failed and Workforce Pro keeps working until <see cref="GraceEndsUtc"/> while the payment is retried.</summary>
		public bool InDunningGrace => Offered && State == WorkforceProSubscriptionStates.DunningGrace;

		public WorkforceProAccessLevels Level => !Offered ? WorkforceProAccessLevels.None : Licensed ? WorkforceProAccessLevels.Full : WorkforceProAccessLevels.ReadOnly;

		public bool CanRead => Offered;

		public bool IsReadOnly => Offered && !Licensed;

		public int PurchasedSeats => Licensed ? Math.Max(0, Packs) * Math.Max(1, PackSize) : 0;

		/// <summary>The member holds a licensed seat (so records about them can be created and edited).</summary>
		public bool HoldsSeat(string userId) => Licensed && !string.IsNullOrWhiteSpace(userId) && SeatHolderUserIds != null && SeatHolderUserIds.Contains(userId);

		/// <summary>Refuses unless the features are offered (flag on). Reading is then up to the record rules.</summary>
		public void EnsureReadable()
		{
			if (!Offered)
				throw new WorkforceProAccessException(WorkforceProCodes.Unavailable);
		}

		/// <summary>Refuses unless offered and licensed: department-level writes (settings, call flags) and every sweep, alert and notice.</summary>
		public void EnsureWritable()
		{
			EnsureReadable();
			if (!Licensed)
				throw new WorkforceProAccessException(WorkforceProCodes.Required);
		}

		/// <summary>Refuses unless offered, licensed and the record's member holds a seat.</summary>
		public void EnsureWritableFor(string subjectUserId)
		{
			EnsureWritable();
			if (!HoldsSeat(subjectUserId))
				throw new WorkforceProAccessException(WorkforceProCodes.SeatRequired);
		}

		public static WorkforceProAccess NotOffered(int departmentId) => new WorkforceProAccess { DepartmentId = departmentId, State = WorkforceProSubscriptionStates.None };
	}

	/// <summary>A department's Workforce Pro seat count (the seats page and the billing page).</summary>
	public sealed class WorkforceProSeatSummary
	{
		public bool Offered { get; init; }
		public WorkforceProSubscriptionStates State { get; init; }
		public DateTime? PaidThroughUtc { get; init; }

		/// <summary>When the dunning grace ends (State DunningGrace), UTC.</summary>
		public DateTime? GraceEndsUtc { get; init; }

		public int PurchasedPacks { get; init; }
		public int PackSize { get; init; }

		/// <summary>The seats the department may fill: packs × the pack size, while the add-on runs or is in its dunning grace.</summary>
		public int PurchasedSeats => WorkforceProSubscription.IsEntitled(State) ? Math.Max(0, PurchasedPacks) * Math.Max(1, PackSize) : 0;

		/// <summary>Active members who hold a seat (dormant seats of inactive members are not counted).</summary>
		public int EnabledSeats { get; init; }

		/// <summary>Seats held by members who are no longer active; not counted, kept so a returning member is not silently dropped.</summary>
		public int DormantSeats { get; init; }

		public HashSet<string> EnabledUserIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The members whose records can be created and edited now (enabled, active, within the purchased seats, add-on running).</summary>
		public HashSet<string> LicensedUserIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public int AvailableSeats => Math.Max(0, PurchasedSeats - EnabledSeats);

		/// <summary>The fewest packs that still seat every enabled member; a pack decrease never goes below it.</summary>
		public int MinimumPacks { get; init; }

		public bool Licensed => Offered && WorkforceProSubscription.IsEntitled(State);

		/// <summary>A renewal failed and Workforce Pro keeps working until <see cref="GraceEndsUtc"/>.</summary>
		public bool InDunningGrace => Offered && State == WorkforceProSubscriptionStates.DunningGrace && GraceEndsUtc.HasValue;
	}

	/// <summary>The outcome of switching one member's Workforce Pro seat.</summary>
	public enum WorkforceProSeatOutcomes
	{
		Enabled = 0,
		Disabled = 1,
		Unchanged = 2,

		/// <summary>Only active members (not deleted, disabled or hidden) can hold a seat.</summary>
		NotActiveMember = 3,

		/// <summary>Every purchased seat is taken: add a pack first.</summary>
		SeatsFull = 4,

		/// <summary>The department has no running Workforce Pro add-on (never bought, lapsed, or billing could not answer).</summary>
		AddonInactive = 5,

		/// <summary>Workforce Pro is switched off for the department (operator flag).</summary>
		NotOffered = 6,

		/// <summary>Only a department administrator switches seats.</summary>
		NotAllowed = 7
	}

	public sealed class WorkforceProSeatChangeResult
	{
		public WorkforceProSeatOutcomes Outcome { get; init; }
		public WorkforceProSeatSummary Summary { get; init; }

		/// <summary>Packs to add so the refused member fits (SeatsFull only).</summary>
		public int PacksNeeded { get; init; }

		public bool Succeeded => Outcome == WorkforceProSeatOutcomes.Enabled || Outcome == WorkforceProSeatOutcomes.Disabled || Outcome == WorkforceProSeatOutcomes.Unchanged;
	}

	/// <summary>
	/// The Workforce Pro seat rules as pure functions (the LMS seat rules, §17.4, applied to Workforce Pro seats). Only active members hold a
	/// counted seat; when a department holds more enabled active members than purchased seats (a member came back from being disabled, or packs
	/// were bought back lower after a lapse), seats are licensed in the order they were enabled. User ids compare case-insensitively.
	/// </summary>
	public static class WorkforceProSeatRules
	{
		/// <summary>The active members' seats, in licensing order (enabled first, then row id).</summary>
		public static List<WorkforceProSeat> CountedSeats(IEnumerable<WorkforceProSeat> seats, ISet<string> activeUserIds)
		{
			var active = new HashSet<string>(activeUserIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			return (seats ?? Enumerable.Empty<WorkforceProSeat>())
				.Where(s => s != null && !string.IsNullOrWhiteSpace(s.UserId) && active.Contains(s.UserId))
				.GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.OrderBy(s => s.EnabledOn).ThenBy(s => s.WorkforceProSeatId).First())
				.OrderBy(s => s.EnabledOn).ThenBy(s => s.WorkforceProSeatId)
				.ToList();
		}

		/// <summary>The members licensed now: counted seats within <paramref name="purchasedSeats"/>, and none without a running add-on.</summary>
		public static HashSet<string> Licensed(IEnumerable<WorkforceProSeat> seats, ISet<string> activeUserIds, int purchasedSeats, bool addonRunning)
		{
			var licensed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!addonRunning || purchasedSeats <= 0)
				return licensed;

			foreach (var seat in CountedSeats(seats, activeUserIds).Take(purchasedSeats))
				licensed.Add(seat.UserId);
			return licensed;
		}

		/// <summary>The packs needed to seat <paramref name="members"/> people beyond <paramref name="purchasedSeats"/> (0 when they fit).</summary>
		public static int PacksNeeded(int members, int purchasedSeats, int packSize)
		{
			var missing = members - Math.Max(0, purchasedSeats);
			return missing <= 0 || packSize <= 0 ? 0 : (int)Math.Ceiling(missing / (double)packSize);
		}

		/// <summary>The fewest packs that seat <paramref name="enabledSeats"/> members, never below <paramref name="floor"/>.</summary>
		public static int MinimumPacks(int enabledSeats, int packSize, int floor) =>
			Math.Max(Math.Max(1, floor), packSize <= 0 ? 0 : (int)Math.Ceiling(Math.Max(0, enabledSeats) / (double)packSize));
	}
}
