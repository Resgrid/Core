using System;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	// Render the production form bodies with small models, including null CallId through the nested partial.
	[Area("User")]
	public class CallPickerRenderingController : Controller
	{
		public IActionResult Open(int? callId) => PartialView("/Areas/User/Views/RecordInvestigations/Open.cshtml", new RecordInvestigationOpenView { CallId = callId });
		public IActionResult Incident(int? callId) => PartialView("/Areas/User/Views/IncidentReports/Index.cshtml", new IncidentReportsIndexView {
			SelectedCallId = callId, Department = new Department { DepartmentId = 77, TimeZone = "UTC" },
			ModuleState = new RecordsModuleState { FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active }
		});
		public IActionResult Report(int? callId) => PartialView("/Areas/User/Views/Records/Edit.cshtml", new RecordEditView {
			CallId = callId, DefinitionKey = RmsDefinitionKeys.Run, RecordType = RmsOperationalRecordType.Run,
			ParticipantRows = new System.Collections.Generic.List<RecordParticipantEditRow>(),
			Department = new Department { DepartmentId = 77, TimeZone = "UTC" }
		});
		public IActionResult Definition(int? callId) => PartialView("/Areas/User/Views/Records/EditDefinition.cshtml", new RecordDefinitionFormView {
			CallId = callId, DefinitionKey = "custom", DefinitionName = "Custom report", Department = new Department { DepartmentId = 77, TimeZone = "UTC" }
		});
	}

	[TestFixture, NonParallelizable]
	public class RecordCallPickerRenderingTests
	{
		[Test]
		public async Task Case_and_report_forms_render_optional_required_and_preselected_call_pickers()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs();
			builder.Services.AddWebOptimizer();
			builder.Services.AddSingleton(Moq.Mock.Of<Resgrid.Model.Services.IUdfRenderingService>());
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddControllersWithViews().AddRazorOptions(o => o.AreaViewLocationFormats.Add("/Areas/User/Views/Records/{0}.cshtml"))
				.AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.RecordCallsController).Assembly)
				.AddApplicationPart(typeof(CallPickerRenderingController).Assembly);
			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => {
				context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "author"), new Claim(ClaimTypes.PrimaryGroupSid, "77"), new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View), new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create) }, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				foreach (var action in new[] { "Open", "Report", "Definition", "Incident" })
					foreach (var callId in new[] { "", "9" })
					{
						var response = await client.GetAsync("/User/CallPickerRendering/" + action + "?callId=" + callId);
						var html = await response.Content.ReadAsStringAsync();
						response.StatusCode.Should().Be(HttpStatusCode.OK, html);
						html.Should().Contain("data-call-picker").And.Contain("Browse calls").And.Contain("name=\"CallId\"").And.Contain("data-call-from").And.Contain("data-call-next");
						if (callId == "9") html.Should().Contain("value=\"9\" selected");
						await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "resgrid-call-picker-" + action + "-" + (callId == "" ? "empty" : callId) + ".html"), html);
					}
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; await app.StopAsync(); }
		}
	}
}
