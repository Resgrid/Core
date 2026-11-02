using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UnitTypes")]
	public class UnitType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Type { get; set; }

		public int? CustomStatesId { get; set; }

		public int? MapIconType { get; set; }

		/// <summary>Key of the department's uploaded <see cref="TypeIcon"/>; null when none is uploaded (M0285).</summary>
		[MaxLength(64)]
		public string IconKey { get; set; }

		/// <summary>What kind of unit the type is, a <see cref="UnitTypeClasses"/> value; -1 (None) until the department sets it.</summary>
		public int BaseType { get; set; } = (int)UnitTypeClasses.None;

		/// <summary>Vehicle size defaults for units of this type, metric, all optional; units may override each one (M0308, <see cref="VehicleProfile"/>).</summary>
		public double? VehicleHeightMeters { get; set; }

		public double? VehicleWidthMeters { get; set; }

		/// <summary>Overall length including any towed trailer.</summary>
		public double? VehicleLengthMeters { get; set; }

		public double? VehicleGrossWeightKg { get; set; }

		/// <summary>Load on the heaviest axle.</summary>
		public double? VehicleAxleLoadKg { get; set; }

		public int? VehicleAxleCount { get; set; }

		/// <summary>Carries hazardous materials.</summary>
		public bool? VehicleHazmat { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitTypeId; }
			set { UnitTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitTypes";

		[NotMapped]
		public string IdName => "UnitTypeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
