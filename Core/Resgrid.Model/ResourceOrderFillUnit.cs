using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("ResourceOrderFillUnits")]
	public class ResourceOrderFillUnit : IEntity
	{
		[Key]
		[Required]
		[Dapper.IgnoreInsert]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ResourceOrderFillUnitId { get; set; }

		[Required]
		[ForeignKey("OrderFill"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ResourceOrderFillId { get; set; }

		public virtual ResourceOrderFill OrderFill { get; set; }

		[Required]
		[ForeignKey("Unit"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int UnitId { get; set; }

		public virtual Unit Unit { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ResourceOrderFillUnitId; }
			set { ResourceOrderFillUnitId = (int)value; }
		}
	}
}