using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A named tactical map for an incident — its own saved framing (center + zoom) and markup
	/// (annotations reference it via <see cref="IncidentMapAnnotation.IncidentMapId"/>), with an optional
	/// expiry. Lanes can attach one via <see cref="CommandStructureNode.LinkedMapId"/>. The incident's
	/// MAIN map lives on <see cref="IncidentCommand"/> itself (MapCenter*/MapZoomLevel, null map id).
	/// </summary>
	public class IncidentMap : IEntity, IChangeTracked
	{
		public string IncidentMapId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public string CenterLatitude { get; set; }

		public string CenterLongitude { get; set; }

		/// <summary>Zoom level (0-22); null until the framing has been pinned.</summary>
		public string ZoomLevel { get; set; }

		/// <summary>Optional expiry after which the map is stale (kept, but flagged in UIs).</summary>
		public DateTime? ExpiresOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		/// <summary>Soft-delete tombstone so removals propagate to offline clients.</summary>
		public DateTime? DeletedOn { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentMaps";

		[NotMapped]
		public string IdName => "IncidentMapId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentMapId; }
			set { IncidentMapId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
