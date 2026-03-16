using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class IndoorMapZone : IEntity
	{
		public string IndoorMapZoneId { get; set; }

		public string IndoorMapFloorId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public int ZoneType { get; set; }

		public string PixelGeometry { get; set; }

		public string GeoGeometry { get; set; }

		public double CenterPixelX { get; set; }

		public double CenterPixelY { get; set; }

		public decimal CenterLatitude { get; set; }

		public decimal CenterLongitude { get; set; }

		public string Color { get; set; }

		public string Metadata { get; set; }

		public bool IsSearchable { get; set; }

		public bool IsDispatchable { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "IndoorMapZones";

		[NotMapped]
		public string IdName => "IndoorMapZoneId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IndoorMapZoneId; }
			set { IndoorMapZoneId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
