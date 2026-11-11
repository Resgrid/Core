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
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>The hydrant list's bulk removal form, through the real MVC pipeline (antiforgery, form limits, policies, view).</summary>
	[TestFixture, NonParallelizable]
	public class HydrantBulkDeleteFormTests
	{
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
				var claims = new[] { new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, "77"), new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View), new Claim(ResgridClaimTypes.Resources.Record, Request.Headers.ContainsKey("X-Viewer") ? "none" : ResgridClaimTypes.Actions.PreventionAdmin) };
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "hydrant-test")), "hydrant-test")));
			}
		}

		private sealed record BulkCall(HydrantBulkDeleteScope Scope, List<string> Ids, int? ExpectedCount);

		[Test]
		public async Task Large_selection_posts_through_antiforgery_and_form_limits_and_unconfirmed_posts_remove_nothing()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("hydrant-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("hydrant-test", _ => { });
			builder.Services.AddAuthorization(o => {
				o.AddPolicy(ResgridResources.Record_View, p => p.RequireClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View));
				o.AddPolicy(ResgridResources.Record_PreventionAdmin, p => p.RequireClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.PreventionAdmin));
			});
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(RecordHydrantsController).Assembly);

			var listed = new List<RmsHydrant>
			{
				new RmsHydrant { RmsHydrantId = "h-manual", DepartmentId = 77, HydrantNumber = "M-1", InService = true, Source = RmsHydrantSources.Manual },
				new RmsHydrant { RmsHydrantId = "h-csv", DepartmentId = 77, HydrantNumber = "C-1", InService = true, Source = "csv-import" },
				new RmsHydrant { RmsHydrantId = "h-json", DepartmentId = 77, HydrantNumber = "J-1", InService = true, Source = "json-import" }
			};
			var calls = new List<BulkCall>();
			var hydrants = new Mock<IRecordsHydrantsService>();
			hydrants.Setup(x => x.IsModuleEnabledAsync(77)).ReturnsAsync(true);
			hydrants.Setup(x => x.ListAsync(77, "manager")).ReturnsAsync(() => listed.ToList());
			hydrants.Setup(x => x.GetMapLayerAsync(77, "manager", null, null, null, null)).ReturnsAsync(new List<HydrantMapPoint>());
			hydrants.Setup(x => x.BulkDeleteAsync(77, "manager", It.IsAny<HydrantBulkDeleteScope>(), It.IsAny<IEnumerable<string>>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int department, string user, HydrantBulkDeleteScope scope, IEnumerable<string> ids, int? expected, CancellationToken token) =>
				{
					calls.Add(new BulkCall(scope, ids?.ToList() ?? new List<string>(), expected));
					if (expected == 12345) throw new ArgumentException("3 hydrants match this removal, not 12,345. Reload the hydrant list and confirm again.");
					return expected ?? 0;
				});
			builder.Services.AddSingleton(hydrants.Object);
			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(77, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = 77, FlagEnabled = true, Activated = true });
			builder.Services.AddSingleton(cutover.Object);
			var toggles = new Mock<IFeatureToggleService>();
			toggles.Setup(t => t.IsEnabledAsync(FeatureFlagKeys.RecordsPreventionHydrants, 77, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			builder.Services.AddSingleton(toggles.Object);
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

				var response = await client.GetAsync("/User/RecordHydrants/Index");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-hydrant-bulk-delete.html"), html);
				html.Should().Contain("id=\"hydrantBulkDeleteForm\"").And.Contain("id=\"hydrantSelectAll\"");
				Regex.Matches(html, "class=\"hydrant-select\" name=\"ids\" value=\"h-[a-z]+\" form=\"hydrantBulkDeleteForm\"").Count.Should().Be(3);
				var text = WebUtility.HtmlDecode(html);
				text.Should().Contain("All imported hydrants (2)").And.Contain("All hydrants (3)").And.Contain("Hydrants ticked in the list");
				Regex.IsMatch(html, "name=\"scope\" value=\"Selected\"[^>]*checked").Should().BeTrue("the default removes nothing until rows are ticked");
				var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
				token.Should().NotBeNullOrEmpty();

				// No antiforgery token: refused before the action.
				response = await client.PostAsync("/User/RecordHydrants/BulkDelete", new FormUrlEncodedContent(new Dictionary<string, string> { ["scope"] = "All", ["expectedCount"] = "3" }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				calls.Should().BeEmpty();

				// 9,000 ticked rows: one "ids" value each, far past the default 1,024-value form limit.
				var ids = Enumerable.Range(1, 9000).Select(i => "hydrant-" + i).ToList();
				var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("scope", "Selected"), new("expectedCount", "9000") };
				form.AddRange(ids.Select(id => new KeyValuePair<string, string>("ids", id)));
				response = await client.PostAsync("/User/RecordHydrants/BulkDelete", new FormUrlEncodedContent(form));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().EndWith("/User/RecordHydrants");
				calls.Should().ContainSingle();
				calls[0].Scope.Should().Be(HydrantBulkDeleteScope.Selected);
				calls[0].Ids.Should().Equal(ids);
				calls[0].ExpectedCount.Should().Be(9000);
				WebUtility.HtmlDecode(await client.GetStringAsync("/User/RecordHydrants/Index")).Should().Contain("9,000 hydrants removed.");

				// The page always asks for the typed count; a post without it removes nothing.
				response = await client.PostAsync("/User/RecordHydrants/BulkDelete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["scope"] = "All" }));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect);
				calls.Should().HaveCount(1);
				WebUtility.HtmlDecode(await client.GetStringAsync("/User/RecordHydrants/Index")).Should().Contain("Type the number of hydrants to remove to confirm.");

				// A count the service refuses comes back as the page error.
				response = await client.PostAsync("/User/RecordHydrants/BulkDelete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["scope"] = "All", ["expectedCount"] = "12345" }));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect);
				calls.Should().HaveCount(2);
				calls[1].Scope.Should().Be(HydrantBulkDeleteScope.All);
				WebUtility.HtmlDecode(await client.GetStringAsync("/User/RecordHydrants/Index")).Should().Contain("not 12,345. Reload the hydrant list");

				// A records viewer (not a prevention admin) is refused and never sees the form.
				client.DefaultRequestHeaders.Add("X-Viewer", "1");
				response = await client.PostAsync("/User/RecordHydrants/BulkDelete", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["scope"] = "All", ["expectedCount"] = "3" }));
				response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
				calls.Should().HaveCount(2);
				(await client.GetStringAsync("/User/RecordHydrants/Index")).Should().NotContain("hydrantBulkDeleteForm").And.NotContain("hydrant-select");
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}
