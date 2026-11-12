using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Bulk removal of POIs (Mapping/DeletePOIs) and of a custom map layer's regions (CustomMaps/ClearRegions) through the real MVC
	/// pipeline: antiforgery, the department-admin gate, the typed-count confirmation and the POI-type list page.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class BulkMapDataRemovalFormTests
	{
		private const int Dept = 77;
		private const int TypeId = 40;
		private const int HydrantPoiId = 10;

		private sealed class PageBodyFilter : IResultFilter
		{
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is ViewResult view)
					context.Result = new PartialViewResult { ViewName = "/Areas/User/Views/" + context.RouteData.Values["controller"] + "/" + (view.ViewName ?? context.RouteData.Values["action"]) + ".cshtml", ViewData = view.ViewData, TempData = view.TempData };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}

		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				// NameIdentifier pins the antiforgery claim uid, so a token issued to the admin also validates for the member request:
				// the member checks below then reach the controller's own admin gate instead of failing antiforgery.
				var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "manager"), new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()), new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View) };
				if (!Request.Headers.ContainsKey("X-Member"))
					claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "bulk-test")), "bulk-test")));
			}
		}

		private sealed record PoiCall(int TypeId, List<int> Ids, int? ExpectedCount);

		private static string Token(string html)
			=> WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

		[Test]
		public async Task Poi_and_region_bulk_removal_post_through_antiforgery_admin_gate_and_typed_count()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs(); builder.Services.AddWebOptimizer();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("bulk-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("bulk-test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(MappingController).Assembly);

			// POI type 40 holds POIs 1-3 and POI 10, which a hydrant points at (hidden from the list, kept by "All").
			var type = new PoiType { PoiTypeId = TypeId, DepartmentId = Dept, Name = "Imported points", Color = "#336699", Pois = new[] { 1, 2, 3, HydrantPoiId }.Select(id => new Poi { PoiId = id, PoiTypeId = TypeId, Name = "P" + id }).ToList() };
			var poiCalls = new List<PoiCall>();
			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(type);
			mapping.Setup(x => x.DeletePOIsAsync(Dept, TypeId, It.IsAny<IEnumerable<int>>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int t, IEnumerable<int> ids, int? expected, CancellationToken _) => { poiCalls.Add(new PoiCall(t, ids.ToList(), expected)); return ids.Count(); });
			builder.Services.AddSingleton(mapping.Object);
			var hydrants = new Mock<IRecordsHydrantsService>();
			hydrants.Setup(x => x.IsModuleEnabledAsync(Dept)).ReturnsAsync(true);
			hydrants.Setup(x => x.GetMapLayerAsync(Dept, "manager", null, null, null, null)).ReturnsAsync(new List<HydrantMapPoint> { new HydrantMapPoint { HydrantId = "h", PoiId = HydrantPoiId, HydrantNumber = "H-1", InService = true } });
			builder.Services.AddSingleton(hydrants.Object);

			var layer = new IndoorMapFloor { IndoorMapFloorId = "layer-1", IndoorMapId = "map-1", Name = "Parcels" };
			var map = new IndoorMap { IndoorMapId = "map-1", DepartmentId = Dept, Name = "County" };
			var regionCalls = new List<int?>();
			var customMaps = new Mock<ICustomMapService>();
			customMaps.Setup(x => x.GetLayerByIdAsync("layer-1")).ReturnsAsync(layer);
			customMaps.Setup(x => x.GetCustomMapByIdAsync("map-1")).ReturnsAsync(map);
			customMaps.Setup(x => x.GetRegionsForLayerAsync("layer-1")).ReturnsAsync(new List<IndoorMapZone> { new IndoorMapZone { IndoorMapZoneId = "z1", IndoorMapFloorId = "layer-1", Name = "Lot 1" }, new IndoorMapZone { IndoorMapZoneId = "z2", IndoorMapFloorId = "layer-1", Name = "Lot 2" } });
			customMaps.Setup(x => x.DeleteRegionsForLayerAsync("layer-1", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string l, int? expected, CancellationToken _) => { regionCalls.Add(expected); return expected ?? 0; });
			builder.Services.AddSingleton(customMaps.Object);

			var departmentSettings = new Mock<IDepartmentSettingsService>();
			departmentSettings.Setup(x => x.GetMapConfigForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new Resgrid.Config.ResolvedMapConfig());
			builder.Services.AddSingleton(departmentSettings.Object);
			builder.Services.AddSingleton(Mock.Of<IGeoLocationProvider>());
			builder.Services.AddSingleton(Mock.Of<ICallsService>());
			builder.Services.AddSingleton(Mock.Of<IDepartmentGroupsService>());
			builder.Services.AddSingleton(Mock.Of<IActionLogsService>());
			builder.Services.AddSingleton(Mock.Of<IUnitsService>());
			builder.Services.AddSingleton(Mock.Of<IPoiImportService>());
			builder.Services.AddSingleton(Mock.Of<IPermissionsService>());
			builder.Services.AddSingleton(Mock.Of<IPersonnelRolesService>());
			builder.Services.AddSingleton(Mock.Of<IProtectedReadService>());
			builder.Services.AddSingleton(Mock.Of<IDispatchScopeService>());
			builder.Services.AddSingleton(Mock.Of<ITypeIconsService>());
			builder.Services.AddSingleton(Mock.Of<IMapIconsService>());
			builder.Services.AddSingleton(Mock.Of<ICustomStateService>());
			builder.Services.AddSingleton(Mock.Of<Resgrid.Model.Services.IAuthorizationService>());
			builder.Services.AddSingleton(Mock.Of<IDepartmentsService>(d => d.GetDepartmentByIdAsync(Dept, false) == Task.FromResult(new Department { DepartmentId = Dept, TimeZone = "Pacific Standard Time" })));
			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			// Both pages read the department map config through SettingsHelper's service-locator statics; point them at this host
			// for the test and put back whatever was there.
			var settingsFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
			var settingsAccessorField = typeof(SettingsHelper).GetField("_httpContextAccessor", settingsFlags);
			var settingsServiceField = typeof(SettingsHelper).GetField("_departmentSettingsService", settingsFlags);
			var previousSettingsAccessor = settingsAccessorField!.GetValue(null);
			var previousSettingsService = settingsServiceField!.GetValue(null);
			settingsAccessorField.SetValue(null, app.Services.GetRequiredService<IHttpContextAccessor>());
			settingsServiceField.SetValue(null, departmentSettings.Object);
			app.Use(async (context, next) => { try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); } });
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
				using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
				async Task<HttpResponseMessage> Post(string url, params (string Key, string Value)[] fields)
					=> await client.PostAsync(url, new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));

				// The POI type page sets the full user layout (bundles this host does not build), so the antiforgery token for every
				// post comes from the region editor; any token issued to this client validates for any form.
				var response = await client.GetAsync("/User/CustomMaps/RegionEditor/layer-1");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-region-bulk-delete.html"), html);
				html.Should().Contain("id=\"regionBulkDeleteForm\"").And.Contain("data-count=\"2\"");
				WebUtility.HtmlDecode(html).Should().Contain("Removes all 2 regions from this layer");
				var token = Token(html);
				token.Should().NotBeNullOrEmpty();

				// ---- POIs ----

				response = await Post("/User/Mapping/DeletePOIs", ("poiTypeId", TypeId.ToString()), ("scope", "All"), ("expectedCount", "3"));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				poiCalls.Should().BeEmpty();

				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "Selected"), ("poiIds", "1, 3,x,,-4,2"), ("expectedCount", "3"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().Contain("/User/Mapping/ViewType").And.Contain("poiTypeId=" + TypeId);
				poiCalls.Should().ContainSingle();
				poiCalls[0].Ids.Should().Equal(1, 3, 2);
				poiCalls[0].ExpectedCount.Should().Be(3);

				// "All" is every POI the list shows: the hydrant-linked POI is managed from the hydrant and stays.
				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "All"), ("poiIds", "999"), ("expectedCount", "3"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect);
				poiCalls.Should().HaveCount(2);
				poiCalls[1].Ids.Should().Equal(1, 2, 3);

				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "All"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect);
				poiCalls.Should().HaveCount(2, "a post without the typed count removes nothing");

				mapping.Setup(x => x.DeletePOIsAsync(Dept, TypeId, It.IsAny<IEnumerable<int>>(), 12345, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("3 POIs match this removal, not 12,345. Reload the list and confirm again."));
				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "All"), ("expectedCount", "12345"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, "a refused count is a page message, not an error page");

				// A type past one removal's limit: "All" checks the typed count against its whole list, then removes it in batches.
				var large = Enumerable.Range(1000, Resgrid.Services.MappingService.MaxBulkPoiDelete + 5).ToList();
				type.Pois = large.Select(id => new Poi { PoiId = id, PoiTypeId = TypeId, Name = "L" + id }).ToList();
				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "All"), ("expectedCount", "3"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect);
				poiCalls.Should().HaveCount(2, "a stale count for a large type removes nothing");
				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "All"), ("expectedCount", large.Count.ToString()));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				poiCalls.Should().HaveCount(4);
				poiCalls[2].Ids.Should().HaveCount(Resgrid.Services.MappingService.MaxBulkPoiDelete);
				poiCalls[3].Ids.Should().Equal(large.Skip(Resgrid.Services.MappingService.MaxBulkPoiDelete));
				poiCalls.Skip(2).Should().OnlyContain(c => c.ExpectedCount == null, "each batch is part of the one confirmed removal");
				poiCalls.Skip(2).SelectMany(c => c.Ids).Should().Equal(large);

				// ---- Custom map regions ----
				response = await Post("/User/CustomMaps/ClearRegions", ("layerId", "layer-1"), ("expectedCount", "2"));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				regionCalls.Should().BeEmpty();

				response = await Post("/User/CustomMaps/ClearRegions", ("__RequestVerificationToken", token), ("layerId", "layer-1"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect);
				regionCalls.Should().BeEmpty("a post without the typed count removes nothing");
				WebUtility.HtmlDecode(await client.GetStringAsync("/User/CustomMaps/RegionEditor/layer-1")).Should().Contain("Type the number of regions to remove to confirm.");

				response = await Post("/User/CustomMaps/ClearRegions", ("__RequestVerificationToken", token), ("layerId", "layer-1"), ("expectedCount", "2"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().EndWith("/User/CustomMaps/RegionEditor/layer-1");
				regionCalls.Should().Equal(2);
				WebUtility.HtmlDecode(await client.GetStringAsync("/User/CustomMaps/RegionEditor/layer-1")).Should().Contain("2 regions removed.");

				// ---- Non-admins: refused, and never shown the forms ----
				client.DefaultRequestHeaders.Add("X-Member", "1");
				response = await Post("/User/Mapping/DeletePOIs", ("__RequestVerificationToken", token), ("poiTypeId", TypeId.ToString()), ("scope", "All"), ("expectedCount", "3"));
				// SecureBaseController.Unauthorized() sends the browser to the Unauthorized page.
				response.Headers.Location!.OriginalString.Should().Contain("/Public/Unauthorized");
				(await Post("/User/CustomMaps/ClearRegions", ("__RequestVerificationToken", token), ("layerId", "layer-1"), ("expectedCount", "2"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				poiCalls.Should().HaveCount(4);
				regionCalls.Should().HaveCount(1);
				// The region editor is an admin page (CustomMapsAdminGateFormTests covers the rest of the controller).
				(await client.GetAsync("/User/CustomMaps/RegionEditor/layer-1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
			}
			finally
			{
				await app.StopAsync();
				ClaimsAuthorizationHelper._httpContextAccessor = previous;
				settingsAccessorField.SetValue(null, previousSettingsAccessor);
				settingsServiceField.SetValue(null, previousSettingsService);
			}
		}
	}
}
