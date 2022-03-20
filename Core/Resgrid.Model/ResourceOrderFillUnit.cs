using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("ResourceOrderFillUnits")]
	public class ResourceOrderFillUnit : IEntity
	{
		[Key]
		[Required]
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
		[JsonIgnore]
		public object IdValue
		{
			get { return ResourceOrderFillUnitId; }
			set { ResourceOrderFillUnitId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ResourceOrderFillUnits";

		[NotMapped]
		public string IdName => "ResourceOrderFillUnitId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "OrderFill", "Unit" };
	}
}
