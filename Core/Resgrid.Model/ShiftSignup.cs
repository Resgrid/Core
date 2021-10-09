using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using Newtonsoft.Json;
using System.Collections.Generic;

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
		[JsonIgnore]public object IdValue
		{
			get { return ShiftSignupId; }
			set { ShiftSignupId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftSignups";

		[NotMapped]
		public string IdName => "ShiftSignupId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift", "User", "Group", "Trade" };

		public ShiftTradeTypes GetTradeType()
		{
			if (Trade == null)
				return ShiftTradeTypes.None;

			if (Trade.SourceShiftSignupId == ShiftSignupId)
				return ShiftTradeTypes.Source;
			
			return ShiftTradeTypes.Target;
		}
	}
}
