using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class MapStylePresetsTests
	{
		[TestCase(null, MapStyleTypes.Automatic)]
		[TestCase("", MapStyleTypes.Automatic)]
		[TestCase("abc", MapStyleTypes.Automatic)]
		[TestCase("99", MapStyleTypes.Automatic)]
		[TestCase("-1", MapStyleTypes.Automatic)]
		[TestCase("5", MapStyleTypes.Satellite)]
		[TestCase("8", MapStyleTypes.NavigationNight)]
		public void should_parse_stored_values(string stored, MapStyleTypes expected)
		{
			MapStylePresets.Parse(stored).Should().Be(expected);
		}

		[TestCase(MapStyleTypes.Automatic, MapStyleTypes.Dark)]
		[TestCase(MapStyleTypes.Streets, MapStyleTypes.Dark)]
		[TestCase(MapStyleTypes.Light, MapStyleTypes.Dark)]
		[TestCase(MapStyleTypes.Dark, MapStyleTypes.Dark)]
		[TestCase(MapStyleTypes.Outdoors, MapStyleTypes.Outdoors)]
		[TestCase(MapStyleTypes.Satellite, MapStyleTypes.Satellite)]
		[TestCase(MapStyleTypes.SatelliteStreets, MapStyleTypes.SatelliteStreets)]
		[TestCase(MapStyleTypes.NavigationDay, MapStyleTypes.NavigationNight)]
		[TestCase(MapStyleTypes.NavigationNight, MapStyleTypes.NavigationNight)]
		public void should_pair_automatic_night_style_with_day_style(MapStyleTypes day, MapStyleTypes expectedNight)
		{
			MapStylePresets.ResolveNightStyle(day, MapStyleTypes.Automatic).Should().Be(expectedNight);
		}

		[Test]
		public void should_prefer_explicit_night_style()
		{
			MapStylePresets.ResolveNightStyle(MapStyleTypes.NavigationDay, MapStyleTypes.Satellite).Should().Be(MapStyleTypes.Satellite);
		}

		[Test]
		public void should_treat_undefined_values_as_automatic()
		{
			MapStylePresets.ResolveDayStyle((MapStyleTypes)42).Should().Be(MapStyleTypes.Streets);
			MapStylePresets.ResolveNightStyle(MapStyleTypes.Streets, (MapStyleTypes)42).Should().Be(MapStyleTypes.Dark);
		}

		[Test]
		public void should_map_every_selectable_style_to_a_distinct_mapbox_style()
		{
			var urls = new System.Collections.Generic.HashSet<string>();

			foreach (var style in MapStylePresets.SelectableStyles)
			{
				var url = MapStylePresets.GetStyleUrl(style);

				url.Should().StartWith("mapbox://styles/mapbox/");
				urls.Add(url).Should().BeTrue($"{style} must not share a style url with another preset");
			}

			MapStylePresets.GetStyleUrl(MapStyleTypes.Automatic).Should().Be(MapStylePresets.StreetsStyleUrl);
		}

		[Test]
		public void should_list_every_defined_style_except_automatic()
		{
			MapStylePresets.SelectableStyles.Should().BeEquivalentTo(
				System.Enum.GetValues<MapStyleTypes>().Where(s => s != MapStyleTypes.Automatic));
		}
	}
}
