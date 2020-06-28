using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
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

		[NotMapped]
		public object Id
		{
			get { return ShiftSignupTradeId; }
			set { ShiftSignupTradeId = (int)value; }
		}

		public bool IsTradeComplete()
		{
			if (UserId == null && TargetShiftSignupId == null)
				return false;

			return true;
		}

		public ShiftSignupTradeStates GetState(string userId)
		{
			var userSignup = Users.FirstOrDefault(x => x.UserId == userId);

			if (userSignup != null && userSignup.Declined)
				return ShiftSignupTradeStates.Declined;

			if (!String.IsNullOrWhiteSpace(UserId) && UserId == userId)
				return ShiftSignupTradeStates.Accepted;

			if (!String.IsNullOrWhiteSpace(UserId) && UserId != userId)
				return ShiftSignupTradeStates.Filled;

			if (TargetShiftSignup != null && TargetShiftSignup.UserId == userId)
				return ShiftSignupTradeStates.Accepted;

			if (TargetShiftSignup != null && TargetShiftSignup.UserId != userId)
				return ShiftSignupTradeStates.Filled;

			if (userSignup != null && userSignup.Offered)
				return ShiftSignupTradeStates.Proposed;
			
			return ShiftSignupTradeStates.Open;
		}
	}

	public class ShiftSignupTrade_Mapping : EntityTypeConfiguration<ShiftSignupTrade>
	{
		public ShiftSignupTrade_Mapping()
		{
			this.HasOptional(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}
