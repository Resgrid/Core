using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/* DEPRICATED! Being replaced by UnitsLocation, will eventually
	 * rename that to UnitLocation once this goes away.
	 */
	[ProtoContract]
	[Table("UnitLocations")]
	public class UnitLocation : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int UnitLocationId { get; set; }

		[ProtoMember(2)]
		public int UnitId { get; set; }

		[ForeignKey("UnitId")]
		public virtual Unit Unit { get; set; }

		[ProtoMember(3)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(4)]
		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[ProtoMember(5)]
		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		[ProtoMember(6)]
		[DecimalPrecision(6, 2)]
		public decimal? Accuracy { get; set; }

		[ProtoMember(7)]
		[DecimalPrecision(7, 2)]
		public decimal? Altitude { get; set; }

		[ProtoMember(8)]
		[DecimalPrecision(6, 2)]
		public decimal? AltitudeAccuracy { get; set; }

		[ProtoMember(9)]
		[DecimalPrecision(5, 2)]
		public decimal? Speed { get; set; }

		[ProtoMember(10)]
		[DecimalPrecision(5, 2)]
		public decimal? Heading { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitLocationId; }
			set { UnitLocationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitLocations";

		[NotMapped]
		public string IdName => "UnitLocationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Unit" };
	}
}
