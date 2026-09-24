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

		[Required]
		[ForeignKey("SourceShiftSignup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int SourceShiftSignupId { get; set; }

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
