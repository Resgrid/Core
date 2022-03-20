using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Model
{
	[Table("ShiftAdmins")]
	public class ShiftAdmin : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftAdminId { get; set; }

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

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return ShiftAdminId; }
			set { ShiftAdminId = (int) value; }
		}

		[NotMapped]
		public string TableName => "ShiftAdmins";

		[NotMapped]
		public string IdName => "ShiftAdminId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift", "User" };
	}
}
