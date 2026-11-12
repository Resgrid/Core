using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("Units")]
	[ProtoContract]
	public class Unit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int UnitId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(3)]
		public string Name { get; set; }

		[ProtoMember(4)]
		public string Type { get; set; }

		[ForeignKey("StationGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(5)]
		public int? StationGroupId { get; set; }

		[ProtoMember(6)]
		public string VIN { get; set; }

		[ProtoMember(7)]
		public string PlateNumber { get; set; }

		[ProtoMember(8)]
		public bool? FourWheel { get; set; }

		[ProtoMember(9)]
		public bool? SpecialPermit { get; set; }

		/// <summary>
		/// Units are never hard deleted: calls, states, logs, reports and records keep pointing at them. A deleted unit
		/// is left out of every current list (pickers, dispatch, boards, apps, limits, name uniqueness) but still
		/// resolves by id, and through the "including deleted" lists, for point-in-time data.
		/// </summary>
		[ProtoMember(10)]
		public bool IsDeleted { get; set; }

		[ProtoMember(11)]
		public DateTime? DeletedOn { get; set; }

		[ProtoMember(12)]
		public string DeletedByUserId { get; set; }

		/// <summary>Vehicle size overrides, metric, all optional; null falls back to the unit type's value (M0308, <see cref="VehicleProfile"/>).</summary>
		[ProtoMember(13)]
		public double? VehicleHeightMeters { get; set; }

		[ProtoMember(14)]
		public double? VehicleWidthMeters { get; set; }

		/// <summary>Overall length including any towed trailer.</summary>
		[ProtoMember(15)]
		public double? VehicleLengthMeters { get; set; }

		[ProtoMember(16)]
		public double? VehicleGrossWeightKg { get; set; }

		/// <summary>Load on the heaviest axle.</summary>
		[ProtoMember(17)]
		public double? VehicleAxleLoadKg { get; set; }

		[ProtoMember(18)]
		public int? VehicleAxleCount { get; set; }

		/// <summary>Carries hazardous materials.</summary>
		[ProtoMember(19)]
		public bool? VehicleHazmat { get; set; }

		/// <summary>Travel mode override, a <see cref="UnitTravelModes"/> value; null uses the unit type's (M0314, <see cref="UnitTravelProfile"/>).</summary>
		[ProtoMember(20)]
		public int? TravelMode { get; set; }

		/// <summary>Average travel speed override in km/h; null uses the unit type's speed when the mode matches, else the mode's default.</summary>
		[ProtoMember(21)]
		public double? TravelSpeedKmh { get; set; }

		public virtual DepartmentGroup StationGroup { get; set; }

		public virtual Department Department { get; set; }

		[NotMapped]
		public virtual List<UnitRole> Roles { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitId; }
			set { UnitId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Units";

		[NotMapped]
		public string IdName => "UnitId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "StationGroup", "Department", "Roles" };
	}
}
