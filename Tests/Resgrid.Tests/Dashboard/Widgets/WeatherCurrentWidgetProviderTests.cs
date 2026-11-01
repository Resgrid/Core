using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Awareness;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture, NonParallelizable]
	public class WeatherCurrentWidgetProviderTests
	{
		private const string ApiKey = "secret-openweather-key";

		// 2026-10-31 15:00 UTC is 08:00 at UTC-7.
		private const string CurrentJson = "{\"name\":\"Reno\",\"dt\":1793458800,\"timezone\":-25200,\"main\":{\"temp\":51.6,\"feels_like\":49.2,\"humidity\":40}," +
			"\"wind\":{\"speed\":7.4,\"deg\":210},\"weather\":[{\"main\":\"Clouds\",\"description\":\"broken clouds\",\"icon\":\"04d\"}]}";

		private string _bigBoardKey, _dispatchKey;
		private bool _cacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_bigBoardKey = MappingConfig.BigBoardOpenWeatherApiKey;
			_dispatchKey = MappingConfig.DispatchOpenWeatherApiKey;
			_cacheEnabled = SystemBehaviorConfig.CacheEnabled;
			MappingConfig.BigBoardOpenWeatherApiKey = ApiKey;
			MappingConfig.DispatchOpenWeatherApiKey = string.Empty;
			SystemBehaviorConfig.CacheEnabled = true;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.BigBoardOpenWeatherApiKey = _bigBoardKey;
			MappingConfig.DispatchOpenWeatherApiKey = _dispatchKey;
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
		}

		private static string ForecastJson()
		{
			// Three-hour slots from 2026-10-31 18:00 UTC (11:00 local) for two local days.
			var start = new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
			var items = new List<string>();
			for (var i = 0; i < 12; i++)
			{
				var temp = 40 + i;
				var pop = i == 5 ? 0.6 : 0.1;
				var main = i == 7 ? "Rain" : "Clear";
				items.Add($"{{\"dt\":{start + i * 10800},\"main\":{{\"temp\":{temp},\"temp_min\":{temp - 1},\"temp_max\":{temp + 1}}},\"pop\":{pop.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"weather\":[{{\"main\":\"{main}\",\"icon\":\"01d\"}}]}}");
			}

			return "{\"city\":{\"timezone\":-25200},\"list\":[" + string.Join(",", items) + "]}";
		}

		private sealed class StubHandler : HttpMessageHandler
		{
			public List<Uri> Requests { get; } = new List<Uri>();
			public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests.Add(request.RequestUri);
				var body = request.RequestUri.AbsolutePath.EndsWith("/forecast") ? ForecastJson() : CurrentJson;
				return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
			}
		}

		private static (WeatherCurrentWidgetProvider Provider, StubHandler Handler, Mock<ICacheProvider> Cache) Create(string center = "39.53,-119.81", string cached = null)
		{
			var handler = new StubHandler();
			var cache = new Mock<ICacheProvider>(MockBehavior.Strict);
			cache.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync(cached);
			cache.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

			var settings = new Mock<IDepartmentSettingsService>(MockBehavior.Strict);
			settings.Setup(x => x.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId)).ReturnsAsync(center);
			settings.Setup(x => x.GetBigBoardCenterAddressDepartmentAsync(DepartmentId)).ReturnsAsync((Address)null);
			var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			var geo = new Mock<IGeoLocationProvider>(MockBehavior.Strict);

			var provider = new WeatherCurrentWidgetProvider(new HttpClient(handler), cache.Object, settings.Object, departments.Object, geo.Object);
			return (provider, handler, cache);
		}

		[Test]
		public async Task Fetches_current_and_forecast_for_the_department_center_and_caches_it_without_the_key()
		{
			var (provider, handler, cache) = Create();

			var data = (WeatherCurrentWidgetData)await provider.GetDataAsync(For("weather.current"), CancellationToken.None);

			data.Available.Should().BeTrue();
			data.LocationName.Should().Be("Reno");
			data.Units.Should().Be("imperial");
			data.Current.Temperature.Should().Be(52);
			data.Current.Condition.Should().Be("Clouds");
			data.Current.WindDirection.Should().Be(210);
			data.Forecast.Should().HaveCount(2, "slots run from 11:00 local today into tomorrow");
			data.Forecast[0].Day.Should().Be("2026-10-31");
			data.Forecast[0].High.Should().Be(45, "the 23:00 local slot (the day's last) has temp_max 45");
			data.Forecast[1].PrecipitationChance.Should().Be(60);
			handler.Requests.Should().HaveCount(2);
			handler.Requests.Should().OnlyContain(u => u.Query.Contains("units=imperial") && u.Query.Contains("lat=39.53"));

			cache.Verify(x => x.SetStringAsync(WeatherCurrentWidgetProvider.CacheKey(DepartmentId, "imperial"),
				It.Is<string>(json => !json.Contains(ApiKey)), WeatherCurrentWidgetProvider.CacheDuration), Times.Once);
		}

		[Test]
		public async Task A_cache_hit_does_not_call_openweather()
		{
			var cached = JsonConvert.SerializeObject(new WeatherCurrentWidgetData
			{
				Available = true, Units = "metric", LocationName = "Cached", Current = new WeatherConditions { Temperature = 11 }
			});
			var (provider, handler, _) = Create(cached: cached);

			var data = (WeatherCurrentWidgetData)await provider.GetDataAsync(
				For("weather.current", CommsPlanningWidgetContexts.Settings(("units", "metric"))), CancellationToken.None);

			data.LocationName.Should().Be("Cached");
			handler.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task A_blank_cache_entry_is_a_miss()
		{
			var (provider, handler, _) = Create(cached: "{}");

			var data = (WeatherCurrentWidgetData)await provider.GetDataAsync(For("weather.current"), CancellationToken.None);

			data.Available.Should().BeTrue();
			handler.Requests.Should().HaveCount(2);
		}

		[Test]
		public async Task No_key_means_no_request()
		{
			MappingConfig.BigBoardOpenWeatherApiKey = string.Empty;
			var (provider, handler, _) = Create();

			var data = (WeatherCurrentWidgetData)await provider.GetDataAsync(For("weather.current"), CancellationToken.None);

			data.Available.Should().BeFalse();
			handler.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task No_center_is_unavailable_rather_than_weather_for_a_default_point()
		{
			var (provider, handler, _) = Create(center: null);

			var data = (WeatherCurrentWidgetData)await provider.GetDataAsync(For("weather.current"), CancellationToken.None);

			data.Available.Should().BeFalse();
			handler.Requests.Should().BeEmpty();
		}

		[Test]
		public async Task A_failed_request_never_carries_the_key_in_its_error()
		{
			var (provider, handler, cache) = Create();
			handler.Status = HttpStatusCode.Unauthorized;

			Func<Task> act = () => provider.GetDataAsync(For("weather.current"), CancellationToken.None);

			var error = await act.Should().ThrowAsync<InvalidOperationException>();
			error.Which.Message.Should().NotContain(ApiKey).And.Contain("401");
			cache.Verify(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
		}
	}
}
