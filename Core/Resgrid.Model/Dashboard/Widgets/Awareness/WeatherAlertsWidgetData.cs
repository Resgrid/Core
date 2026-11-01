using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Awareness
{
	/// <summary>weather.alerts (plan §7.4): the department's active NWS / Environment Canada / MeteoAlarm alerts by severity.</summary>
	public sealed class WeatherAlertsWidgetData
	{
		/// <summary>Every active alert, before the severity filter and the row cap.</summary>
		public int TotalCount { get; set; }

		/// <summary>Counts per severity, most severe first (always five entries: extreme, severe, moderate, minor, unknown).</summary>
		public List<WeatherAlertSeverityCount> Severities { get; set; } = new List<WeatherAlertSeverityCount>();

		public List<WeatherAlertItem> Items { get; set; } = new List<WeatherAlertItem>();
	}

	public sealed class WeatherAlertSeverityCount
	{
		/// <summary>WeatherAlertSeverity value (0 = Extreme … 4 = Unknown).</summary>
		public int Severity { get; set; }

		/// <summary>"extreme", "severe", "moderate", "minor", "unknown" — the client's label key.</summary>
		public string SeverityKey { get; set; }

		public string Color { get; set; }

		public int Count { get; set; }
	}

	public sealed class WeatherAlertItem
	{
		public Guid WeatherAlertId { get; set; }

		public string Event { get; set; }

		public string Headline { get; set; }

		public int Severity { get; set; }

		public string SeverityKey { get; set; }

		/// <summary>Badge color for the severity (the Weather Alerts page's label classes).</summary>
		public string Color { get; set; }

		public int Urgency { get; set; }

		public int Certainty { get; set; }

		public string AreaDescription { get; set; }

		public string Sender { get; set; }

		public DateTime EffectiveUtc { get; set; }

		public DateTime? OnsetUtc { get; set; }

		public DateTime? ExpiresUtc { get; set; }

		public string Url { get; set; }
	}
}
