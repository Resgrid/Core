using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("InventoryTypes")]
	public class InventoryType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int InventoryTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(250)]
		public string Type { get; set; }

		public int ExpiresDays { get; set; }

		public string Description { get; set; }

		public string UnitOfMesasure { get; set; }

		public virtual ICollection<Inventory> Inventories { get; set; }

		[NotMapped]
		public object Id
		{
			get { return InventoryTypeId; }
			set { InventoryTypeId = (int)value; }
		}
	}
}