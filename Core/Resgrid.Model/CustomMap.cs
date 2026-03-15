using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("CustomMaps")]
	public class CustomMap : IEntity
	{
		[Key]
		[Required]
		[MaxLength(36)]
		public string CustomMapId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(500)]
		public string Name { get; set; }

		[MaxLength(2000)]
		public string Description { get; set; }

		/// <summary>Map type: Indoor, Satellite, Schematic, Event</summary>
		public int Type { get; set; }

		public double BoundsTopLeftLat { get; set; }
		public double BoundsTopLeftLng { get; set; }
		public double BoundsBottomRightLat { get; set; }
		public double BoundsBottomRightLng { get; set; }

		public int DefaultZoom { get; set; }
		public int MinZoom { get; set; }
		public int MaxZoom { get; set; }

		public bool IsActive { get; set; }

		public DateTime? EventStartsOn { get; set; }
		public DateTime? EventEndsOn { get; set; }

		[MaxLength(128)]
		public string AddedById { get; set; }

		public DateTime AddedOn { get; set; }

		[MaxLength(128)]
		public string UpdatedById { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public virtual ICollection<CustomMapFloor> Floors { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomMapId; }
			set { CustomMapId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "CustomMaps";

		[NotMapped]
		public string IdName => "CustomMapId";

		/// <summary>IdType 1 = application-generated Guid string (not DB identity)</summary>
		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Floors" };
	}
}
