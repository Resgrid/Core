using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("POITypes")]
	public class PoiType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PoiTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		public string Image { get; set; }

		public string Color { get; set; }

		public string Marker { get; set; }

		public int Size { get; set; }

		public bool IsDestination { get; set; }

		public virtual ICollection<Poi> Pois { get; set; }

		[NotMapped]
		public int Count { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PoiTypeId; }
			set { PoiTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "POITypes";

		[NotMapped]
		public string IdName => "PoiTypeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Pois", "Count" };
	}
}
