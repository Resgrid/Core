using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
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
		[JsonIgnore]
		public object IdValue
		{
			get { return InventoryId; }
			set { InventoryId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Inventories";

		[NotMapped]
		public string IdName => "InventoryId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Group", "AddedBy", "Unit", "Type" };
	}
}
