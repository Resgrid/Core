using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model
{
	[Table("ResourceOrderItems")]
	public class ResourceOrderItem : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ResourceOrderItemId { get; set; }

		[Required]
		[ForeignKey("Order"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ResourceOrderId { get; set; }

		public virtual ResourceOrder Order { get; set; }

		public string Resource { get; set; }

		public int Min { get; set; }

		public int Max { get; set; }

		public string FinancialCode { get; set; }

		public string SpecialNeeds { get; set; }

		public string Requirements { get; set; }

		public virtual ICollection<ResourceOrderFill> Fills { get; set; }

		public bool IsFilled()
		{
			if (Fills == null || !Fills.Any())
				return false;

			if (Fills.Where(x => x.Accepted).Sum(x => x.Units?.Count) >= Min)
				return true;

			return false;
		}

		public int UnitFillCount()
		{
			if (Fills == null || !Fills.Any())
				return 0;

			var result = Fills.Sum(x => x.Units?.Count);

			if (result.HasValue)
				return result.Value;

			return 0;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResourceOrderItemId; }
			set { ResourceOrderItemId = (int)value; }
		}


		[NotMapped]
		public string TableName => "ResourceOrderItems";

		[NotMapped]
		public string IdName => "ResourceOrderItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Order", "Fills" };
	}
}
