using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Pois")]
	public class Poi : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PoiId { get; set; }

		[Required]
		public int PoiTypeId { get; set; }

		[ForeignKey("PoiTypeId")]
		public virtual PoiType Type { get; set; }

		public double Longitude { get; set; }

		public double Latitude { get; set; }

		public string Note { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return PoiId; }
			set { PoiId = (int)value; }
		}

		
		[NotMapped]
		public string TableName => "Pois";

		[NotMapped]
		public string IdName => "PoiId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Type" };
	}
}
