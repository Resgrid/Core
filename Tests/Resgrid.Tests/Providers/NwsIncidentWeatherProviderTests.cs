using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Weather;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class NwsIncidentWeatherProviderTests
	{
		[Test]
		public async Task GetWeather_WithNwsResponses_MapsForecastObservationAndRadarOverlay()
		{
			// Arrange
			var handler = new NwsWeatherHandler();
			using var httpClient = new HttpClient(handler);
			var cacheProvider = CreatePassthroughCacheProvider();
			var provider = new NwsIncidentWeatherProvider(httpClient, cacheProvider.Object);

			// Act
			var weather = await provider.GetWeatherAsync(41.1234m, -122.5678m, 1);

			// Assert
			weather.Source.Should().Be("National Weather Service");
			weather.HourlyForecast.Should().ContainSingle();
			weather.HourlyForecast[0].TemperatureCelsius.Should().Be(20m);
			weather.HourlyForecast[0].PrecipitationProbabilityPercent.Should().Be(30);
			weather.Current.StationId.Should().Be("KXYZ");
			weather.Current.TemperatureCelsius.Should().Be(12.5m);
			weather.Current.BarometricPressureHpa.Should().Be(1013.3m);
			weather.Overlays.Should().ContainSingle(x =>
				x.Id == "noaa-mrms-base-reflectivity" && x.LayerIds == "3" && x.RefreshSeconds == 300);
			weather.Overlays[0].ExportUrlTemplate.Should().Contain("mapservices.weather.noaa.gov");
			handler.RequestCount.Should().Be(4);
			cacheProvider.Verify(x => x.RetrieveAsync<IncidentWeather>(
				It.IsAny<string>(), It.IsAny<Func<Task<IncidentWeather>>>(), TimeSpan.FromMinutes(5)), Times.Once);
		}

		[Test]
		public async Task GetWeather_WithCachedWeather_DoesNotCallNws()
		{
			// Arrange
			var cachedWeather = new IncidentWeather { Source = "Cached weather" };
			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider
				.Setup(x => x.RetrieveAsync<IncidentWeather>(
					It.IsAny<string>(), It.IsAny<Func<Task<IncidentWeather>>>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(cachedWeather);
			var handler = new NwsWeatherHandler();
			using var httpClient = new HttpClient(handler);
			var provider = new NwsIncidentWeatherProvider(httpClient, cacheProvider.Object);

			// Act
			var weather = await provider.GetWeatherAsync(41.1234m, -122.5678m, 1);

			// Assert
			weather.Should().BeSameAs(cachedWeather);
			handler.RequestCount.Should().Be(0);
		}

		[TestCase(-91, 0)]
		[TestCase(91, 0)]
		[TestCase(0, -181)]
		[TestCase(0, 181)]
		public async Task GetWeather_WithInvalidCoordinates_RejectsRequest(decimal latitude, decimal longitude)
		{
			// Arrange
			var cacheProvider = CreatePassthroughCacheProvider();
			using var httpClient = new HttpClient(new NwsWeatherHandler());
			var provider = new NwsIncidentWeatherProvider(httpClient, cacheProvider.Object);

			// Act
			Func<Task> act = () => provider.GetWeatherAsync(latitude, longitude);

			// Assert
			await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
			cacheProvider.Verify(x => x.RetrieveAsync<IncidentWeather>(
				It.IsAny<string>(), It.IsAny<Func<Task<IncidentWeather>>>(), It.IsAny<TimeSpan>()), Times.Never);
		}

		private static Mock<ICacheProvider> CreatePassthroughCacheProvider()
		{
			var cacheProvider = new Mock<ICacheProvider>();
			cacheProvider
				.Setup(x => x.RetrieveAsync<IncidentWeather>(
					It.IsAny<string>(), It.IsAny<Func<Task<IncidentWeather>>>(), It.IsAny<TimeSpan>()))
				.Returns((string _, Func<Task<IncidentWeather>> fallback, TimeSpan _) => fallback());
			return cacheProvider;
		}

		private sealed class NwsWeatherHandler : HttpMessageHandler
		{
			public int RequestCount { get; private set; }

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				RequestCount++;
				var url = request.RequestUri.ToString();
				string json;
				if (url.Contains("/points/", StringComparison.OrdinalIgnoreCase))
				{
					json = """
					{
					  "properties": {
					    "forecastHourly": "https://test.local/forecast",
					    "observationStations": "https://test.local/stations"
					  }
					}
					""";
				}
				else if (url.EndsWith("/forecast", StringComparison.OrdinalIgnoreCase))
				{
					json = """
					{
					  "properties": {
					    "updated": "2026-07-19T12:00:00+00:00",
					    "periods": [{
					      "number": 1,
					      "name": "This Hour",
					      "startTime": "2026-07-19T12:00:00+00:00",
					      "endTime": "2026-07-19T13:00:00+00:00",
					      "isDaytime": true,
					      "temperature": 68,
					      "temperatureUnit": "F",
					      "probabilityOfPrecipitation": { "value": 30 },
					      "windSpeed": "10 mph",
					      "windDirection": "SW",
					      "shortForecast": "Showers",
					      "detailedForecast": "Showers likely.",
					      "icon": "https://api.weather.gov/icons/test"
					    }]
					  }
					}
					""";
				}
				else if (url.EndsWith("/stations", StringComparison.OrdinalIgnoreCase))
				{
					json = """
					{ "features": [{ "id": "https://api.weather.gov/stations/KXYZ", "properties": { "stationIdentifier": "KXYZ" } }] }
					""";
				}
				else
				{
					json = """
					{
					  "properties": {
					    "textDescription": "Light rain",
					    "timestamp": "2026-07-19T12:05:00+00:00",
					    "temperature": { "value": 12.5, "unitCode": "wmoUnit:degC" },
					    "relativeHumidity": { "value": 82, "unitCode": "wmoUnit:percent" },
					    "windSpeed": { "value": 18, "unitCode": "wmoUnit:km_h-1" },
					    "windGust": { "value": 25, "unitCode": "wmoUnit:km_h-1" },
					    "windDirection": { "value": 225, "unitCode": "wmoUnit:degree_(angle)" },
					    "barometricPressure": { "value": 101325, "unitCode": "wmoUnit:Pa" },
					    "visibility": { "value": 16000, "unitCode": "wmoUnit:m" }
					  }
					}
					""";
				}

				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(json)
				});
			}
		}
	}
}
