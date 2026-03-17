using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class IndoorMap : IEntity
	{
		public string IndoorMapId { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public decimal CenterLatitude { get; set; }

		public decimal CenterLongitude { get; set; }

		public decimal BoundsNELat { get; set; }

		public decimal BoundsNELon { get; set; }

		public decimal BoundsSWLat { get; set; }

		public decimal BoundsSWLon { get; set; }

		public string DefaultFloorId { get; set; }

		public bool IsDeleted { get; set; }

		public string AddedById { get; set; }

		public DateTime AddedOn { get; set; }

		public string UpdatedById { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int MapType { get; set; }

		public string BoundsGeoJson { get; set; }

		public byte[] ThumbnailData { get; set; }

		public string ThumbnailContentType { get; set; }

		[NotMapped]
		public string TableName => "IndoorMaps";

		[NotMapped]
		public string IdName => "IndoorMapId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IndoorMapId; }
			set { IndoorMapId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
