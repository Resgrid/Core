using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Resgrid.Model.Repositories;

namespace Resgrid.Model
{
	[BsonCollection("unitLocations")]
	public class UnitsLocation : NoSqlDocument
	{
		[BsonElement("departmentId")]
		public int DepartmentId { get; set; }

		[Required]
		[BsonElement("unitId")]
		public int UnitId { get; set; }

		[BsonElement("timestamp")]
		public DateTime Timestamp { get; set; }

		[BsonElement("latitude")]
		public decimal Latitude { get; set; }

		[BsonElement("longitude")]
		public decimal Longitude { get; set; }

		[BsonElement("accuracy")]
		public decimal? Accuracy { get; set; }

		[BsonElement("altitude")]
		public decimal? Altitude { get; set; }

		[BsonElement("altitudeAccuracy")]
		public decimal? AltitudeAccuracy { get; set; }

		[BsonElement("speed")]
		public decimal? Speed { get; set; }

		[BsonElement("heading")]
		public decimal? Heading { get; set; }

		[BsonIgnore()]
		public string PgId { get; set; }

		public string GetId()
		{
			if (!String.IsNullOrWhiteSpace(PgId))
				return PgId;

			return Id.ToString();
		}
	}
}
