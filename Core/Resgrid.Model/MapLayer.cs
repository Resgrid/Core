using System;
using System.ComponentModel.DataAnnotations;
using GeoJSON.Net.Feature;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Model.Repositories;

namespace Resgrid.Model
{
	[BsonCollection("maplayers")]
	[BsonIgnoreExtraElements]
	public class MapLayer: NoSqlDocument
	{
		[BsonElement("departmentId")]
		[JsonProperty(PropertyName = "departmentId")]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(250)]
		[BsonElement("name")]
		[JsonProperty(PropertyName = "name")]
		public string Name { get; set; }

		[BsonElement("type")]
		[JsonProperty(PropertyName = "type")]
		public int Type { get; set; }

		[Required]
		[MaxLength(50)]
		[BsonElement("color")]
		[JsonProperty(PropertyName = "color")]
		public string Color { get; set; }

		[BsonElement("data")]
		[JsonProperty(PropertyName = "data")]
		public MapLayerData Data { get; set; }

		[BsonElement("isSearchable")]
		[JsonProperty(PropertyName = "isSearchable")]
		public bool IsSearchable { get; set; }

		[BsonElement("isOnByDefault")]
		[JsonProperty(PropertyName = "isOnByDefault")]
		public bool IsOnByDefault { get; set; }

		[BsonElement("addedById")]
		[JsonProperty(PropertyName = "addedById")]
		public string AddedById { get; set; }

		[BsonElement("addedOn")]
		[JsonProperty(PropertyName = "addedOn")]
		public DateTime AddedOn { get; set; }

		[BsonElement("isDeleted")]
		[JsonProperty(PropertyName = "isDeleted")]
		public bool IsDeleted { get; set; }

		[BsonElement("updatedById")]
		[JsonProperty(PropertyName = "updatedById")]
		public string UpdatedById { get; set; }

		[BsonElement("updatedOn")]
		[JsonProperty(PropertyName = "updatedOn")]
		public DateTime UpdatedOn { get; set; }

		/// <summary>What the layer is for (an event layer's summary, shown with its details). Optional.</summary>
		[MaxLength(2000)]
		[BsonElement("description")]
		[JsonProperty(PropertyName = "description")]
		public string Description { get; set; }

		/// <summary>
		/// When the event the layer maps starts (UTC). With <see cref="EventEndsOn"/> it makes the layer an event layer: shown on
		/// every map by default while the event runs (<see cref="MapLayerEvents.IsShownByDefault"/>). Optional.
		/// </summary>
		[BsonElement("eventStartsOn")]
		[BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
		[JsonProperty(PropertyName = "eventStartsOn")]
		public DateTime? EventStartsOn { get; set; }

		/// <summary>When the event ends (UTC). Optional.</summary>
		[BsonElement("eventEndsOn")]
		[BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
		[JsonProperty(PropertyName = "eventEndsOn")]
		public DateTime? EventEndsOn { get; set; }

		/// <summary>Who runs the event (organization or owner).</summary>
		[MaxLength(250)]
		[BsonElement("organizer")]
		[JsonProperty(PropertyName = "organizer")]
		public string Organizer { get; set; }

		/// <summary>The event's coordinator or contact person.</summary>
		[MaxLength(250)]
		[BsonElement("contactName")]
		[JsonProperty(PropertyName = "contactName")]
		public string ContactName { get; set; }

		[MaxLength(100)]
		[BsonElement("contactPhone")]
		[JsonProperty(PropertyName = "contactPhone")]
		public string ContactPhone { get; set; }

		[MaxLength(250)]
		[BsonElement("contactEmail")]
		[JsonProperty(PropertyName = "contactEmail")]
		public string ContactEmail { get; set; }

		/// <summary>
		/// The file the layer's shapes were imported from (KML, KMZ or GeoJSON), blank for a drawn layer. An imported layer's
		/// shapes carry their own names, descriptions and colors, so it is changed by replacing the file, not in the drawing
		/// editor (which writes the layer's name and color onto every shape).
		/// </summary>
		[MaxLength(260)]
		[BsonElement("sourceFileName")]
		[JsonProperty(PropertyName = "sourceFileName")]
		public string SourceFileName { get; set; }

		[BsonElement("importedById")]
		[JsonProperty(PropertyName = "importedById")]
		public string ImportedById { get; set; }

		[BsonElement("importedOn")]
		[BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
		[JsonProperty(PropertyName = "importedOn")]
		public DateTime? ImportedOn { get; set; }

		/// <summary>True for a layer whose shapes came from an uploaded file.</summary>
		[BsonIgnore]
		[JsonIgnore]
		public bool IsImported => !String.IsNullOrWhiteSpace(SourceFileName);

		[BsonIgnore()]
		[JsonProperty(PropertyName = "id")]
		public string PgId { get; set; }

		public string GetId()
		{
			if (!String.IsNullOrWhiteSpace(PgId))
				return PgId;

			return Id.ToString();
		}
	}
}
