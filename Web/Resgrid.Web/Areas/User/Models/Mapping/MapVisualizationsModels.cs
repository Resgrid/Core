using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class MapVisualizationsIndexView
	{
		public string TileUrl { get; set; }

		public string Attribution { get; set; }

		public string InitialId { get; set; }

		public string InitialTemplate { get; set; }

		public int InitialCallId { get; set; }

		/// <summary>The MapVisualizations resx family, for the React element.</summary>
		public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();
	}

	public class MapVisualizationTemplateResult
	{
		public string Key { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string CategoryName { get; set; }
		public bool Recommended { get; set; }
		public bool NeedsCall { get; set; }
		public MapVisualizationDefinition Definition { get; set; }
	}

	public class MapVisualizationSavedResult
	{
		public string MapVisualizationId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsShared { get; set; }
		public bool IsOwner { get; set; }
		public bool CanEdit { get; set; }
		public string CreatedByName { get; set; }
		public DateTime UpdatedOnUtc { get; set; }
		public string TemplateKey { get; set; }
		public MapVisualizationDefinition Definition { get; set; }
	}

	public class MapVisualizationOption
	{
		public string Value { get; set; }
		public string Label { get; set; }
	}

	public class MapVisualizationSourceOptions
	{
		public IReadOnlyList<string> Displays { get; set; }
		public IReadOnlyList<string> ColorBys { get; set; }
		public IReadOnlyList<string> Metrics { get; set; }
	}

	public class MapVisualizationLimitsResult
	{
		public int MaxWindowDays { get; set; } = MapVisualizationLimits.MaxWindowDays;
		public int MaxTrackWindowDays { get; set; } = MapVisualizationLimits.MaxTrackWindowDays;
		public int MaxLayers { get; set; } = MapVisualizationLimits.MaxLayers;
		public int MaxNameLength { get; set; } = MapVisualizationLimits.MaxNameLength;
		public int MaxDescriptionLength { get; set; } = MapVisualizationLimits.MaxDescriptionLength;
	}

	public class MapVisualizationBootstrapResult
	{
		public List<MapVisualizationTemplateResult> Templates { get; set; } = new List<MapVisualizationTemplateResult>();
		public List<MapVisualizationSavedResult> Saved { get; set; } = new List<MapVisualizationSavedResult>();
		public bool CanShare { get; set; }
		public Dictionary<string, MapVisualizationSourceOptions> SourceOptions { get; set; } = new Dictionary<string, MapVisualizationSourceOptions>();
		public List<MapVisualizationOption> CallTypes { get; set; } = new List<MapVisualizationOption>();
		public List<MapVisualizationOption> Priorities { get; set; } = new List<MapVisualizationOption>();
		public List<MapVisualizationOption> Outcomes { get; set; } = new List<MapVisualizationOption>();
		public List<MapVisualizationOption> Groups { get; set; } = new List<MapVisualizationOption>();
		public List<MapVisualizationOption> Units { get; set; } = new List<MapVisualizationOption>();
		public MapVisualizationLimitsResult Limits { get; set; } = new MapVisualizationLimitsResult();
	}

	public class MapVisualizationSaveInput
	{
		public string MapVisualizationId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsShared { get; set; }
		public string TemplateKey { get; set; }
		public MapVisualizationDefinition Definition { get; set; }
	}

	public class MapVisualizationSaveResponse
	{
		public MapVisualizationSavedResult Saved { get; set; }

		/// <summary>A <see cref="Resgrid.Model.Services.MapVisualizationSaveErrors"/> name, or null on success.</summary>
		public string Error { get; set; }
	}

	public class MapVisualizationDeleteInput
	{
		public string MapVisualizationId { get; set; }
	}

	public class MapVisualizationCallDetail
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public string Priority { get; set; }
		public string State { get; set; }
		public string Address { get; set; }
		public string LoggedOn { get; set; }
		public string Url { get; set; }
	}

	public class MapVisualizationCallHit
	{
		public int CallId { get; set; }
		public string Label { get; set; }
	}
}
