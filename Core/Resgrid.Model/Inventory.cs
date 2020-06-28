using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("Inventories")]
	public class Inventory : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int InventoryId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int GroupId { get; set; }

		public virtual DepartmentGroup Group { get; set; }

		[Required]
		public int TypeId { get; set; }

		public virtual InventoryType Type { get; set; }

		public string Batch { get; set; }

		public string Note { get; set; }

		public string Location { get; set; }

		public double Amount { get; set; }

		public DateTime TimeStamp { get; set; }

		public string AddedByUserId { get; set; }

		[ForeignKey("AddedByUserId")]
		public virtual IdentityUser AddedBy { get; set; }

		public int? UnitId { get; set; }

		[ForeignKey("UnitId")]
		public virtual Unit Unit { get; set; }

		[NotMapped]
		public object Id
		{
			get { return InventoryId; }
			set { InventoryId = (int)value; }
		}
	}

	public class Inventory_Mapping : EntityTypeConfiguration<Inventory>
	{
		public Inventory_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
			this.HasRequired(t => t.Group).WithMany().HasForeignKey(t => t.GroupId).WillCascadeOnDelete(false);
			this.HasRequired(t => t.Type).WithMany().HasForeignKey(t => t.TypeId).WillCascadeOnDelete(false);
		}
	}
}
