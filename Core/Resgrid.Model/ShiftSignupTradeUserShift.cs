using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("ShiftSignupTradeUserShifts")]
	public class ShiftSignupTradeUserShift : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftSignupTradeUserShiftId { get; set; }

		[Required]
		[ForeignKey("ShiftSignupTradeUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftSignupTradeUserId { get; set; }

		public virtual ShiftSignupTradeUser ShiftSignupTradeUser { get; set; }

		[ForeignKey("ShiftSignup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? ShiftSignupId { get; set; }

		public virtual ShiftSignup ShiftSignup { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ShiftSignupTradeUserShiftId; }
			set { ShiftSignupTradeUserShiftId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftSignupTradeUserShifts";

		[NotMapped]
		public string IdName => "ShiftSignupTradeUserShiftId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ShiftSignupTradeUser", "ShiftSignup" };
	}
}
