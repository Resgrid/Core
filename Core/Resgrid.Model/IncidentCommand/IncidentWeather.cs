using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Commander-ready current conditions, hourly forecast, and map overlays for an incident location.</summary>
	public class IncidentWeather
	{
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string Source { get; set; }
		public string Attribution { get; set; }
		public DateTime GeneratedAtUtc { get; set; }
		public DateTime? UpdatedAtUtc { get; set; }
		public DateTime ExpiresAtUtc { get; set; }
		public IncidentWeatherObservation Current { get; set; }
		public List<IncidentWeatherForecastPeriod> HourlyForecast { get; set; } = new List<IncidentWeatherForecastPeriod>();
		public List<IncidentWeatherOverlay> Overlays { get; set; } = new List<IncidentWeatherOverlay>();
	}

	public class IncidentWeatherObservation
	{
		public string StationId { get; set; }
		public string Description { get; set; }
		public DateTime? ObservedAtUtc { get; set; }
		public decimal? TemperatureCelsius { get; set; }
		public decimal? RelativeHumidityPercent { get; set; }
		public decimal? WindSpeedKph { get; set; }
		public decimal? WindGustKph { get; set; }
		public decimal? WindDirectionDegrees { get; set; }
		public decimal? BarometricPressureHpa { get; set; }
		public decimal? VisibilityMeters { get; set; }
	}

	public class IncidentWeatherForecastPeriod
	{
		public int Number { get; set; }
		public string Name { get; set; }
		public DateTime StartsAtUtc { get; set; }
		public DateTime EndsAtUtc { get; set; }
		public bool IsDaytime { get; set; }
		public decimal? TemperatureCelsius { get; set; }
		public int? PrecipitationProbabilityPercent { get; set; }
		public string WindSpeed { get; set; }
		public string WindDirection { get; set; }
		public string ShortForecast { get; set; }
		public string DetailedForecast { get; set; }
		public string IconUrl { get; set; }
	}

	/// <summary>Map-client overlay manifest. ArcGIS export templates use an EPSG:3857 bounding box.</summary>
	public class IncidentWeatherOverlay
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string OverlayType { get; set; }
		public string ServiceUrl { get; set; }
		public string ExportUrlTemplate { get; set; }
		public string LayerIds { get; set; }
		public decimal DefaultOpacity { get; set; }
		public int RefreshSeconds { get; set; }
		public string Attribution { get; set; }
	}
}
