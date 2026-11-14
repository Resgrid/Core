using GeoJSON.Net.Feature;
using MongoDB.Bson.Serialization.Attributes;
using Resgrid.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class GetMapLayersResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetMapLayersResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetMapLayersResult()
		{
			Data = new GetMapLayersResultData();
		}
	}

	public class GetMapLayersResultData
	{
		public GetMapLayersResultData()
		{
			Layers = new List<GetMapLayersData>();
		}

		/// <summary>
		/// GeoJson text of the map layers
		/// </summary>
		public string LayerJson { get; set; }

		/// <summary>
		/// Map Layers
		/// </summary>
		public List<GetMapLayersData> Layers { get; set; }
	}

	public class GetMapLayersData
	{
		public string Id { get; set; }
		
		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public int Type { get; set; }

		public string Color { get; set; }

		public GetMapLayersDataInfo Data { get; set; }

		public bool IsSearchable { get; set; }

		/// <summary>
		/// Whether maps start with the layer switched on: the department's setting, or (event layers) the event running now.
		/// </summary>
		public bool IsOnByDefault { get; set; }

		public string AddedById { get; set; }

		public DateTime AddedOn { get; set; }

		public string UpdatedById { get; set; }

		public DateTime UpdatedOn { get; set; }

		/// <summary>What the layer is for. Event layers carry the event's summary here.</summary>
		public string Description { get; set; }

		/// <summary>Event start (UTC, ISO 8601), blank for a layer with no event dates.</summary>
		public string EventStartsOnUtc { get; set; }

		/// <summary>Event end (UTC, ISO 8601), blank when open-ended or not an event layer.</summary>
		public string EventEndsOnUtc { get; set; }

		/// <summary>"none", "upcoming", "active" or "ended", relative to the request time.</summary>
		public string EventPhase { get; set; }

		public string Organizer { get; set; }

		public string ContactName { get; set; }

		public string ContactPhone { get; set; }

		public string ContactEmail { get; set; }

		/// <summary>The file the shapes were imported from (KML, KMZ or GeoJSON), blank for a drawn layer.</summary>
		public string SourceFileName { get; set; }
	}

	public class GetMapLayersDataInfo
	{
		public string Type { get; set; }

		public FeatureCollection Features { get; set; }
	}
}
