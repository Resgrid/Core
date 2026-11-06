using System;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[Area("User")]
	public class LegacyLogsNavigationRenderingController : Controller
	{
		public IActionResult Sidebar() => PartialView("/Areas/User/Views/Shared/_Navigation.cshtml");
	}

	/// <summary>
	/// The production sidebar across the Records cutover. The Records.System flag only shows the Records group; the
	/// department's cutover decides where Logs lives. Before activation Logs is still the working log system and
	/// keeps its own link; after activation the old Logs stay reachable from inside the Records group.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LegacyLogsNavigationRenderingTests
	{
		private const string TopLevelLogs = "data-i18n=\"nav.logs\"";

		[Test]
		public async Task Logs_follow_the_cutover_not_the_records_flag()
		{
			var state = new RecordsModuleState { DepartmentId = 77 };
			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => state);
			var modules = new DepartmentModuleSettings();
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => modules);

			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs();
			builder.Services.AddWebOptimizer();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			// The flag service agrees with the module state, as it does in production (the state reads the flag).
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<System.Collections.Generic.IDictionary<string, string>>()))
				.ReturnsAsync(() => state.FlagEnabled);
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.ChatSystem, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<System.Collections.Generic.IDictionary<string, string>>()))
				.ReturnsAsync(true);
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.Deployments, It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<System.Collections.Generic.IDictionary<string, string>>()))
				.ReturnsAsync(true);
			builder.Services.AddSingleton(cutover.Object);
			builder.Services.AddSingleton(flags.Object);
			builder.Services.AddSingleton(Mock.Of<IReadinessAccessService>());
			builder.Services.AddSingleton(Mock.Of<IRecordsAuthorizationService>());
			builder.Services.AddSingleton(Mock.Of<IBusinessOperationsAccessService>());
			builder.Services.AddSingleton(Mock.Of<IIncidentBackOfficeAccessService>());
			builder.Services.AddControllersWithViews()
				.AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.LogsController).Assembly)
				.AddApplicationPart(typeof(LegacyLogsNavigationRenderingController).Assembly);
			await using var app = builder.Build();

			// SettingsHelper resolves the module settings through the service locator and caches the instance statically.
			var settingsField = typeof(SettingsHelper).GetField("_departmentSettingsService", BindingFlags.NonPublic | BindingFlags.Static);
			var previousSettings = settingsField!.GetValue(null);
			settingsField.SetValue(null, settings.Object);
			var previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => {
				context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "member"), new Claim(ClaimTypes.PrimaryGroupSid, "77"),
					new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View), new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View),
					new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View), new Claim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View) }, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				async Task<string> Render()
				{
					var response = await client.GetAsync("/User/LegacyLogsNavigationRendering/Sidebar");
					var html = await response.Content.ReadAsStringAsync();
					response.StatusCode.Should().Be(HttpStatusCode.OK, html);
					return html;
				}

				// Records not offered: the plain Logs link, as always.
				var html = await Render();
				html.Should().Contain(TopLevelLogs).And.Contain("href=\"/User/Logs\"").And.NotContain("id=\"records-menu\"");

				// Flag on, not yet activated: Records shows up (an administrator activates from there), but Logs is
				// still the department's working log system and must keep its link.
				state = new RecordsModuleState { DepartmentId = 77, FlagEnabled = true };
				html = await Render();
				html.Should().Contain("id=\"records-menu\"");
				html.Should().Contain(TopLevelLogs, "a department that has not activated Records still uses Logs");
				RecordsMenu(html).Should().NotContain("href=\"/User/Logs\"");

				// A clean revert puts the department back on Logs the same way.
				state = new RecordsModuleState { DepartmentId = 77, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Reverted };
				(await Render()).Should().Contain(TopLevelLogs);

				// Activated: Records replaces the Logs entry, and the old Logs are reachable from inside the Records group.
				state = new RecordsModuleState { DepartmentId = 77, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true };
				html = await Render();
				html.Should().NotContain(TopLevelLogs);
				RecordsMenu(html).Should().Contain("href=\"/User/Logs\"").And.Contain("Existing logs");

				// Logs and Records have separate module switches. Turning Logs off used to hide Records too; now it only
				// takes the old Logs out of the Records group.
				modules = new DepartmentModuleSettings { LogsDisabled = true };
				html = await Render();
				html.Should().Contain("id=\"records-menu\"", "the Logs switch no longer hides Records").And.NotContain(TopLevelLogs);
				RecordsMenu(html).Should().NotContain("href=\"/User/Logs\"");
				state = new RecordsModuleState { DepartmentId = 77, FlagEnabled = true };
				(await Render()).Should().Contain("id=\"records-menu\"").And.NotContain(TopLevelLogs);

				// Records switched off: no Records group. Once activated, the read-only Logs keep their own link.
				modules = new DepartmentModuleSettings { RecordsDisabled = true };
				(await Render()).Should().NotContain("id=\"records-menu\"").And.Contain(TopLevelLogs);
				state = new RecordsModuleState { DepartmentId = 77, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true };
				(await Render()).Should().NotContain("id=\"records-menu\"").And.Contain(TopLevelLogs).And.Contain("href=\"/User/Logs\"");
				modules = new DepartmentModuleSettings();

				// Activated and then the flag turned off: no Records group, so the (read-only) Logs link comes back.
				state = new RecordsModuleState { DepartmentId = 77, FlagEnabled = false, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true };
				html = await Render();
				html.Should().Contain(TopLevelLogs).And.NotContain("id=\"records-menu\"");

				// The newer modules' visibility switches each hide their own entry and nothing else.
				var entries = new (string Href, Action<DepartmentModuleSettings> Hide)[]
				{
					("href=\"/User/Contacts\"", m => m.ContactsDisabled = true),
					("href=\"/User/Certifications\"", m => m.CertificationsDisabled = true),
					("href=\"/User/Deployments\"", m => m.DeploymentsDisabled = true),
					("href=\"/User/Chat\"", m => m.ChatDisabled = true)
				};
				modules = new DepartmentModuleSettings();
				html = await Render();
				foreach (var entry in entries)
					html.Should().Contain(entry.Href, "every module is shown by default");
				foreach (var entry in entries)
				{
					modules = new DepartmentModuleSettings();
					entry.Hide(modules);
					html = await Render();
					html.Should().NotContain(entry.Href);
					foreach (var other in entries.Where(e => e.Href != entry.Href))
						html.Should().Contain(other.Href, "only the switched-off module is hidden");
				}
			}
			finally
			{
				settingsField.SetValue(null, previousSettings);
				ClaimsAuthorizationHelper._httpContextAccessor = previousAccessor;
				await app.StopAsync();
			}
		}

		private static string RecordsMenu(string html)
		{
			var start = html.IndexOf("id=\"records-menu\"", StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, "the Records group should render");
			var end = html.IndexOf("</ul>", start, StringComparison.Ordinal);
			return html.Substring(start, end - start);
		}
	}
}
