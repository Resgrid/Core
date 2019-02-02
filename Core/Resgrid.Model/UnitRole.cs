using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UnitRoles")]
	public class UnitRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitRoleId { get; set; }

		[Required]
		public int UnitId { get; set; }

		[ForeignKey("UnitId")]
		public Unit Unit { get; set; }

		[Required]
		[MaxLength(250)]
		public string Name { get; set; }

		[NotMapped]
		public object Id
		{
			get { return UnitRoleId; }
			set { UnitRoleId = (int)value; }
		}
	}
}