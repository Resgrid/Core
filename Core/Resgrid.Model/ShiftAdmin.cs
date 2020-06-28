using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using Newtonsoft.Json;

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
		public object Id
		{
			get { return ShiftAdminId; }
			set { ShiftAdminId = (int) value; }
		}
	}
}
