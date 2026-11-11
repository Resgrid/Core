using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
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
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Custom maps (indoor and event maps whose regions dispatch can target) through the real MVC pipeline: the management pages
	/// and every write are department-admin only, map and layer deletes are antiforgery POSTs (a GET link or image tag no longer
	/// deletes anything), and the region search and layer image/tile reads dispatch uses stay open to members.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class CustomMapsAdminGateFormTests
	{
		private const int Dept = 77;

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
				// the member checks then reach the controller's admin gate instead of failing antiforgery.
				var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "manager"), new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) };
				if (!Request.Headers.ContainsKey("X-Member"))
					claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "custom-maps-test")), "custom-maps-test")));
			}
		}

		// Every ICustomMapService / IIndoorMapService method that changes data.
		private static readonly HashSet<string> WriteMethods = new HashSet<string>
		{
			nameof(ICustomMapService.SaveCustomMapAsync), nameof(ICustomMapService.DeleteCustomMapAsync), nameof(ICustomMapService.SaveLayerAsync),
			nameof(ICustomMapService.DeleteLayerAsync), nameof(ICustomMapService.SaveRegionAsync), nameof(ICustomMapService.DeleteRegionAsync),
			nameof(ICustomMapService.DeleteRegionsForLayerAsync), nameof(ICustomMapService.ProcessAndStoreTilesAsync), nameof(ICustomMapService.DeleteTilesForLayerAsync),
			nameof(ICustomMapService.ImportGeoJsonAsync), nameof(ICustomMapService.ImportKmlAsync),
			nameof(IIndoorMapService.SaveIndoorMapAsync), nameof(IIndoorMapService.DeleteIndoorMapAsync), nameof(IIndoorMapService.SaveFloorAsync),
			nameof(IIndoorMapService.DeleteFloorAsync), nameof(IIndoorMapService.SaveZoneAsync), nameof(IIndoorMapService.DeleteZoneAsync),
		};

		private static string Token(string html)
			=> WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

		[Test]
		public async Task Members_are_refused_custom_map_management_deletes_need_an_antiforgery_post_and_admins_can_delete()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs(); builder.Services.AddWebOptimizer();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("custom-maps-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("custom-maps-test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(CustomMapsController).Assembly);

			var map = new IndoorMap { IndoorMapId = "map-1", DepartmentId = Dept, Name = "Fairgrounds", MapType = (int)CustomMapType.Event, AddedOn = DateTime.UtcNow };
			var layer = new IndoorMapFloor { IndoorMapFloorId = "layer-1", IndoorMapId = "map-1", Name = "Grounds", ImageData = new byte[] { 1, 2, 3 }, ImageContentType = "image/png" };
			var zone = new IndoorMapZone { IndoorMapZoneId = "z1", IndoorMapFloorId = "layer-1", Name = "Gate A" };
			var customMaps = new Mock<ICustomMapService>();
			customMaps.Setup(x => x.GetCustomMapsForDepartmentAsync(Dept, null)).ReturnsAsync(new List<IndoorMap> { map });
			customMaps.Setup(x => x.GetCustomMapByIdAsync("map-1")).ReturnsAsync(map);
			customMaps.Setup(x => x.GetLayersForMapAsync("map-1")).ReturnsAsync(new List<IndoorMapFloor> { layer });
			customMaps.Setup(x => x.GetLayerByIdAsync("layer-1")).ReturnsAsync(layer);
			customMaps.Setup(x => x.GetRegionByIdAsync("z1")).ReturnsAsync(zone);
			customMaps.Setup(x => x.GetRegionsForLayerAsync("layer-1")).ReturnsAsync(new List<IndoorMapZone> { zone });
			customMaps.Setup(x => x.GetImportsForMapAsync("map-1")).ReturnsAsync(new List<CustomMapImport>());
			customMaps.Setup(x => x.SearchRegionsAsync(Dept, "gate")).ReturnsAsync(new List<IndoorMapZone> { zone });
			customMaps.Setup(x => x.GetRegionDisplayNameAsync("z1")).ReturnsAsync("Fairgrounds - Grounds - Gate A");
			customMaps.Setup(x => x.GetTileAsync("layer-1", 1, 0, 0)).ReturnsAsync(new CustomMapTile { TileData = new byte[] { 9, 9 }, TileContentType = "image/png" });
			customMaps.Setup(x => x.DeleteCustomMapAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			customMaps.Setup(x => x.DeleteLayerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			builder.Services.AddSingleton(customMaps.Object);
			var indoorMaps = new Mock<IIndoorMapService>();
			indoorMaps.Setup(x => x.GetFloorByIdAsync("layer-1")).ReturnsAsync(layer);
			indoorMaps.Setup(x => x.GetIndoorMapByIdAsync("map-1")).ReturnsAsync(map);
			builder.Services.AddSingleton(indoorMaps.Object);

			List<string> Writes() => customMaps.Invocations.Concat(indoorMaps.Invocations).Where(i => WriteMethods.Contains(i.Method.Name)).Select(i => i.Method.Name + "(" + i.Arguments[0] + ")").ToList();

			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
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
				async Task<HttpResponseMessage> PostJson(string url, string token, string json)
				{
					using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
					request.Headers.Add("RequestVerificationToken", token);
					return await client.SendAsync(request);
				}

				// ---- Admin pages: delete is a small POST form with the confirm prompt, never a GET link ----
				var response = await client.GetAsync("/User/CustomMaps");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-custom-maps-index.html"), html);
				var mapForm = Regex.Match(html, "<form class=\"custom-map-delete-form\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
				mapForm.Should().Contain("action=\"/User/CustomMaps/Delete\"").And.Contain("method=\"post\"").And.Contain("onsubmit=\"return confirm(")
					.And.Contain("name=\"id\" value=\"map-1\"").And.Contain("__RequestVerificationToken");
				html.Should().NotContain("href=\"/User/CustomMaps/Delete");
				var token = Token(mapForm);
				token.Should().NotBeNullOrEmpty();

				html = await client.GetStringAsync("/User/CustomMaps/Layers/map-1");
				var layerForm = Regex.Match(html, "<form class=\"custom-map-layer-delete-form\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
				layerForm.Should().Contain("action=\"/User/CustomMaps/DeleteLayer\"").And.Contain("method=\"post\"").And.Contain("onsubmit=\"return confirm(")
					.And.Contain("name=\"id\" value=\"layer-1\"").And.Contain("__RequestVerificationToken");
				html.Should().NotContain("href=\"/User/CustomMaps/DeleteLayer");

				// ---- GET deletes no longer work, and a POST without the antiforgery token is refused ----
				(await client.GetAsync("/User/CustomMaps/Delete/map-1")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
				(await client.GetAsync("/User/CustomMaps/DeleteLayer/layer-1")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
				response = await client.GetAsync("/User/IndoorMaps/Delete/map-1");
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, "the legacy indoor-maps link now lands on the list");
				response.Headers.Location!.OriginalString.Should().Contain("/User/CustomMaps").And.NotContain("Delete");
				(await Post("/User/CustomMaps/Delete", ("id", "map-1"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				(await Post("/User/CustomMaps/DeleteLayer", ("id", "layer-1"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				Writes().Should().BeEmpty();

				// ---- Members: every page and every write is refused; nothing is written ----
				client.DefaultRequestHeaders.Add("X-Member", "1");
				foreach (var page in new[] { "/User/CustomMaps", "/User/CustomMaps/New", "/User/CustomMaps/Edit/map-1", "/User/CustomMaps/Layers/map-1", "/User/CustomMaps/RegionEditor/layer-1", "/User/CustomMaps/Import/map-1" })
					(await client.GetAsync(page)).StatusCode.Should().Be(HttpStatusCode.Forbidden, page);

				(await Post("/User/CustomMaps/New", ("__RequestVerificationToken", token), ("Map.Name", "Member map"), ("Map.MapType", "2"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "New");
				(await Post("/User/CustomMaps/Edit", ("__RequestVerificationToken", token), ("Map.IndoorMapId", "map-1"), ("Map.Name", "Renamed"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Edit");
				(await Post("/User/CustomMaps/Delete", ("__RequestVerificationToken", token), ("id", "map-1"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Delete");
				(await Post("/User/CustomMaps/NewLayer", ("__RequestVerificationToken", token), ("indoorMapId", "map-1"), ("name", "Member layer"), ("floorOrder", "0"), ("layerType", "0"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "NewLayer");
				(await Post("/User/CustomMaps/DeleteLayer", ("__RequestVerificationToken", token), ("id", "layer-1"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "DeleteLayer");
				(await PostJson("/User/CustomMaps/SaveRegion", token, "{\"IndoorMapFloorId\":\"layer-1\",\"Name\":\"Member region\"}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "SaveRegion");
				(await PostJson("/User/CustomMaps/DeleteRegion", token, "{\"RegionId\":\"z1\"}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "DeleteRegion");
				(await Post("/User/CustomMaps/ImportUpload", ("__RequestVerificationToken", token), ("mapId", "map-1"), ("layerId", "layer-1"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "ImportUpload");
				(await Post("/User/CustomMaps/ClearRegions", ("__RequestVerificationToken", token), ("layerId", "layer-1"), ("expectedCount", "1"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "ClearRegions");
				(await Post("/User/IndoorMaps/DeleteFloor", ("__RequestVerificationToken", token), ("id", "layer-1"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the legacy floor delete removes the same layer row");
				Writes().Should().BeEmpty();

				// ---- Members keep what dispatch reads: region search and the layer image and tiles ----
				response = await client.GetAsync("/User/CustomMaps/SearchRegions?term=gate");
				response.StatusCode.Should().Be(HttpStatusCode.OK);
				(await response.Content.ReadAsStringAsync()).Should().Contain("Fairgrounds - Grounds - Gate A");
				response = await client.GetAsync("/User/CustomMaps/GetLayerImage/layer-1");
				response.StatusCode.Should().Be(HttpStatusCode.OK);
				(await response.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
				response = await client.GetAsync("/User/CustomMaps/GetLayerTile/layer-1?z=1&x=0&y=0");
				response.StatusCode.Should().Be(HttpStatusCode.OK);
				(await response.Content.ReadAsByteArrayAsync()).Should().Equal(9, 9);
				client.DefaultRequestHeaders.Remove("X-Member");

				// ---- Admin POSTs with the token delete ----
				response = await Post("/User/CustomMaps/DeleteLayer", ("__RequestVerificationToken", token), ("id", "layer-1"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().EndWith("/User/CustomMaps/Layers/map-1");
				response = await Post("/User/CustomMaps/Delete", ("__RequestVerificationToken", token), ("id", "map-1"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().EndWith("/User/CustomMaps");
				Writes().Should().Equal("DeleteLayerAsync(layer-1)", "DeleteCustomMapAsync(map-1)");
			}
			finally
			{
				await app.StopAsync();
				ClaimsAuthorizationHelper._httpContextAccessor = previous;
			}
		}
	}
}
