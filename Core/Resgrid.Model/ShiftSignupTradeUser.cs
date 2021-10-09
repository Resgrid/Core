using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
		[JsonIgnore]public object IdValue
		{
			get { return ShiftSignupTradeUserId; }
			set { ShiftSignupTradeUserId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftSignupTradeUsers";

		[NotMapped]
		public string IdName => "ShiftSignupTradeUserId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ShiftSignupTrade", "User", "Shifts" };
	}
}
