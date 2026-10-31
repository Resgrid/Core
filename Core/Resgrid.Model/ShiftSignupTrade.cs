using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("ShiftSignupTrades")]
	public class ShiftSignupTrade : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftSignupTradeId { get; set; }

		/// <summary>
		/// The signup (shift day slot) being traded away. Null for a static shift (workshift) day trade, which has no signup:
		/// see <see cref="WorkshiftDayId"/>.
		/// </summary>
		[ForeignKey("SourceShiftSignup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? SourceShiftSignupId { get; set; }

		[JsonIgnore]
		public virtual ShiftSignup SourceShiftSignup { get; set; }

		[ForeignKey("TargetShiftSignup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? TargetShiftSignupId { get; set; }

		[JsonIgnore]
		public virtual ShiftSignup TargetShiftSignup { get; set; }

		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		public virtual ICollection<ShiftSignupTradeUser> Users { get; set; }

		public bool Denied { get; set; }

		public string Note { get; set; }

		/// <summary>
		/// The requester picked an offer (UserId or TargetShiftSignupId is set) on a shift that requires approval, and
		/// a supervisor has not reviewed it yet. The swap does not change the roster until it is approved.
		/// </summary>
		public bool ApprovalPending { get; set; }

		public string ReviewedByUserId { get; set; }

		public DateTime? ReviewedOn { get; set; }

		public string ReviewNote { get; set; }

		/// <summary>
		/// Set on trades created since open trade requests were added (null on older rows, which have a source signup to
		/// read it from).
		/// </summary>
		public int? DepartmentId { get; set; }

		/// <summary>Who asked to trade out. Older shift trades leave it null; use <see cref="GetSourceUserId"/>.</summary>
		public string SourceUserId { get; set; }

		/// <summary>
		/// Department-local date of the day being traded, so trades without a source signup (static shift days) can be
		/// found by date.
		/// </summary>
		public DateTime? SourceDay { get; set; }

		/// <summary>
		/// The request is open to everyone eligible to take the slot rather than only the invited <see cref="Users"/>: any
		/// eligible member may apply, and applying adds them to <see cref="Users"/>.
		/// </summary>
		public bool IsOpen { get; set; }

		/// <summary>When the trade was requested (UTC).</summary>
		public DateTime? RequestedOn { get; set; }

		/// <summary>Static shift (workshift) day trades: the workshift.</summary>
		public string WorkshiftId { get; set; }

		/// <summary>Static shift (workshift) day trades: the day being traded.</summary>
		public string WorkshiftDayId { get; set; }

		/// <summary>Static shift (workshift) day trades: the unit the requester crews on that day.</summary>
		public int? UnitId { get; set; }

		/// <summary>Static shift (workshift) day trades: the unit's station, which decides the group admins who may approve.</summary>
		public int? DepartmentGroupId { get; set; }

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return ShiftSignupTradeId; }
			set { ShiftSignupTradeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftSignupTrades";

		[NotMapped]
		public string IdName => "ShiftSignupTradeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "SourceShiftSignup", "TargetShiftSignup", "User", "Users" };

		/// <summary>
		/// An offer has been picked (someone takes the source slot outright, or a swap-back signup was chosen).
		/// This is true while the pick is still waiting on a supervisor; use <see cref="IsTradeComplete"/> to know
		/// whether the roster has actually changed.
		/// </summary>
		public bool HasSelection()
		{
			return !String.IsNullOrWhiteSpace(UserId) || TargetShiftSignupId.HasValue;
		}

		/// <summary>
		/// The trade has taken effect: an offer was picked, and it is neither waiting for nor denied by a supervisor.
		/// </summary>
		public bool IsTradeComplete()
		{
			return HasSelection() && !ApprovalPending && !Denied;
		}

		/// <summary>
		/// Still taking offers: nobody has been picked yet, or a supervisor denied the pick so another one can be made.
		/// </summary>
		public bool IsOpenForOffers()
		{
			return !HasSelection() || Denied;
		}

		/// <summary>A trade of a static shift (workshift) day rather than a shift signup.</summary>
		public bool IsWorkshiftTrade()
		{
			return !String.IsNullOrWhiteSpace(WorkshiftDayId);
		}

		/// <summary>Who is trading out: the source signup's person, or the stored requester for static shift days.</summary>
		public string GetSourceUserId()
		{
			return !String.IsNullOrWhiteSpace(SourceShiftSignup?.UserId) ? SourceShiftSignup.UserId : SourceUserId;
		}

		/// <summary>The group (team or station) whose slot is being traded.</summary>
		public int? GetDepartmentGroupId()
		{
			return SourceShiftSignup != null ? SourceShiftSignup.DepartmentGroupId : DepartmentGroupId;
		}

		/// <summary>The department the trade belongs to, from the source signup's shift when it is loaded.</summary>
		public int? GetDepartmentId()
		{
			return SourceShiftSignup?.Shift?.DepartmentId ?? DepartmentId;
		}

		/// <summary>The person picked to take the slot (outright, or the owner of the chosen swap-back day).</summary>
		public string GetPickedUserId()
		{
			return !String.IsNullOrWhiteSpace(UserId) ? UserId : TargetShiftSignup?.UserId;
		}

		public ShiftSignupTradeStates GetState(string userId)
		{
			// User ids are GUID strings that arrive in either case depending on where they were read from.
			var userSignup = Users?.FirstOrDefault(x => SameUser(x.UserId, userId));

			if (userSignup != null && userSignup.Declined)
				return ShiftSignupTradeStates.Declined;

			if (Denied)
				return ShiftSignupTradeStates.Denied;

			var pickedUserId = !String.IsNullOrWhiteSpace(UserId) ? UserId : TargetShiftSignup?.UserId;

			if (ApprovalPending && SameUser(pickedUserId, userId))
				return ShiftSignupTradeStates.PendingApproval;

			if (!String.IsNullOrWhiteSpace(UserId) && SameUser(UserId, userId))
				return ShiftSignupTradeStates.Accepted;

			if (!String.IsNullOrWhiteSpace(UserId) && !SameUser(UserId, userId))
				return ShiftSignupTradeStates.Filled;

			if (TargetShiftSignup != null && SameUser(TargetShiftSignup.UserId, userId))
				return ShiftSignupTradeStates.Accepted;

			if (TargetShiftSignup != null && !SameUser(TargetShiftSignup.UserId, userId))
				return ShiftSignupTradeStates.Filled;

			if (userSignup != null && userSignup.Offered)
				return ShiftSignupTradeStates.Proposed;
			
			return ShiftSignupTradeStates.Open;
		}

		private static bool SameUser(string a, string b)
		{
			return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}
	}
}
