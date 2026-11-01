using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Awareness;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class WeatherAlertsWidgetProviderTests
	{
		private static WeatherAlert Alert(int severity, int departmentId = DepartmentId, int hoursAgo = 1) => new WeatherAlert
		{
			WeatherAlertId = Guid.NewGuid(), DepartmentId = departmentId, Severity = severity, Event = "Event " + severity, EffectiveUtc = Now.AddHours(-hoursAgo)
		};

		private static WeatherAlertsWidgetProvider Provider(params WeatherAlert[] alerts)
		{
			var service = new Mock<IWeatherAlertService>(MockBehavior.Strict);
			service.Setup(x => x.GetActiveAlertsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(alerts.ToList());
			return new WeatherAlertsWidgetProvider(service.Object);
		}

		[Test]
		public async Task Counts_by_severity_and_lists_the_most_severe_first()
		{
			var provider = Provider(Alert(2), Alert(0), Alert(3), Alert(9), Alert(0, OtherDepartmentId));

			var data = (WeatherAlertsWidgetData)await provider.GetDataAsync(For("weather.alerts"), CancellationToken.None);

			data.TotalCount.Should().Be(4, "another department's alert is never counted");
			data.Severities.Select(s => s.SeverityKey).Should().Equal("extreme", "severe", "moderate", "minor", "unknown");
			data.Severities.Single(s => s.SeverityKey == "unknown").Count.Should().Be(1, "an out-of-range severity is unknown");
			data.Items.Select(i => i.Severity).Should().Equal(0, 2, 3, 4);
			data.Items[0].Color.Should().Be("#ED5565");
			data.Items[0].Url.Should().StartWith("/User/WeatherAlerts/Details/");
		}

		[Test]
		public async Task The_severity_setting_filters_the_list_but_not_the_counts()
		{
			var provider = Provider(Alert(0), Alert(1), Alert(2), Alert(3));

			var data = (WeatherAlertsWidgetData)await provider.GetDataAsync(
				For("weather.alerts", CommsPlanningWidgetContexts.Settings(("minSeverity", "severe"), ("maxItems", 1))), CancellationToken.None);

			data.TotalCount.Should().Be(4);
			data.Items.Should().ContainSingle().Which.Severity.Should().Be(0);
		}
	}
}
