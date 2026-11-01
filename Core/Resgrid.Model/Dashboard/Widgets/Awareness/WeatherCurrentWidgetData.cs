using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// weather.current (plan §7.4): current conditions and a three-day outlook for the department center, fetched
	/// server-side from OpenWeather (the Big Board source) and cached per department for 15 minutes. The API key never
	/// leaves the server.
	/// </summary>
	public sealed class WeatherCurrentWidgetData
	{
		/// <summary>False when the department has no usable center (no coordinates and no geocodable address).</summary>
		public bool Available { get; set; }

		/// <summary>"imperial" (°F, mph) or "metric" (°C, m/s).</summary>
		public string Units { get; set; }

		/// <summary>The place name OpenWeather reports for the coordinates.</summary>
		public string LocationName { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		public WeatherConditions Current { get; set; }

		public List<WeatherForecastDay> Forecast { get; set; } = new List<WeatherForecastDay>();

		/// <summary>When OpenWeather observed the current conditions.</summary>
		public DateTime ObservedUtc { get; set; }

		/// <summary>When the data was fetched (it is cached up to 15 minutes).</summary>
		public DateTime FetchedUtc { get; set; }
	}

	public sealed class WeatherConditions
	{
		public int Temperature { get; set; }

		public int FeelsLike { get; set; }

		/// <summary>OpenWeather's condition group ("Clear", "Clouds", "Rain", "Snow", "Thunderstorm", "Drizzle", "Mist"…); the client maps it to an icon and a localized label.</summary>
		public string Condition { get; set; }

		/// <summary>OpenWeather's English description ("light rain"); a tooltip, not a label.</summary>
		public string Description { get; set; }

		/// <summary>OpenWeather icon code ("10d").</summary>
		public string Icon { get; set; }

		public int Humidity { get; set; }

		public int WindSpeed { get; set; }

		/// <summary>Degrees the wind blows from (meteorological), when reported.</summary>
		public int? WindDirection { get; set; }
	}

	public sealed class WeatherForecastDay
	{
		/// <summary>Department-local calendar date, "yyyy-MM-dd".</summary>
		public string Day { get; set; }

		/// <summary>0 = Sunday.</summary>
		public int DayOfWeek { get; set; }

		public int High { get; set; }

		public int Low { get; set; }

		public string Condition { get; set; }

		public string Icon { get; set; }

		/// <summary>Highest probability of precipitation in the day's forecast slots, 0–100.</summary>
		public int PrecipitationChance { get; set; }
	}
}
