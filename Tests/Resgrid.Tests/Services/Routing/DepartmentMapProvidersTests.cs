using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.GeoLocationProvider.Routing;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>
	/// A department's own map provider accounts (setting 125): validated and encrypted on save, never shown back, decrypted
	/// once per change, and used first for routing, geocoding and traffic with the install's providers behind them.
	/// </summary>
	[TestFixture]
	public class DepartmentMapProvidersServiceTests
	{
		// Each test uses its own department: decrypted accounts are kept in process memory per department.
		private static int _nextDepartment = 910_000;

		private int _departmentId;
		private MappingProviderOverrides _stored;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IEncryptionService> _encryption;
		private DepartmentMapProvidersService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentId = Interlocked.Increment(ref _nextDepartment);
			_stored = new MappingProviderOverrides();

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetMappingProviderOverridesAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => _stored);
			_settings.Setup(s => s.SetMappingProviderOverridesAsync(It.IsAny<int>(), It.IsAny<MappingProviderOverrides>(), It.IsAny<CancellationToken>()))
				.Callback((int _, MappingProviderOverrides config, CancellationToken __) => _stored = config ?? new MappingProviderOverrides())
				.ReturnsAsync((DepartmentSetting)null);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, Code = "TEST" });

			_encryption = new Mock<IEncryptionService>();
			_encryption.Setup(e => e.EncryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((string plain, int id, string _) => $"enc[{id}]:{plain}");
			_encryption.Setup(e => e.DecryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((string cipher, int id, string _) => cipher.Substring($"enc[{id}]:".Length));

			_service = new DepartmentMapProvidersService(_settings.Object, departments.Object, _encryption.Object);
		}

		[Test]
		public async Task a_routing_key_is_stored_encrypted_and_read_back_decrypted()
		{
			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "Mapbox", RoutingApiKey = " sk.secret " });

			result.Saved.Should().BeTrue();
			_stored.RoutingProvider.Should().Be(RoutingProviderNames.Mapbox);
			_stored.RoutingApiKeyEncrypted.Should().Be($"enc[{_departmentId}]:sk.secret");

			var providers = await _service.GetAsync(_departmentId);
			providers.Routing.Provider.Should().Be(RoutingProviderNames.Mapbox);
			providers.Routing.ApiKey.Should().Be("sk.secret");
			providers.Routing.DepartmentId.Should().Be(_departmentId);
		}

		[Test]
		public async Task a_blank_key_keeps_the_saved_one_but_a_new_provider_needs_its_own()
		{
			await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "google", RoutingApiKey = "AIza-first" });

			var kept = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "google", GoogleRoutingApi = "routes" });
			kept.Saved.Should().BeTrue();
			(await _service.GetAsync(_departmentId)).Routing.ApiKey.Should().Be("AIza-first");
			_stored.GoogleRoutingApi.Should().Be("routes");

			var switched = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "mapbox" });
			switched.Saved.Should().BeFalse("the Google key is not a Mapbox token");
			switched.Errors.Should().Contain(DepartmentMapProvidersError.RoutingKeyRequired);
			_stored.RoutingProvider.Should().Be("google", "nothing is saved when there is an error");
		}

		[Test]
		public async Task clearing_the_routing_key_drops_it()
		{
			await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "valhalla", RoutingUrl = "https://8.8.8.8/valhalla", RoutingApiKey = "k" });

			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate
			{
				RoutingProvider = "valhalla", RoutingUrl = "https://8.8.8.8/valhalla", ClearRoutingApiKey = true
			});

			result.Saved.Should().BeTrue("a Valhalla key is optional");
			_stored.RoutingApiKeyEncrypted.Should().BeNull();
		}

		[TestCase("http://8.8.8.8/valhalla")]
		[TestCase("https://10.0.0.5/valhalla")]
		[TestCase("https://127.0.0.1:8002")]
		[TestCase("https://169.254.169.254/latest")]
		[TestCase("https://user:pass@8.8.8.8/")]
		[TestCase("https://localhost/valhalla")]
		[TestCase("not a url")]
		public async Task a_department_routing_url_must_be_public_https(string url)
		{
			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "valhalla", RoutingUrl = url });

			result.Saved.Should().BeFalse();
			result.Errors.Should().Contain(DepartmentMapProvidersError.RoutingUrlNotPublic);
		}

		[Test]
		public async Task a_public_https_routing_url_is_accepted_and_bearer_kept()
		{
			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate
			{
				RoutingProvider = "valhalla", RoutingUrl = "https://8.8.8.8/valhalla/", RoutingApiKey = "sr_key", RoutingAuthScheme = "Bearer"
			});

			result.Saved.Should().BeTrue();
			_stored.RoutingUrl.Should().Be("https://8.8.8.8/valhalla");
			_stored.RoutingAuthScheme.Should().Be(MappingConfig.ValhallaBearerAuthScheme);
		}

		[TestCase("sk.secret", false)]
		[TestCase("", false)]
		[TestCase("pk.public", true)]
		public async Task the_traffic_token_must_be_public(string token, bool saved)
		{
			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { TrafficSource = "mapbox", TrafficMapboxToken = token });

			result.Saved.Should().Be(saved);
			if (saved)
			{
				var traffic = (await _service.GetAsync(_departmentId)).Traffic;
				traffic.Type.Should().Be(TrafficLayerSource.VectorType);
				traffic.Url.Should().Contain("mapbox.mapbox-traffic-v1").And.Contain("access_token=pk.public");
				traffic.IsDepartment.Should().BeTrue();
			}
		}

		[TestCase("https://api.tomtom.com/traffic/map/4/tile/flow/relative0/{z}/{x}/{y}.png?key=abc", true)]
		[TestCase("http://tiles.example.com/{z}/{x}/{y}.png", false)]
		[TestCase("https://tiles.example.com/{z}/{x}.png", false)]
		public async Task a_traffic_tile_url_needs_https_and_tile_placeholders(string url, bool saved)
		{
			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { TrafficSource = "tiles", TrafficTileUrl = url });

			result.Saved.Should().Be(saved);
			if (saved)
			{
				_stored.TrafficTileUrlEncrypted.Should().StartWith("enc[");
				var traffic = (await _service.GetAsync(_departmentId)).Traffic;
				traffic.Type.Should().Be(TrafficLayerSource.RasterType);
				traffic.Url.Should().Be(url);
			}
		}

		[Test]
		public async Task a_geocoding_provider_needs_a_key()
		{
			var missing = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { GeocodingProvider = "locationiq" });
			missing.Errors.Should().Contain(DepartmentMapProvidersError.GeocodingKeyRequired);

			var ok = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { GeocodingProvider = "locationiq", GeocodingApiKey = "pk.liq" });
			ok.Saved.Should().BeTrue();
			var providers = await _service.GetAsync(_departmentId);
			providers.GeocodingProvider.Should().Be("locationiq");
			providers.GeocodingApiKey.Should().Be("pk.liq");
		}

		[Test]
		public async Task unknown_providers_are_refused()
		{
			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "here", TrafficSource = "waze", GeocodingProvider = "bing" });

			result.Errors.Should().BeEquivalentTo(new[]
			{
				DepartmentMapProvidersError.UnknownRoutingProvider,
				DepartmentMapProvidersError.UnknownTrafficSource,
				DepartmentMapProvidersError.UnknownGeocodingProvider
			});
		}

		[Test]
		public async Task decryption_runs_once_per_stored_value()
		{
			await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "mapbox", RoutingApiKey = "sk.one" });

			await _service.GetAsync(_departmentId);
			await _service.GetAsync(_departmentId);
			await _service.GetAsync(_departmentId);

			_encryption.Verify(e => e.DecryptForDepartment(It.IsAny<string>(), _departmentId, It.IsAny<string>()), Times.Once);

			await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "mapbox", RoutingApiKey = "sk.two" });
			(await _service.GetAsync(_departmentId)).Routing.ApiKey.Should().Be("sk.two");
		}

		[Test]
		public async Task switching_everything_back_to_the_defaults_leaves_no_accounts()
		{
			await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate { RoutingProvider = "mapbox", RoutingApiKey = "sk.one" });

			var result = await _service.SaveAsync(_departmentId, new DepartmentMapProvidersUpdate());

			result.Saved.Should().BeTrue();
			_stored.IsEmpty.Should().BeTrue();
			(await _service.GetAsync(_departmentId)).Should().BeSameAs(DepartmentMapProviders.None);
		}

		[Test]
		public async Task department_zero_has_no_accounts()
		{
			(await _service.GetAsync(0)).Should().BeSameAs(DepartmentMapProviders.None);
		}
	}

	/// <summary>The routing chain with a department's own account in front of the install's providers.</summary>
	[TestFixture]
	public class DepartmentRoutingChainTests
	{
		private static int _nextDepartment = 920_000;

		private string _provider;
		private string _fallback;
		private bool _cacheEnabled;
		private bool _navigation;

		[SetUp]
		public void SetUp()
		{
			_provider = MappingConfig.RoutingProvider;
			_fallback = MappingConfig.RoutingFallbackProvider;
			_cacheEnabled = SystemBehaviorConfig.CacheEnabled;
			_navigation = MappingConfig.NavigationEnabled;
			SystemBehaviorConfig.CacheEnabled = false;
			MappingConfig.NavigationEnabled = true;
			MappingConfig.RoutingProvider = RoutingProviderNames.Valhalla;
			MappingConfig.RoutingFallbackProvider = RoutingProviderNames.Estimate;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.RoutingProvider = _provider;
			MappingConfig.RoutingFallbackProvider = _fallback;
			SystemBehaviorConfig.CacheEnabled = _cacheEnabled;
			MappingConfig.NavigationEnabled = _navigation;
		}

		/// <summary>A provider whose department copies record the credentials they were given.</summary>
		private sealed class FakeProvider : IRoutingProvider
		{
			public FakeProvider(string name, bool navigation, Func<RoutingProviderCredentials, RouteResult> answer, RoutingProviderCredentials credentials = null)
			{
				Name = name;
				Answer = answer;
				Credentials = credentials;
				Capabilities = new RoutingCapabilities { Steps = true, Navigation = navigation, Matrix = true, MaxWaypoints = 25, MaxMatrixCoordinates = 25 };
			}

			public string Name { get; }
			public RoutingCapabilities Capabilities { get; }
			public bool IsConfigured => true;
			public RoutingProviderCredentials Credentials { get; }
			public Func<RoutingProviderCredentials, RouteResult> Answer { get; }
			public List<RoutingProviderCredentials> Calls { get; } = new List<RoutingProviderCredentials>();
			public List<FakeProvider> Copies { get; } = new List<FakeProvider>();

			public Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken)
			{
				Calls.Add(Credentials);
				return Task.FromResult(Answer(Credentials));
			}

			public Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken) =>
				Task.FromResult(new RouteMatrixResult { Error = "not used" });

			public Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken) =>
				Task.FromResult(new RouteOptimizationResult { Error = "not used" });

			public IRoutingProvider WithCredentials(RoutingProviderCredentials credentials)
			{
				var copy = new FakeProvider(Name, Capabilities.Navigation, Answer, credentials);
				Copies.Add(copy);
				return copy;
			}
		}

		private static RouteRequest Request(int departmentId) => new RouteRequest
		{
			DepartmentId = departmentId,
			Waypoints = new List<RoutingWaypoint> { new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.54, -119.75) }
		};

		private static Lazy<IDepartmentMapProvidersService> Accounts(int departmentId, RoutingProviderCredentials routing)
		{
			var service = new Mock<IDepartmentMapProvidersService>();
			service.Setup(s => s.GetAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => id == departmentId ? new DepartmentMapProviders { Routing = routing } : DepartmentMapProviders.None);
			return new Lazy<IDepartmentMapProvidersService>(() => service.Object);
		}

		[Test]
		public async Task the_department_account_answers_first_with_its_credentials()
		{
			var department = Interlocked.Increment(ref _nextDepartment);
			var valhalla = new FakeProvider(RoutingProviderNames.Valhalla, true, c => new RouteResult { Successful = true, DurationSeconds = 100 });
			var mapbox = new FakeProvider(RoutingProviderNames.Mapbox, true, c => new RouteResult { Successful = true, DurationSeconds = 200 });
			var usage = new Mock<IRoutingUsageService>();
			var service = new RoutingService(new IRoutingProvider[] { valhalla, mapbox, new EstimateRoutingProvider() }, null, usage.Object,
				Accounts(department, new RoutingProviderCredentials { DepartmentId = department, Provider = RoutingProviderNames.Mapbox, ApiKey = "sk.dept" }));

			var route = await service.GetRouteAsync(Request(department));

			route.DurationSeconds.Should().Be(200);
			route.Provider.Should().Be(RoutingProviderNames.Mapbox);
			mapbox.Copies.Should().ContainSingle().Which.Calls.Should().ContainSingle().Which.ApiKey.Should().Be("sk.dept");
			mapbox.Calls.Should().BeEmpty("the install's Mapbox account is not used");
			valhalla.Calls.Should().BeEmpty();
			usage.Verify(u => u.RecordAsync(RoutingProviderNames.Mapbox, RoutingUsageOperation.Route, RoutingUsageOutcome.Succeeded, 0, true), Times.Once);
		}

		[Test]
		public async Task other_departments_and_department_zero_use_the_install_providers()
		{
			var department = Interlocked.Increment(ref _nextDepartment);
			var valhalla = new FakeProvider(RoutingProviderNames.Valhalla, true, c => new RouteResult { Successful = true, DurationSeconds = 100 });
			var mapbox = new FakeProvider(RoutingProviderNames.Mapbox, true, c => new RouteResult { Successful = true, DurationSeconds = 200 });
			var service = new RoutingService(new IRoutingProvider[] { valhalla, mapbox, new EstimateRoutingProvider() }, null, null,
				Accounts(department, new RoutingProviderCredentials { DepartmentId = department, Provider = RoutingProviderNames.Mapbox, ApiKey = "sk.dept" }));

			(await service.GetRouteAsync(Request(department + 1))).DurationSeconds.Should().Be(100);
			(await service.GetRouteAsync(Request(0))).DurationSeconds.Should().Be(100);
			mapbox.Copies.Should().BeEmpty();
		}

		[Test]
		public async Task a_failing_department_account_falls_back_to_the_install_without_benching_it()
		{
			var department = Interlocked.Increment(ref _nextDepartment);
			var valhalla = new FakeProvider(RoutingProviderNames.Valhalla, true,
				c => c == null ? new RouteResult { Successful = true, DurationSeconds = 100 } : new RouteResult { Error = "401: bad key" });
			var service = new RoutingService(new IRoutingProvider[] { valhalla, new EstimateRoutingProvider() }, null, null,
				Accounts(department, new RoutingProviderCredentials { DepartmentId = department, Provider = RoutingProviderNames.Valhalla, Url = "https://8.8.8.8" }));

			var first = await service.GetRouteAsync(Request(department));
			var second = await service.GetRouteAsync(Request(department));
			var install = await service.GetRouteAsync(Request(0));

			first.DurationSeconds.Should().Be(100, "the install's Valhalla answers when the department's fails");
			second.DurationSeconds.Should().Be(100);
			install.DurationSeconds.Should().Be(100);
			// The department's account is benched after its failure; the install's Valhalla (same provider name) is not.
			valhalla.Copies.Sum(c => c.Calls.Count).Should().Be(1);
			valhalla.Calls.Should().HaveCount(3);
		}

		[TestCase(RoutingProviderNames.Mapbox, true)]
		[TestCase(RoutingProviderNames.Google, false)]
		public async Task provider_info_reflects_the_department_account(string provider, bool navigation)
		{
			var department = Interlocked.Increment(ref _nextDepartment);
			var valhalla = new FakeProvider(RoutingProviderNames.Valhalla, true, c => new RouteResult { Successful = true });
			var own = new FakeProvider(provider, provider == RoutingProviderNames.Mapbox, c => new RouteResult { Successful = true });
			var service = new RoutingService(new IRoutingProvider[] { valhalla, own, new EstimateRoutingProvider() }, null, null,
				Accounts(department, new RoutingProviderCredentials { DepartmentId = department, Provider = provider, ApiKey = "key" }));

			var info = await service.GetProviderInfoAsync(department);
			var installInfo = await service.GetProviderInfoAsync(0);

			info.Name.Should().Be(provider);
			info.DepartmentAccount.Should().BeTrue();
			info.NavigationAvailable.Should().Be(navigation);
			installInfo.Name.Should().Be(RoutingProviderNames.Valhalla);
			installInfo.DepartmentAccount.Should().BeFalse();
		}

		[Test]
		public async Task an_edited_department_account_never_gets_the_old_accounts_cached_routes()
		{
			// PR 585: the cache scope was name#d{department}, so a department that pointed Valhalla at a new server kept
			// getting the old server's routes for up to 30 days.
			SystemBehaviorConfig.CacheEnabled = true;
			var department = Interlocked.Increment(ref _nextDepartment);
			var valhalla = new FakeProvider(RoutingProviderNames.Valhalla, true,
				c => new RouteResult { Successful = true, DurationSeconds = c?.Url == "https://two.example.org" ? 222 : 111 });
			var credentials = new RoutingProviderCredentials { DepartmentId = department, Provider = RoutingProviderNames.Valhalla, Url = "https://one.example.org" };
			var service = new RoutingService(new IRoutingProvider[] { valhalla, new EstimateRoutingProvider() }, new Resgrid.Tests.Security.Live.InMemoryCacheProvider(), null,
				Accounts(department, credentials));

			(await service.GetRouteAsync(Request(department))).DurationSeconds.Should().Be(111);
			(await service.GetRouteAsync(Request(department))).DurationSeconds.Should().Be(111);
			valhalla.Copies.Sum(c => c.Calls.Count).Should().Be(1, "the repeat is a cache hit");
			var before = (await service.GetProviderInfoAsync(department)).AccountRevision;

			credentials.Url = "https://two.example.org";

			(await service.GetRouteAsync(Request(department))).DurationSeconds.Should().Be(222);
			var after = (await service.GetProviderInfoAsync(department)).AccountRevision;
			after.Should().NotBeNullOrWhiteSpace().And.NotBe(before);
			after.Should().NotContain("two.example.org");
		}

		[Test]
		public async Task a_department_account_that_cannot_optimize_hands_the_stop_order_to_the_install()
		{
			// PR 585: only the first provider in the chain was asked; a department's Google account sent every plan to the
			// matrix heuristic even though the install's Valhalla optimizes.
			var department = Interlocked.Increment(ref _nextDepartment);
			var google = new Mock<IRoutingProvider>();
			google.SetupGet(p => p.Name).Returns(RoutingProviderNames.Google);
			google.SetupGet(p => p.IsConfigured).Returns(true);
			google.SetupGet(p => p.Capabilities).Returns(new RoutingCapabilities { MaxWaypoints = 25 });
			google.Setup(p => p.WithCredentials(It.IsAny<RoutingProviderCredentials>())).Returns(google.Object);
			var valhalla = new Mock<IRoutingProvider>();
			valhalla.SetupGet(p => p.Name).Returns(RoutingProviderNames.Valhalla);
			valhalla.SetupGet(p => p.IsConfigured).Returns(true);
			valhalla.SetupGet(p => p.Capabilities).Returns(new RoutingCapabilities { Optimize = true, MaxOptimizeWaypoints = 25, MaxWaypoints = 25 });
			valhalla.Setup(p => p.OptimizeAsync(It.IsAny<RouteOptimizationRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RouteOptimizationResult { Successful = true, Provider = RoutingProviderNames.Valhalla, Order = new List<int> { 0, 2, 1, 3 } });
			var service = new RoutingService(new IRoutingProvider[] { valhalla.Object, google.Object, new EstimateRoutingProvider() }, null, null,
				Accounts(department, new RoutingProviderCredentials { DepartmentId = department, Provider = RoutingProviderNames.Google, ApiKey = "key" }));

			var result = await service.OptimizeAsync(new RouteOptimizationRequest
			{
				DepartmentId = department,
				Waypoints = new List<RoutingWaypoint>
				{
					new RoutingWaypoint(39.50, -119.80), new RoutingWaypoint(39.53, -119.81), new RoutingWaypoint(39.51, -119.79), new RoutingWaypoint(39.54, -119.75)
				}
			});

			result.Order.Should().Equal(0, 2, 1, 3);
			result.Provider.Should().Be(RoutingProviderNames.Valhalla);
			valhalla.Verify(p => p.OptimizeAsync(It.IsAny<RouteOptimizationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
		}
	}

	/// <summary>The Valhalla provider with a department's own server.</summary>
	[TestFixture]
	public class DepartmentValhallaProviderTests
	{
		private string _coverage;

		[SetUp]
		public void SetUp()
		{
			_coverage = MappingConfig.ValhallaCoverage;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.ValhallaCoverage = _coverage;
		}

		[Test]
		public void a_department_server_needs_an_https_url_and_has_stock_limits()
		{
			var template = new ValhallaRoutingProvider();

			template.WithCredentials(new RoutingProviderCredentials { Provider = "valhalla", Url = "http://8.8.8.8" }).IsConfigured.Should().BeFalse();

			var own = template.WithCredentials(new RoutingProviderCredentials { Provider = "valhalla", Url = "https://valhalla.example.org" });
			own.IsConfigured.Should().BeTrue();
			own.Capabilities.MaxWaypoints.Should().Be(20);
			own.Capabilities.MaxExcludePoints.Should().Be(50);
		}

		[Test]
		public async Task the_install_coverage_boxes_do_not_apply_to_a_department_server()
		{
			MappingConfig.ValhallaCoverage = "-170,14,-50,84";
			// Port 1 refuses connections: the department copy must try to connect (it is not skipped as outside coverage).
			var own = new ValhallaRoutingProvider().WithCredentials(new RoutingProviderCredentials { Provider = "valhalla", Url = "https://127.0.0.1:1" });

			Func<Task> route = () => own.GetRouteAsync(new RouteRequest
			{
				Waypoints = new List<RoutingWaypoint> { new RoutingWaypoint(-33.87, 151.21), new RoutingWaypoint(-33.88, 151.20) }
			}, CancellationToken.None);

			// The public-address guard refuses 127.0.0.1 before any connection is made.
			await route.Should().ThrowAsync<HttpRequestException>();
		}

		[Test]
		public void department_keys_use_the_department_scheme()
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.simplerouting.io/valhalla/route");

			ValhallaRoutingProvider.Authorize(request, "dept-key", "bearer");

			request.Headers.Authorization.ToString().Should().Be("Bearer dept-key");
		}
	}

	/// <summary>Geocoding with a department's own key, chosen by the request's department.</summary>
	[TestFixture]
	public class DepartmentGeocodingTests
	{
		private string _providers;

		[SetUp]
		public void SetUp()
		{
			_providers = MappingConfig.GeocodingProviders;
			MappingConfig.GeocodingProviders = "nominatim";
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.GeocodingProviders = _providers;
		}

		private sealed class FakeGeocoder : IGeocodingProvider
		{
			public FakeGeocoder(string name, string key = null)
			{
				Name = name;
				Key = key;
			}

			public string Name { get; }
			public string Key { get; }
			public bool IsConfigured => true;
			public List<string> Asked { get; } = new List<string>();

			public Task<GeocodeMatch> ForwardAsync(string address, CancellationToken cancellationToken)
			{
				Asked.Add(address);
				return Task.FromResult(new GeocodeMatch { Latitude = Key == null ? 1 : 2, Longitude = 0 });
			}

			public Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken) =>
				Task.FromResult<string>(null);

			public IGeocodingProvider WithApiKey(string apiKey) => new FakeGeocoder(Name, apiKey);
		}

		private static Mock<ICacheProvider> PassThroughCache()
		{
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Returns((string _, Func<Task<string>> fallback, TimeSpan __) => fallback());
			return cache;
		}

		[Test]
		public async Task the_request_department_key_is_tried_first()
		{
			var accounts = new Mock<IDepartmentMapProvidersService>();
			accounts.Setup(s => s.GetAsync(77)).ReturnsAsync(new DepartmentMapProviders { GeocodingProvider = "google", GeocodingApiKey = "AIza-dept" });
			accounts.Setup(s => s.GetAsync(It.Is<int>(id => id != 77))).ReturnsAsync(DepartmentMapProviders.None);
			var provider = new GeoLocationProvider(PassThroughCache().Object, new IGeocodingProvider[] { new FakeGeocoder("google"), new FakeGeocoder("nominatim") },
				new Lazy<IDepartmentMapProvidersService>(() => accounts.Object));

			string own;
			using (RequestDepartment.Enter(77))
				own = await provider.GetLatLonFromAddress("1 Main St");

			string install;
			using (RequestDepartment.Enter(78))
				install = await provider.GetLatLonFromAddress("1 Main St");

			own.Should().StartWith("2,", "the department's own Google key answered");
			install.Should().StartWith("1,", "another department gets the install's chain (Nominatim)");
			RequestDepartment.Id.Should().Be(0, "the scope is restored");
		}

		[Test]
		public async Task a_department_with_its_own_geocoder_never_gets_an_answer_the_install_cached()
		{
			// PR 585: forward and reverse keys were shared, so a cached install answer bypassed the department's geocoder.
			var stored = new Dictionary<string, string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
				.Returns(async (string key, Func<Task<string>> fallback, TimeSpan _) =>
				{
					if (!stored.TryGetValue(key, out var value))
						stored[key] = value = await fallback();
					return value;
				});
			var accounts = new Mock<IDepartmentMapProvidersService>();
			accounts.Setup(s => s.GetAsync(77)).ReturnsAsync(new DepartmentMapProviders { GeocodingProvider = "google", GeocodingApiKey = "AIza-dept" });
			accounts.Setup(s => s.GetAsync(It.Is<int>(id => id != 77))).ReturnsAsync(DepartmentMapProviders.None);
			var provider = new GeoLocationProvider(cache.Object, new IGeocodingProvider[] { new FakeGeocoder("google"), new FakeGeocoder("nominatim") },
				new Lazy<IDepartmentMapProvidersService>(() => accounts.Object));

			string install, other, own;
			using (RequestDepartment.Enter(78))
				install = await provider.GetLatLonFromAddress("1 Main St");
			using (RequestDepartment.Enter(79))
				other = await provider.GetLatLonFromAddress("1 Main St");
			using (RequestDepartment.Enter(77))
				own = await provider.GetLatLonFromAddress("1 Main St");

			install.Should().StartWith("1,");
			other.Should().StartWith("1,");
			own.Should().StartWith("2,", "the department's own geocoder is asked, not the install's cached answer");
			stored.Should().HaveCount(2, "departments without their own geocoder share one entry");
		}
	}

	/// <summary>The live traffic layer a department's maps get.</summary>
	[TestFixture]
	public class DepartmentTrafficLayerTests
	{
		private string _traffic;
		private string _token;

		[SetUp]
		public void SetUp()
		{
			_traffic = MappingConfig.TrafficProvider;
			_token = MappingConfig.MapboxTrafficAccessToken;
		}

		[TearDown]
		public void TearDown()
		{
			MappingConfig.TrafficProvider = _traffic;
			MappingConfig.MapboxTrafficAccessToken = _token;
		}

		[Test]
		public async Task the_department_layer_wins_and_others_get_the_install_layer()
		{
			MappingConfig.TrafficProvider = "mapbox";
			MappingConfig.MapboxTrafficAccessToken = "pk.install";
			var accounts = new Mock<IDepartmentMapProvidersService>();
			accounts.Setup(s => s.GetAsync(5)).ReturnsAsync(new DepartmentMapProviders
			{
				Traffic = new TrafficLayerSource { Url = "https://tiles.example.com/{z}/{x}/{y}.png?key=k", Type = TrafficLayerSource.RasterType, IsDepartment = true }
			});
			accounts.Setup(s => s.GetAsync(It.Is<int>(id => id != 5))).ReturnsAsync(DepartmentMapProviders.None);
			var styles = new MapStyleService(Mock.Of<IDepartmentSettingsService>(), Mock.Of<ICacheProvider>(), new Lazy<IDepartmentMapProvidersService>(() => accounts.Object));

			var own = await styles.GetTrafficLayerAsync(5);
			var other = await styles.GetTrafficLayerAsync(6);

			own.Type.Should().Be(TrafficLayerSource.RasterType);
			own.IsDepartment.Should().BeTrue();
			other.Type.Should().Be(TrafficLayerSource.VectorType);
			other.Url.Should().Contain("access_token=pk.install");
			other.IsDepartment.Should().BeFalse();
		}

		[Test]
		public async Task no_layer_without_a_department_or_install_provider()
		{
			MappingConfig.TrafficProvider = "none";
			var styles = new MapStyleService(Mock.Of<IDepartmentSettingsService>(), Mock.Of<ICacheProvider>(), null);

			(await styles.GetTrafficLayerAsync(5)).Should().BeNull();
		}
	}
}
