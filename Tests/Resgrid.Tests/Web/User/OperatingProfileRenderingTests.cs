using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	// Renders the production operating profile form body with a representative profile and picker options.
	[Area("User")]
	public class OperatingProfileRenderingController : Controller
	{
		public IActionResult Show()
		{
			// Admin Assist field help is matched to the owning screen's route, so render as that screen.
			RouteData.Values["controller"] = "Department";
			RouteData.Values["action"] = "OperatingProfile";
			ViewData["DepartmentTimeZone"] = "Pacific Standard Time";
			ViewData["AdminAssistAvailable"] = true;
			ViewData["ProfileGroups"] = new List<SelectListItem> { new("Station 1", "12"), new("Station 2", "13") };
			var policies = new SelectListGroup { Name = "Policies" };
			ViewData["ProfileDocuments"] = new List<SelectListItem> { new("Loose note", "39"), new("Staffing policy", "40") { Group = policies }, new("Qualification policy", "41") { Group = policies } };
			return PartialView("/Areas/User/Views/Department/OperatingProfile.cshtml", new DepartmentOperatingProfile
			{
				Revision = 4, Archetypes = new() { "fire", "ems" }, WorkforceMix = "combination", DeclaredMemberCount = 48, DispatchModel = "central",
				OperatingHours = "seasonal", MutualAid = true, ExpectedEmailPollIntervalMinutes = 15, SeasonStartMonthDay = "11-15", SeasonEndMonthDay = "02-29",
				LanguageCodes = new() { "en", "es" }, AccessibilityNeeds = new() { "large-text" }, SiteGroupReferences = new() { "12", "99" },
				StaffingPolicyReferences = new() { "40" }, AuthoritativeSystemReferences = new() { "cad-primary" }
			});
		}
	}

	[TestFixture, NonParallelizable]
	public class OperatingProfileRenderingTests
	{
		[Test]
		public async Task Profile_form_uses_pickers_and_number_inputs_and_keeps_Admin_Assist_field_help()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddWebOptimizer();
			// The real catalog and an allowing access check, so asp-for editors carry their Admin Assist field help.
			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			builder.Services.AddSingleton<IAdminAssistCatalog>(new Resgrid.AdminAssist.ConfigurationCatalog());
			builder.Services.AddSingleton(access.Object);
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddControllersWithViews()
				.AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.DepartmentController).Assembly)
				.AddApplicationPart(typeof(OperatingProfileRenderingController).Assembly);
			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => {
				context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "admin"), new Claim(ClaimTypes.PrimaryGroupSid, "77") }, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

				var response = await client.GetAsync("/User/OperatingProfileRendering/Show");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-operating-profile.html"), html);

				// Seasons: month and day pickers, preselected, with days a month cannot have unavailable.
				html.Should().Contain("name=\"SeasonStartMonth\"").And.Contain("name=\"SeasonStartDay\"").And.Contain("name=\"SeasonEndMonth\"").And.Contain("name=\"SeasonEndDay\"");
				html.Should().Contain("<option value=\"11\" selected=\"selected\">November</option>").And.Contain("<option value=\"15\" selected=\"selected\">15</option>");
				html.Should().Contain("<option value=\"02\" selected=\"selected\">February</option>").And.Contain("<option value=\"29\" selected=\"selected\">29</option>");
				html.Should().Contain("<option value=\"30\" disabled=\"disabled\" hidden=\"hidden\">30</option>");
				html.Should().NotContain("MM-DD").And.NotContain("name=\"SeasonStartMonthDay\"");

				// Numbers: numeric inputs with their range, and no unobtrusive validation to switch off the browser's own checks.
				html.Should().MatchRegex("<input[^>]*type=\"number\"[^>]*min=\"0\" max=\"1000000\" step=\"1\"[^>]*name=\"DeclaredMemberCount\"[^>]*value=\"48\"");
				html.Should().MatchRegex("<input[^>]*type=\"number\"[^>]*min=\"1\" max=\"10080\" step=\"1\"[^>]*name=\"ExpectedEmailPollIntervalMinutes\"[^>]*value=\"15\"");
				html.Should().NotContain("data-val=\"true\"");
				html.Should().Contain("name=\"Revision\" value=\"4\"");

				// References: pickers of department groups and documents, grouped by category, keeping a stale choice removable.
				html.Should().MatchRegex("<select id=\"SiteGroupReferences\" name=\"SiteGroupReferences\" multiple");
				html.Should().Contain("<option value=\"12\" selected=\"selected\">Station 1</option>").And.Contain("<option value=\"13\">Station 2</option>");
				html.Should().Contain("<option value=\"99\" selected>#99 (no longer available)</option>");
				html.Should().Contain("<optgroup label=\"Policies\">").And.Contain("<option value=\"40\" selected=\"selected\">Staffing policy</option>");
				html.Should().Contain("<option value=\"cad-primary\" selected>cad-primary</option>");
				html.Should().NotContain("Saving adds another optional row");

				// Choices and Admin Assist field help.
				html.Should().Contain("value=\"fire\" checked=\"checked\"").And.Contain("value=\"large-text\" checked=\"checked\"");
				html.Should().Contain("admin-assist-field-help").And.Contain("data-aa-field=\"profile.WorkforceMix\"");

				// An Admin Assist link to a picker field highlights and focuses it.
				var linked = await (await client.GetAsync("/User/OperatingProfileRendering/Show?aa=SeasonStartMonthDay")).Content.ReadAsStringAsync();
				linked.Should().Contain("form-group admin-assist-field-highlight").And.MatchRegex("<select id=\"SeasonStartMonth\"[^>]*autofocus=\"autofocus\"");
				await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-operating-profile-linked.html"), linked);
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; await app.StopAsync(); }
		}
	}
}
