using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Event layers are shown on every map by default while their event runs, and follow "on by default" before and after.
	/// Every client (web map, Omni, the older apps) reads the API's IsOnByDefault as is, so the rule lives on the server.
	/// </summary>
	[TestFixture]
	public class MapLayerEventsTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

		private static MapLayer Layer(DateTime? starts, DateTime? ends, bool onByDefault = false) => new MapLayer
		{
			Name = "City Marathon",
			IsOnByDefault = onByDefault,
			EventStartsOn = starts,
			EventEndsOn = ends,
			Data = new MapLayerData { Features = new System.Collections.Generic.List<MapLayerDataFeature>() }
		};

		[Test]
		public void A_layer_without_dates_is_not_an_event_and_follows_its_setting()
		{
			MapLayerEvents.GetPhase(Layer(null, null), Now).Should().Be(MapLayerEventPhases.None);
			MapLayerEvents.IsShownByDefault(Layer(null, null), Now).Should().BeFalse();
			MapLayerEvents.IsShownByDefault(Layer(null, null, true), Now).Should().BeTrue();
		}

		[Test]
		public void The_layer_is_shown_while_the_event_runs_and_follows_its_setting_otherwise()
		{
			var upcoming = Layer(Now.AddHours(2), Now.AddHours(8));
			var active = Layer(Now.AddHours(-2), Now.AddHours(4));
			var ended = Layer(Now.AddDays(-2), Now.AddDays(-1));

			MapLayerEvents.GetPhase(upcoming, Now).Should().Be(MapLayerEventPhases.Upcoming);
			MapLayerEvents.GetPhase(active, Now).Should().Be(MapLayerEventPhases.Active);
			MapLayerEvents.GetPhase(ended, Now).Should().Be(MapLayerEventPhases.Ended);

			MapLayerEvents.IsShownByDefault(upcoming, Now).Should().BeFalse();
			MapLayerEvents.IsShownByDefault(active, Now).Should().BeTrue();
			MapLayerEvents.IsShownByDefault(ended, Now).Should().BeFalse();
			MapLayerEvents.IsShownByDefault(Layer(Now.AddDays(-2), Now.AddDays(-1), true), Now).Should().BeTrue();
		}

		[Test]
		public void An_open_ended_event_runs_from_its_start()
		{
			MapLayerEvents.GetPhase(Layer(Now.AddHours(-1), null), Now).Should().Be(MapLayerEventPhases.Active);
			MapLayerEvents.GetPhase(Layer(null, Now.AddHours(1)), Now).Should().Be(MapLayerEventPhases.Active);
		}

		[Test]
		public void The_api_reports_an_active_event_layer_as_on_by_default_with_its_details()
		{
			var layer = Layer(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
			layer.Organizer = "Run Club";
			layer.ContactPhone = "+32 470 00 00 00";
			layer.SourceFileName = "marathon.kml";

			var result = MappingController.ConvertMapLayerData(layer);

			result.IsOnByDefault.Should().BeTrue();
			result.EventPhase.Should().Be("active");
			result.Organizer.Should().Be("Run Club");
			result.ContactPhone.Should().Be("+32 470 00 00 00");
			result.SourceFileName.Should().Be("marathon.kml");
			result.EventStartsOnUtc.Should().EndWith("Z");
		}
	}
}
