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

		public bool IsOnByDefault { get; set; }

		public string AddedById { get; set; }

		public DateTime AddedOn { get; set; }

		public string UpdatedById { get; set; }

		public DateTime UpdatedOn { get; set; }
	}

	public class GetMapLayersDataInfo
	{
		public string Type { get; set; }

		public FeatureCollection Features { get; set; }
	}
}
