using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A member's LMS seat in one department (§17.4 seat rules, decision 7): the member is LMS-enabled, so they can be trained
	/// through LMS features while the department holds enough packs. An LMS table: it references the department only and nothing
	/// baseline references it (§17.3). Seats are per department: a person in two departments needs a seat in each. A seat held by a
	/// member who is no longer active (deleted, disabled or hidden) is dormant and is not counted; it is never deleted by a lapse
	/// (lapse rule 1), only by an admin switching it off or the department's deletion.
	/// </summary>
	[Table("LmsMemberSeats")]
	public class LmsMemberSeat : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsMemberSeatId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>When the member was LMS-enabled, UTC. Seats are licensed in this order when a department holds more enabled members than seats.</summary>
		public DateTime EnabledOn { get; set; }

		public string EnabledByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsMemberSeatId; }
			set { LmsMemberSeatId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsMemberSeats";

		[NotMapped]
		public string IdName => "LmsMemberSeatId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// What Core last saw of a department's LMS add-on (one row per department that ever held it): the packs and paid-through date
	/// of its current interval and whether its renewal was turned off. The Billing API answers only intervals still running, so this
	/// is how Core tells a renewal that failed (dunning grace, lapse rule 13) from one that ended, and when a lapse began. Written by
	/// the gate only when something changed, never from a sweep (R-L9).
	/// </summary>
	[Table("LmsAddonStates")]
	public class LmsAddonState : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsAddonStateId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Packs in force on the last interval seen.</summary>
		public int Packs { get; set; }

		/// <summary>End of the last paid interval seen, UTC.</summary>
		public DateTime? PaidThroughUtc { get; set; }

		/// <summary>The last interval seen had its renewal turned off (it ends at <see cref="PaidThroughUtc"/>, with no dunning grace).</summary>
		public bool RenewalCancelled { get; set; }

		/// <summary>When Core last saw a running interval, UTC.</summary>
		public DateTime? LastActiveOn { get; set; }

		/// <summary>When the open lapse began, UTC (mirrors the open LmsAddonLapses row so the gate reads one row); null while active.</summary>
		public DateTime? LapsedSinceUtc { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsAddonStateId; }
			set { LmsAddonStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsAddonStates";

		[NotMapped]
		public string IdName => "LmsAddonStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Why an LMS add-on lapsed. Persisted in LmsAddonLapses.Reason; append only.</summary>
	public enum LmsLapseReasons
	{
		Unknown = 0,

		/// <summary>The renewal was turned off and the paid month ran out.</summary>
		RenewalCancelled = 1,

		/// <summary>A renewal payment failed and the dunning grace (LmsAddonConfig.DunningGraceDays) ran out.</summary>
		PaymentFailed = 2,

		/// <summary>The running interval was cut short (a refund or chargeback revoked it).</summary>
		Revoked = 3
	}

	/// <summary>
	/// One period in which a department's LMS add-on had lapsed (§17.3 lapse rules): from the end of the paid month (plus the dunning
	/// grace for a failed payment) to the moment Core saw it running again. The reactivation re-base (rule 12) shifts paused due dates
	/// by these periods. At most one open lapse per department (a filtered unique index).
	/// </summary>
	[Table("LmsAddonLapses")]
	public class LmsAddonLapse : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsAddonLapseId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>When the lapse rules began to apply, UTC.</summary>
		public DateTime StartedOn { get; set; }

		/// <summary>When the add-on was seen running again, UTC; null while the lapse lasts.</summary>
		public DateTime? EndedOn { get; set; }

		/// <summary>The paid-through date of the interval that ran out, UTC.</summary>
		public DateTime? PaidThroughUtc { get; set; }

		/// <summary><see cref="LmsLapseReasons"/>.</summary>
		public int Reason { get; set; }

		/// <summary>Packs held when the add-on lapsed (seats stay enabled across a lapse; lapse rule 1).</summary>
		public int Packs { get; set; }

		public DateTime DetectedOn { get; set; }

		[NotMapped]
		public bool IsOpen => !EndedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsAddonLapseId; }
			set { LmsAddonLapseId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsAddonLapses";

		[NotMapped]
		public string IdName => "LmsAddonLapseId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>Where a department's LMS add-on stands for the gate.</summary>
	public enum LmsSubscriptionStates
	{
		/// <summary>Billing could not answer: callers fail closed.</summary>
		Unknown = 0,

		/// <summary>Never held, or billing is not configured (self-hosted): no add-on.</summary>
		None = 1,

		/// <summary>A paid interval is running.</summary>
		Active = 2,

		/// <summary>A renewal failed and the dunning grace is running: the LMS keeps working (lapse rule 13).</summary>
		DunningGrace = 3,

		/// <summary>The add-on ended: the lapse rules apply.</summary>
		Lapsed = 4
	}

	/// <summary>One answer from the LMS gate's billing read: the add-on's state and the packs it holds.</summary>
	public sealed class LmsSubscriptionSnapshot
	{
		public LmsSubscriptionStates State { get; init; }

		/// <summary>Packs in force (0 without a running or grace interval).</summary>
		public int Packs { get; init; }

		/// <summary>End of the paid interval in force (or of the last one, while in grace or lapsed), UTC.</summary>
		public DateTime? PaidThroughUtc { get; init; }

		/// <summary>Start of the paid interval in force, UTC (for a proration preview).</summary>
		public DateTime? PeriodStartUtc { get; init; }

		/// <summary>The interval in force has its renewal turned off.</summary>
		public bool RenewalCancelled { get; init; }

		/// <summary>When the dunning grace ends, UTC, while in <see cref="LmsSubscriptionStates.DunningGrace"/>.</summary>
		public DateTime? GraceEndsUtc { get; init; }

		/// <summary>When the current lapse began, UTC, while <see cref="LmsSubscriptionStates.Lapsed"/>.</summary>
		public DateTime? LapsedSinceUtc { get; init; }

		/// <summary>The LMS works: a running interval or the dunning grace.</summary>
		public bool IsEntitled => State == LmsSubscriptionStates.Active || State == LmsSubscriptionStates.DunningGrace;

		/// <summary>The gate's tri-state answer: true, false, or null when billing could not answer.</summary>
		public bool? Entitlement => State == LmsSubscriptionStates.Unknown ? null : IsEntitled;

		public static LmsSubscriptionSnapshot Unknown { get; } = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Unknown };
		public static LmsSubscriptionSnapshot None { get; } = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.None };
	}

	/// <summary>A department's seat count (the personnel page counter and the LMS settings section).</summary>
	public sealed class LmsSeatSummary
	{
		public LmsSubscriptionStates State { get; init; }

		/// <summary>When the dunning grace ends (State DunningGrace), UTC.</summary>
		public DateTime? GraceEndsUtc { get; init; }

		/// <summary>End of the paid interval in force or last held, UTC.</summary>
		public DateTime? PaidThroughUtc { get; init; }

		public int PurchasedPacks { get; init; }
		public int PackSize { get; init; }
		public int PurchasedSeats => PurchasedPacks * PackSize;

		/// <summary>Active members who hold a seat (dormant seats of inactive members are not counted).</summary>
		public int EnabledSeats { get; init; }

		/// <summary>Seats held by members who are no longer active; not counted, kept so a returning member is not silently dropped.</summary>
		public int DormantSeats { get; init; }

		/// <summary>The active members who hold a seat, case-insensitive.</summary>
		public HashSet<string> EnabledUserIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The members who can be trained through LMS features now: enabled, active, within the purchased seats (in enable order) and with the add-on working.</summary>
		public HashSet<string> LicensedUserIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public int AvailableSeats => Math.Max(0, PurchasedSeats - EnabledSeats);

		/// <summary>The fewest packs that still seat every enabled member; a pack decrease never goes below it.</summary>
		public int MinimumPacks { get; init; }
	}

	/// <summary>The outcome of switching one member's seat.</summary>
	public enum LmsSeatOutcomes
	{
		Enabled = 0,
		Disabled = 1,
		Unchanged = 2,

		/// <summary>Only active members (not deleted, disabled or hidden) can hold a seat.</summary>
		NotActiveMember = 3,

		/// <summary>Every purchased seat is taken: add a pack first.</summary>
		SeatsFull = 4,

		/// <summary>The department has no working LMS add-on (never bought, lapsed, or billing could not answer).</summary>
		AddonInactive = 5,

		/// <summary>The LMS is switched off for the department (operator flag or Trainings module).</summary>
		NotOffered = 6
	}

	public sealed class LmsSeatChangeResult
	{
		public LmsSeatOutcomes Outcome { get; init; }
		public LmsSeatSummary Summary { get; init; }

		/// <summary>Packs to add so the refused members fit (SeatsFull only).</summary>
		public int PacksNeeded { get; init; }

		public bool Succeeded => Outcome == LmsSeatOutcomes.Enabled || Outcome == LmsSeatOutcomes.Disabled || Outcome == LmsSeatOutcomes.Unchanged;
	}

	/// <summary>The outcome of a bulk switch by group.</summary>
	public sealed class LmsSeatBulkResult
	{
		public LmsSeatOutcomes Outcome { get; init; }
		public int Changed { get; init; }
		public int AlreadySet { get; init; }

		/// <summary>Group members skipped because they are not active.</summary>
		public int SkippedInactive { get; init; }

		/// <summary>Members not enabled because the seats ran out (enabled in group order until the cap).</summary>
		public int RefusedForSeats { get; init; }

		public int PacksNeeded { get; init; }
		public LmsSeatSummary Summary { get; init; }
	}

	/// <summary>One paused due date the requirement engine hands to the reactivation re-base (lapse rule 12).</summary>
	public sealed class LmsDueItem
	{
		/// <summary>The caller's key (an assignment or requirement id), returned unchanged.</summary>
		public string Key { get; init; }
		public DateTime DueUtc { get; init; }

		/// <summary>When the item was assigned, UTC: lapse time before it does not move it.</summary>
		public DateTime? AssignedOnUtc { get; init; }
	}

	/// <summary>A re-based due date: the shifted due, and the moment before which it can be neither overdue nor enforced.</summary>
	public sealed class LmsRebasedDue
	{
		public string Key { get; init; }
		public DateTime OriginalDueUtc { get; init; }
		public DateTime DueUtc { get; init; }

		/// <summary>
		/// Nothing becomes overdue or enforceable before this moment (the last reactivation + LmsAddonConfig.ReactivationGraceDays);
		/// null when the item was assigned after the last reactivation, or the department never lapsed.
		/// </summary>
		public DateTime? HoldUntilUtc { get; init; }

		/// <summary>The lapse time the due date was shifted by.</summary>
		public TimeSpan Shift => DueUtc - OriginalDueUtc;
	}

	#region Billing API contract (int-CommonApis LmsBilling, owed)

	/// <summary>
	/// The Billing API's LMS status (GET /api/LmsBilling/Status?departmentId=). A pack add-on: the quantity is packs; adding packs
	/// is charged at once, prorated; removing packs is scheduled for the next billing date.
	/// </summary>
	public sealed class LmsBillingStatus
	{
		public string Provider { get; set; }
		public string Currency { get; set; }
		public bool CheckoutAvailable { get; set; }
		public bool Active { get; set; }
		public bool CanCancel { get; set; }
		public bool Cancelled { get; set; }
		public DateTime? PeriodStartUtc { get; set; }
		public DateTime? PaidThroughUtc { get; set; }

		/// <summary>The department's base plan is an active paid plan; the LMS is sold only with one (§25.4a item 9).</summary>
		public bool HasEntityPlan { get; set; }

		/// <summary>Packs in force now.</summary>
		public int Packs { get; set; }

		/// <summary>Packs from the next billing date when a decrease is scheduled; null when nothing is scheduled.</summary>
		public int? ScheduledPacks { get; set; }

		/// <summary>The current monthly total the provider bills (graduated).</summary>
		public decimal MonthlyAmount { get; set; }
	}

	/// <summary>A started LMS checkout (POST /api/LmsBilling/Checkout with DepartmentId and Packs): a Stripe Checkout URL or a Paddle transaction id.</summary>
	public sealed class LmsBillingCheckout
	{
		public string Provider { get; set; }
		public string Url { get; set; }
		public string TransactionId { get; set; }
	}

	/// <summary>
	/// The Billing API's answer to a pack change (POST /api/LmsBilling/ChangePacks with DepartmentId, Packs and ExpectedPacks): an
	/// increase is applied and invoiced at once with proration; a decrease is scheduled for the next billing date.
	/// </summary>
	public sealed class LmsPackChangeResult
	{
		public bool Succeeded { get; set; }
		public int Packs { get; set; }
		public int? ScheduledPacks { get; set; }
		public DateTime? EffectiveOnUtc { get; set; }

		/// <summary>The prorated amount invoiced at once (an increase).</summary>
		public decimal AmountChargedNow { get; set; }

		/// <summary>A machine code when not applied: stale_quantity, below_minimum, above_maximum, no_subscription, provider_error.</summary>
		public string Error { get; set; }
	}

	/// <summary>The price of a pack change before it is made (the billing page's preview). Generic to every pack add-on (LMS today, Workforce Pro later).</summary>
	public sealed class PackQuote
	{
		public string Currency { get; init; }
		public int CurrentPacks { get; init; }
		public int Packs { get; init; }
		public int Seats { get; init; }
		public decimal CurrentMonthly { get; init; }
		public decimal NextMonthly { get; init; }
		public decimal MonthlyChange => NextMonthly - CurrentMonthly;

		/// <summary>The estimated prorated charge made at once (an increase on a running subscription).</summary>
		public decimal ProratedNow { get; init; }

		/// <summary>When the change takes effect: now for an increase, the next billing date for a decrease.</summary>
		public DateTime? EffectiveOnUtc { get; init; }

		public bool IsIncrease => Packs > CurrentPacks;
		public bool IsDecrease => Packs < CurrentPacks;

		/// <summary>Why the quantity cannot be bought: below_minimum, above_maximum, below_enabled_seats; null when it can.</summary>
		public string Refusal { get; init; }
	}

	#endregion
}
