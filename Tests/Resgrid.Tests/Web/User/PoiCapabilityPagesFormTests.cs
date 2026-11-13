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
	/// The POI capability and destination availability pages through the real MVC pipeline: the views render, the forms carry
	/// their antiforgery tokens, ticked checkboxes bind, and a dispatcher (Call create, not a map administrator) can open the
	/// destinations page and set availability but not manage capabilities.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PoiCapabilityPagesFormTests
	{
		private const int Dept = 77;
		private const int TypeId = 40;
		private const int HospitalId = 1;

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
				// NameIdentifier pins the antiforgery claim uid, so a token issued to the administrator also validates for the dispatcher.
				var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "manager"), new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) };
				if (Request.Headers.ContainsKey("X-Dispatcher"))
					claims.Add(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
				else
					claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "poi-test")), "poi-test")));
			}
		}

		private static string Token(string html)
			=> WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

		private sealed record AvailabilityCall(int PoiId, PoiAvailabilityStatuses Status, List<string> Unavailable, string Note, DateTime? ExpiresOn);

		[Test]
		public async Task Capability_and_availability_pages_render_and_post_through_antiforgery_and_the_role_gates()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs(); builder.Services.AddWebOptimizer();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("poi-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("poi-test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(MappingController).Assembly);

			var capabilities = new List<PoiCapability>
			{
				new PoiCapability { PoiCapabilityId = "cath", DepartmentId = Dept, Name = "Cath Lab", Sort = 1 },
				new PoiCapability { PoiCapabilityId = "stroke", DepartmentId = Dept, Name = "Stroke Center", Sort = 2 }
			};
			var type = new PoiType { PoiTypeId = TypeId, DepartmentId = Dept, Name = "Hospitals", IsDestination = true, Color = "#cc0000" };
			var hospital = new Poi { PoiId = HospitalId, PoiTypeId = TypeId, Name = "UZ Gent", Address = "Corneel Heymanslaan 10", Type = type };
			type.Pois = new List<Poi> { hospital };
			var report = new PoiAvailability { PoiId = HospitalId, DepartmentId = Dept, Status = (int)PoiAvailabilityStatuses.Diversion, UnavailableCapabilityIds = "cath", Note = "ED <full>", SetOn = DateTime.UtcNow, ExpiresOn = DateTime.UtcNow.AddHours(2) };

			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetPoiCapabilitiesForDepartmentAsync(Dept)).ReturnsAsync(() => capabilities.Select(c => new PoiCapability { PoiCapabilityId = c.PoiCapabilityId, DepartmentId = c.DepartmentId, Name = c.Name, Sort = c.Sort }).ToList());
			mapping.Setup(x => x.GetTypeByIdAsync(TypeId)).ReturnsAsync(type);
			mapping.Setup(x => x.GetPOIsForDepartmentAsync(Dept)).ReturnsAsync(() => new List<Poi> { hospital });
			mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(Dept)).ReturnsAsync(() => new List<Poi> { hospital });
			mapping.Setup(x => x.GetDestinationPOIByIdAsync(Dept, HospitalId)).ReturnsAsync(() => hospital);
			mapping.Setup(x => x.PopulatePoiDetailsAsync(Dept, It.IsAny<IEnumerable<Poi>>())).Callback((int _, IEnumerable<Poi> pois) =>
			{
				foreach (var p in pois.Where(p => p.PoiId == HospitalId))
				{
					p.CapabilityIds = new List<string> { "cath", "stroke" };
					p.Capabilities = new List<string> { "Cath Lab", "Stroke Center" };
					p.Availability = report;
				}
			}).Returns(Task.CompletedTask);
			var savedCapabilities = new List<PoiCapability>();
			mapping.Setup(x => x.SavePoiCapabilityAsync(It.IsAny<PoiCapability>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PoiCapability c, CancellationToken _) => { savedCapabilities.Add(c); return c; });
			var availabilityCalls = new List<AvailabilityCall>();
			mapping.Setup(x => x.SetPoiAvailabilityAsync(Dept, It.IsAny<int>(), It.IsAny<PoiAvailabilityStatuses>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(),
					It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int poiId, PoiAvailabilityStatuses status, IEnumerable<string> ids, string note, DateTime? expires, string user, CancellationToken _) =>
				{
					availabilityCalls.Add(new AvailabilityCall(poiId, status, ids?.ToList() ?? new List<string>(), note, expires));
					return null;
				});
			builder.Services.AddSingleton(mapping.Object);

			var departmentSettings = new Mock<IDepartmentSettingsService>();
			departmentSettings.Setup(x => x.GetMapConfigForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new Resgrid.Config.ResolvedMapConfig());
			builder.Services.AddSingleton(departmentSettings.Object);
			builder.Services.AddSingleton(Mock.Of<IRecordsHydrantsService>());
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
				async Task<string> Page(string url)
				{
					var response = await client.GetAsync(url);
					var body = await response.Content.ReadAsStringAsync();
					response.StatusCode.Should().Be(HttpStatusCode.OK, body);
					return body;
				}

				// ---- Map administrator: capabilities page and the POI form ----

				var html = await Page("/User/Mapping/PoiCapabilities");
				html.Should().Contain("id=\"addPoiCapabilityForm\"").And.Contain("value=\"Cath Lab\"").And.Contain("value=\"Stroke Center\"");
				WebUtility.HtmlDecode(html).Should().Contain("Delete the capability \"Cath Lab\"? It is removed from every POI that offers it (1)");
				var token = Token(html);
				token.Should().NotBeNullOrEmpty();

				(await Post("/User/Mapping/SavePoiCapability", ("name", "Burns Unit"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				savedCapabilities.Should().BeEmpty();

				var saved = await Post("/User/Mapping/SavePoiCapability", ("__RequestVerificationToken", token), ("name", "Burns Unit"), ("sort", ""));
				saved.StatusCode.Should().Be(HttpStatusCode.Redirect, await saved.Content.ReadAsStringAsync());
				saved.Headers.Location!.OriginalString.Should().Contain("/User/Mapping/PoiCapabilities");
				savedCapabilities.Should().ContainSingle().Which.Should().Match<PoiCapability>(c => c.Name == "Burns Unit" && c.DepartmentId == Dept && c.PoiCapabilityId == null && c.Sort == 0);

				var form = await Page("/User/Mapping/AddPOI?poiTypeId=" + TypeId);
				form.Should().Contain("name=\"SelectedCapabilityIds\" value=\"cath\"").And.Contain("name=\"SelectedCapabilityIds\" value=\"stroke\"");

				// ---- Dispatcher: destinations and availability, but not capability management ----

				client.DefaultRequestHeaders.Add("X-Dispatcher", "1");
				var destinations = WebUtility.HtmlDecode(await Page("/User/Mapping/Destinations"));
				destinations.Should().Contain("Destination Availability").And.Contain("UZ Gent").And.Contain("Cath Lab, Stroke Center")
					.And.Contain("Diversion").And.Contain("ED <full>").And.Contain("SetPoiAvailability?poiId=" + HospitalId);
				destinations.Should().NotContain("/User/Mapping/PoiCapabilities", "capability management is for map administrators");

				var availabilityForm = await Page("/User/Mapping/SetPoiAvailability?poiId=" + HospitalId);
				availabilityForm.Should().Contain("id=\"setPoiAvailabilityForm\"").And.Contain("name=\"UnavailableCapabilityIds\" value=\"cath\" checked=\"checked\"")
					.And.Contain("value=\"-1\" selected=\"selected\"").And.Contain("ED &lt;full&gt;");

				var posted = await Post("/User/Mapping/SetPoiAvailability", ("__RequestVerificationToken", Token(availabilityForm)), ("PoiId", HospitalId.ToString()),
					("Status", "1"), ("UnavailableCapabilityIds", "cath"), ("UnavailableCapabilityIds", "stroke"), ("Note", "CT down"), ("ExpiresInHours", "2"));
				posted.StatusCode.Should().Be(HttpStatusCode.Redirect, await posted.Content.ReadAsStringAsync());
				posted.Headers.Location!.OriginalString.Should().Contain("/User/Mapping/Destinations");
				availabilityCalls.Should().ContainSingle();
				availabilityCalls[0].Should().Match<AvailabilityCall>(c => c.PoiId == HospitalId && c.Status == PoiAvailabilityStatuses.Limited && c.Note == "CT down" && c.ExpiresOn.HasValue);
				availabilityCalls[0].Unavailable.Should().Equal("cath", "stroke");

				(await Post("/User/Mapping/SetPoiAvailability", ("PoiId", HospitalId.ToString()), ("Status", "3"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				availabilityCalls.Should().HaveCount(1);

				(await client.GetAsync("/User/Mapping/PoiCapabilities")).Headers.Location!.OriginalString.Should().Contain("/Public/Unauthorized");
				(await Post("/User/Mapping/SavePoiCapability", ("__RequestVerificationToken", token), ("name", "Dispatcher's"))).Headers.Location!.OriginalString.Should().Contain("/Public/Unauthorized");
				savedCapabilities.Should().HaveCount(1);
			}
			finally
			{
				await app.StopAsync();
				ClaimsAuthorizationHelper._httpContextAccessor = previous;
			}
		}
	}
}
