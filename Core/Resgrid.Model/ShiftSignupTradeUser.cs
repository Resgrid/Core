using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("ShiftSignupTradeUsers")]
	public class ShiftSignupTradeUser : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftSignupTradeUserId { get; set; }

		[Required]
		[ForeignKey("ShiftSignupTrade"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftSignupTradeId { get; set; }

		[JsonIgnore]
		public virtual ShiftSignupTrade ShiftSignupTrade { get; set; }

		[Required]
		public string UserId { get; set; }

		[JsonIgnore]
		public virtual IdentityUser User { get; set; }

		public bool Declined { get; set; }

		public bool Offered { get; set; }

		public string Reason { get; set; }

		public virtual ICollection<ShiftSignupTradeUserShift> Shifts { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftSignupTradeUserId; }
			set { ShiftSignupTradeUserId = (int)value; }
		}
	}

	public class ShiftSignupTradeUser_Mapping : EntityTypeConfiguration<ShiftSignupTradeUser>
	{
		public ShiftSignupTradeUser_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}
