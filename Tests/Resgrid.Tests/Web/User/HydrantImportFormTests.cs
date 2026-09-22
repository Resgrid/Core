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
using Resgrid.Model.Services;
using Resgrid.Model.Providers;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public class HydrantImportFormTests
	{
		private sealed class PageBodyFilter : IResultFilter
		{
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is ViewResult view)
					context.Result = new PartialViewResult { ViewName = "/Areas/User/Views/RecordHydrants/" + (view.ViewName ?? context.RouteData.Values["action"]) + ".cshtml", ViewData = view.ViewData, TempData = view.TempData };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}

		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				var claims = new[] { new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, "77"), new Claim(ResgridClaimTypes.Resources.Record, Request.Headers.ContainsKey("X-NoRecords") ? "none" : ResgridClaimTypes.Actions.View), new Claim(ResgridClaimTypes.Resources.Record, Request.Headers.ContainsKey("X-Viewer") ? "none" : ResgridClaimTypes.Actions.PreventionAdmin) };
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "hydrant-test")), "hydrant-test")));
			}
		}

		[Test]
		public async Task File_and_paste_imports_validate_and_enforce_csrf_admin_and_module_access()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("hydrant-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("hydrant-test", _ => { });
			builder.Services.AddAuthorization(o => {
				o.AddPolicy(ResgridResources.Record_View, p => p.RequireClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View));
				o.AddPolicy(ResgridResources.Record_PreventionAdmin, p => p.RequireClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.PreventionAdmin));
			});
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(RecordHydrantsController).Assembly);
			var hydrants = new Mock<IRecordsHydrantsService>();
			var attempts = 0;
			hydrants.Setup(x => x.IsModuleEnabledAsync(77)).ReturnsAsync(true);
			hydrants.Setup(x => x.ImportAsync(77, "manager", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int department, string user, string content, string format, CancellationToken token) => {
					attempts++;
					var batch = HydrantImportParser.Parse(content, format);
					if (!batch.Result.ValidationFailed) batch.Result.Created = batch.Rows.Count;
					return batch.Result;
				});
			builder.Services.AddSingleton(hydrants.Object);
			hydrants.Setup(x => x.GetMapLayerAsync(77, "manager", null, null, null, null)).ReturnsAsync(new List<HydrantMapPoint> { new HydrantMapPoint { HydrantId = "hydrant", PoiId = 10, HydrantNumber = "H-10", InService = true } });
			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetTypeByIdAsync(1)).ReturnsAsync(new PoiType { DepartmentId = 77, PoiTypeId = 1, Pois = new List<Poi> { new Poi { PoiId = 10, Name = "Linked hydrant" }, new Poi { PoiId = 20, Name = "Unrelated landmark" } } });
			builder.Services.AddSingleton(mapping.Object);
			builder.Services.AddSingleton(Mock.Of<IDepartmentSettingsService>());
			builder.Services.AddSingleton(Mock.Of<IGeoLocationProvider>());
			builder.Services.AddSingleton(Mock.Of<ICallsService>());
			builder.Services.AddSingleton(Mock.Of<IDepartmentGroupsService>());
			builder.Services.AddSingleton(Mock.Of<IActionLogsService>());
			builder.Services.AddSingleton(Mock.Of<IUnitsService>());
			builder.Services.AddSingleton(Mock.Of<IKmlProvider>());
			builder.Services.AddSingleton(Mock.Of<IPermissionsService>());
			builder.Services.AddSingleton(Mock.Of<IPersonnelRolesService>());
			builder.Services.AddSingleton(Mock.Of<IProtectedReadService>());
			builder.Services.AddSingleton(Mock.Of<IRecordsCutoverService>());
			builder.Services.AddSingleton(Mock.Of<IFeatureToggleService>());
			builder.Services.AddSingleton(Mock.Of<IDepartmentsService>(d => d.GetDepartmentByIdAsync(77, false) == Task.FromResult(new Department { DepartmentId = 77, TimeZone = "Pacific Standard Time" })));
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
				var response = await client.GetAsync("/User/RecordHydrants/Import");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				html.Should().Contain("multipart/form-data").And.Contain("Download JSON example").And.Contain("Download CSV example");
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-hydrant-import.html"), html);
				var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
				token.Should().NotBeNullOrEmpty();
				response = await client.PostAsync("/User/RecordHydrants/Import", new FormUrlEncodedContent(new Dictionary<string, string> { ["Content"] = "[]" }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				attempts.Should().Be(0);
				const string invalid = "[{\"number\":\"<script>test</script>\",\"latitude\":\"wrong\",\"longitude\":2}]";
				response = await client.PostAsync("/User/RecordHydrants/Import", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Content"] = invalid, ["Format"] = "json" }));
				html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				WebUtility.HtmlDecode(html).Should().Contain("No hydrants were saved").And.Contain("latitude").And.Contain(invalid);
				html.Should().NotContain("<script>test</script>");
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-hydrant-import-errors.html"), html);
				async Task<string> Upload(string name, string data, string pasted = null)
				{
					using var form = new MultipartFormDataContent();
					form.Add(new StringContent(token), "__RequestVerificationToken");
					form.Add(new StringContent("csv"), "Format");
					form.Add(new StringContent(data), "file", name);
					if (pasted != null) form.Add(new StringContent(pasted), "Content");
					var r = await client.PostAsync("/User/RecordHydrants/Import", form);
					var body = await r.Content.ReadAsStringAsync();
					r.StatusCode.Should().Be(HttpStatusCode.OK, body);
					return WebUtility.HtmlDecode(body);
				}
				(await Upload("hydrants.json", HydrantImportParser.JsonExample)).Should().Contain("1 created");
				(await Upload("hydrants.csv", HydrantImportParser.CsvExample)).Should().Contain("1 created");
				(await Upload("hydrants.json", "bad-json")).Should().Contain("Invalid JSON at line").And.Contain("Choose the corrected file again");
				(await Upload("hydrants.json", "")).Should().Contain("file is empty");
				(await Upload("hydrants.exe", "[]")).Should().Contain("Choose a .json or .csv");
				(await Upload("hydrants.json", "[]", "[]")).Should().Contain("not both");
				(await Upload("hydrants.json", new string(' ', 1024 * 1024 + 10) + HydrantImportParser.JsonExample)).Should().Contain("1 created");
				(await Upload("hydrants.json", new string('x', 10 * 1024 * 1024 + 1))).Should().Contain("exceeds 10 MB");
				var example = await client.GetStringAsync("/User/RecordHydrants/ImportExample?format=json");
				HydrantImportParser.Parse(example, "json").Result.ValidationFailed.Should().BeFalse();
				(await client.GetStringAsync("/User/Mapping/GetPoisForType?poiTypeId=1")).Should().Contain("Unrelated landmark").And.NotContain("Linked hydrant");
				(await client.GetStringAsync("/User/RecordHydrants/MapLayer")).Should().Contain("H-10");
				client.DefaultRequestHeaders.Add("X-NoRecords", "1");
				(await client.GetAsync("/User/RecordHydrants/MapLayer")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				(await client.GetStringAsync("/User/Mapping/GetPoisForType?poiTypeId=1")).Should().Contain("Linked hydrant");
				client.DefaultRequestHeaders.Remove("X-NoRecords");
				client.DefaultRequestHeaders.Add("X-Viewer", "1");
				(await client.GetAsync("/User/RecordHydrants/Import")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				client.DefaultRequestHeaders.Remove("X-Viewer");
				hydrants.Setup(x => x.IsModuleEnabledAsync(77)).ReturnsAsync(false);
				(await client.GetAsync("/User/RecordHydrants/Import")).StatusCode.Should().Be(HttpStatusCode.NotFound);
				(await client.GetStringAsync("/User/Mapping/GetPoisForType?poiTypeId=1")).Should().Contain("Linked hydrant");
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}
