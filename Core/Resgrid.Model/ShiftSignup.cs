using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("ShiftSignups")]
	public class ShiftSignup : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftSignupId { get; set; }

		[Required]
		[ForeignKey("Shift"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftId { get; set; }

		[JsonIgnore]
		public virtual Shift Shift { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		[JsonIgnore]
		public virtual IdentityUser User { get; set; }

		public DateTime SignupTimestamp { get; set; }

		public DateTime ShiftDay { get; set; }

		public bool Denied { get; set; }

		[ForeignKey("Group"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? DepartmentGroupId { get; set; }

		public virtual DepartmentGroup Group { get; set; }

		[NotMapped]
		public virtual ShiftSignupTrade Trade { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftSignupId; }
			set { ShiftSignupId = (int)value; }
		}

		public ShiftTradeTypes GetTradeType()
		{
			if (Trade == null)
				return ShiftTradeTypes.None;

			if (Trade.SourceShiftSignupId == ShiftSignupId)
				return ShiftTradeTypes.Source;
			
			return ShiftTradeTypes.Target;
		}
	}

	public class ShiftSignup_Mapping : EntityTypeConfiguration<ShiftSignup>
	{
		public ShiftSignup_Mapping()
		{
			//this.HasOptional(t => t.Trade).WithMany().Map(x => x.MapKey("SourceShiftSignupId"));
		}
	}
}