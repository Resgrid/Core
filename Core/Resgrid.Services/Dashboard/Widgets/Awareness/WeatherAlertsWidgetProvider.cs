using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Awareness
{
	/// <summary>weather.alerts (plan §7.4): the v4 GetActiveAlerts list (the Weather Alerts page's source), counted by severity.</summary>
	[DashboardWidget("weather.alerts")]
	public class WeatherAlertsWidgetProvider : IDashboardWidgetDataProvider
	{
		/// <summary>Severity keys and badge colors in WeatherAlertSeverity order (the page's danger/danger/warning/info/default labels).</summary>
		private static readonly (string Key, string Color)[] SeverityStyles =
		{
			("extreme", "#ED5565"), ("severe", "#ED5565"), ("moderate", "#f8ac59"), ("minor", "#23c6c8"), ("unknown", "#D1DADD")
		};

		private readonly IWeatherAlertService _weatherAlertService;

		public WeatherAlertsWidgetProvider(IWeatherAlertService weatherAlertService)
		{
			_weatherAlertService = weatherAlertService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var alerts = (await _weatherAlertService.GetActiveAlertsByDepartmentIdAsync(context.DepartmentId) ?? new List<WeatherAlert>())
				.Where(a => a != null && a.DepartmentId == context.DepartmentId)
				.ToList();

			var data = new WeatherAlertsWidgetData { TotalCount = alerts.Count };
			for (var severity = 0; severity < SeverityStyles.Length; severity++)
			{
				data.Severities.Add(new WeatherAlertSeverityCount
				{
					Severity = severity,
					SeverityKey = SeverityStyles[severity].Key,
					Color = SeverityStyles[severity].Color,
					Count = alerts.Count(a => NormalizeSeverity(a.Severity) == severity)
				});
			}

			var maximum = MaximumSeverity(context.GetString("minSeverity", "all"));
			var maxItems = context.GetInt("maxItems", 5);

			data.Items = alerts
				.Where(a => NormalizeSeverity(a.Severity) <= maximum)
				.OrderBy(a => NormalizeSeverity(a.Severity))
				.ThenByDescending(a => a.EffectiveUtc)
				.Take(maxItems)
				.Select(a =>
				{
					var severity = NormalizeSeverity(a.Severity);
					return new WeatherAlertItem
					{
						WeatherAlertId = a.WeatherAlertId,
						Event = a.Event,
						Headline = a.Headline,
						Severity = severity,
						SeverityKey = SeverityStyles[severity].Key,
						Color = SeverityStyles[severity].Color,
						Urgency = a.Urgency,
						Certainty = a.Certainty,
						AreaDescription = a.AreaDescription,
						Sender = a.Sender,
						EffectiveUtc = a.EffectiveUtc,
						OnsetUtc = a.OnsetUtc,
						ExpiresUtc = a.ExpiresUtc,
						Url = "/User/WeatherAlerts/Details/" + a.WeatherAlertId.ToString("D")
					};
				})
				.ToList();

			return data;
		}

		/// <summary>"extreme" keeps Extreme only, "severe" Extreme + Severe, "moderate" down to Moderate, anything else everything.</summary>
		internal static int MaximumSeverity(string setting)
		{
			switch (setting)
			{
				case "extreme": return (int)WeatherAlertSeverity.Extreme;
				case "severe": return (int)WeatherAlertSeverity.Severe;
				case "moderate": return (int)WeatherAlertSeverity.Moderate;
				default: return (int)WeatherAlertSeverity.Unknown;
			}
		}

		internal static int NormalizeSeverity(int severity) =>
			severity >= (int)WeatherAlertSeverity.Extreme && severity <= (int)WeatherAlertSeverity.Unknown ? severity : (int)WeatherAlertSeverity.Unknown;
	}
}
