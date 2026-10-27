using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HtmlAgilityPack;
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
	// Renders a production settings screen as its owning route, with a default model whose lists and nested objects are filled.
	[Area("User")]
	public class AdminAssistFieldHelpLayoutController : Controller
	{
		public IActionResult Show(string view, string model, string c, string a)
		{
			// Admin Assist field help is matched to the owning screen's route, so render as that screen.
			RouteData.Values["controller"] = c;
			RouteData.Values["action"] = a;
			ViewBag.Countries = new SelectList(new[] { "United States" });
			ViewBag.TimeZones = new SelectList(new[] { "Pacific Standard Time" });
			ViewBag.Users = new SelectList(new[] { "Admin" });
			var type = typeof(Resgrid.Web.Areas.User.Controllers.DepartmentController).Assembly.GetType(model, true);
			return PartialView($"/Areas/User/Views/{view}.cshtml", Fill(Activator.CreateInstance(type), 0));
		}

		// Null lists become empty (select lists get one option) and null Resgrid objects are created, so every section renders.
		private static object Fill(object o, int depth)
		{
			if (o == null || depth > 3) return o;
			foreach (var p in o.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
			{
				var t = p.PropertyType;
				var value = p.GetValue(o);
				if (value != null) { if (t.IsClass && t != typeof(string) && t.Namespace?.StartsWith("Resgrid") == true) Fill(value, depth + 1); continue; }
				object created = null;
				if (t == typeof(string)) continue;
				if (typeof(SelectList).IsAssignableFrom(t)) created = new SelectList(new[] { "Option" });
				else if (t.IsAssignableFrom(typeof(List<SelectListItem>))) created = new List<SelectListItem> { new("Option", "1") };
				else if (t.IsArray) created = Array.CreateInstance(t.GetElementType()!, 0);
				else if (t.IsGenericType && t.GetGenericArguments().Length == 1 && t.IsAssignableFrom(typeof(List<>).MakeGenericType(t.GetGenericArguments())))
					created = Activator.CreateInstance(typeof(List<>).MakeGenericType(t.GetGenericArguments()));
				else if (t.IsGenericType && t.GetGenericArguments().Length == 2 && t.IsAssignableFrom(typeof(Dictionary<,>).MakeGenericType(t.GetGenericArguments())))
					created = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(t.GetGenericArguments()));
				else if (t.IsClass && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null && t.Namespace?.StartsWith("Resgrid") == true)
					created = Fill(Activator.CreateInstance(t), depth + 1);
				if (created != null) p.SetValue(o, created);
			}
			return o;
		}
	}

	/// <summary>
	/// Admin Assist field help is appended straight after its editor. Where the editor's next sibling is part of the control,
	/// or the editor sits in a narrow column, that breaks the page (a switch that always draws OFF, help wrapping a word per
	/// line): such editors are wrapped in &lt;admin-assist-help-after&gt;. Every screen the catalog points an editor at is
	/// rendered with field help on, and the help must land where it reads.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AdminAssistFieldHelpLayoutTests
	{
		// Catalog screens whose editors are not asp-for inputs on the page, so no field help is attached there.
		private static readonly Dictionary<string, string> NotRenderedHere = new()
		{
			["Department/OperatingProfile"] = "rendered by OperatingProfileRenderingTests",
			["Department/Terminology"] = "editors are built per term, not asp-for",
			["Department/TextSettings"] = "the catalog's text-to-call editors are inside a Razor comment",
			["Security/Index"] = "permission editors are built per permission, not asp-for",
			["IncidentBackOffice/Settings"] = "policy editors are built per policy field from IncidentPolicyForm, not asp-for; Field is the policy form's anchor",
		};

		private static readonly string[] SwitchFields = { "MessagingEnabled", "MappingEnabled", "ShiftsEnabled", "LogsEnabled", "ReportsEnabled", "DocumentsEnabled",
			"CalendarEnabled", "NotesEnabled", "TrainingEnabled", "InventoryEnabled", "ChecklistsEnabled", "MaintenanceEnabled", "BusinessOperationsEnabled",
			"IncidentBackOfficeEnabled", "IncidentBusinessEnabled", "IncidentCoordinationEnabled" };

		private readonly Resgrid.AdminAssist.ConfigurationCatalog _catalog = new();
		private WebApplication _app;
		private HttpClient _client;
		private IHttpContextAccessor _previousAccessor;

		[OneTimeSetUp]
		public async Task StartAsync()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddWebOptimizer();
			// The real catalog and an allowing access check, so asp-for editors carry their Admin Assist field help.
			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			builder.Services.AddSingleton<IAdminAssistCatalog>(_catalog);
			builder.Services.AddSingleton(access.Object);
			// Every flag and availability check on, so gated sections and their editors render too.
			var flags = new Mock<Resgrid.Model.Services.IFeatureToggleService>(); flags.SetReturnsDefault(Task.FromResult(true));
			builder.Services.AddSingleton(flags.Object);
			var aiDispatch = new Mock<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>(); aiDispatch.SetReturnsDefault(Task.FromResult(true));
			builder.Services.AddSingleton(aiDispatch.Object);
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddDepartmentTerminologyStub();
			builder.Services.AddControllersWithViews()
				.AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.DepartmentController).Assembly)
				.AddApplicationPart(typeof(AdminAssistFieldHelpLayoutController).Assembly);
			_app = builder.Build();
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = _app.Services.GetRequiredService<IHttpContextAccessor>();
			_app.Use(async (context, next) => {
				context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "admin"), new Claim(ClaimTypes.PrimaryGroupSid, "77") }, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			_app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			_app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			await _app.StartAsync();
			_client = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
		}

		[OneTimeTearDown]
		public async Task StopAsync()
		{
			_client?.Dispose();
			ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;
			if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
		}

		[Test]
		public void Every_catalog_screen_with_editor_help_is_checked_here()
		{
			var checkedHere = typeof(AdminAssistFieldHelpLayoutTests).GetMethod(nameof(Field_help_lands_where_it_reads))!
				.GetCustomAttributes(typeof(TestCaseAttribute), false).Cast<TestCaseAttribute>().Select(t => (string)t.Arguments[0]);
			var screens = _catalog.Settings.Where(s => s.Location.Field != null).Select(s => s.Location.Controller + "/" + s.Location.Action).Distinct();
			screens.Except(checkedHere).Except(NotRenderedHere.Keys).Should().BeEmpty("a new screen with field help needs a layout check here");
		}

		[TestCase("Department/CallSettings", "Resgrid.Web.Areas.User.Models.Departments.CallSettings.CallSettingsView")]
		[TestCase("Department/DispatchSettings", "Resgrid.Web.Areas.User.Models.Departments.DispatchSettingsView")]
		[TestCase("Department/DocumentNumbering", "Resgrid.Web.Areas.User.Models.Departments.DocumentNumberingView")]
		[TestCase("Department/MappingSettings", "Resgrid.Web.Areas.User.Models.Departments.MappingSettingsView")]
		[TestCase("Department/ModuleSettings", "Resgrid.Web.Areas.User.Models.Departments.DepartmentModulesSettingView")]
		[TestCase("Department/Settings", "Resgrid.Web.Areas.User.Models.DepartmentSettingsModel")]
		[TestCase("Department/ShiftSettings", "Resgrid.Web.Areas.User.Models.Departments.ShiftSettingsView")]
		[TestCase("DepartmentImport/Settings", "Resgrid.Web.Areas.User.Models.DepartmentImport.ImportSettingsView")]
		[TestCase("Records/Settings", "Resgrid.Web.Areas.User.Models.Records.RecordsSettingsView")]
		[TestCase("Security/SecurityPolicy", "Resgrid.Web.Areas.User.Models.Security.SecurityPolicyEditView")]
		public async Task Field_help_lands_where_it_reads(string screen, string model)
		{
			var html = await RenderAsync(screen, model);
			html.Should().NotContain("admin-assist-help-after", "the wrapper renders nothing of its own");
			var doc = new HtmlDocument(); doc.LoadHtml(html);
			var helps = doc.DocumentNode.SelectNodes("//span[contains(concat(' ', normalize-space(@class), ' '), ' admin-assist-field-help ')]")?.ToList() ?? new List<HtmlNode>();
			helps.Should().NotBeEmpty("the screen's editors carry field help");
			helps.Select(h => h.Id).Should().OnlyHaveUniqueItems();
			foreach (var help in helps)
			{
				var ancestors = help.Ancestors().Where(a => a.NodeType == HtmlNodeType.Element).ToList();
				// Not inside the control: a label (between a checkbox and its caption), a switch box or an input group.
				ancestors.Should().NotContain(a => a.Name == "label" || HasClass(a, "onoffswitch") || HasClass(a, "input-group"), help.Id);
				// Not between a checkbox and the label that draws it ("checkbox:checked + label").
				var previous = PreviousElement(help); var next = NextElement(help);
				var splitsControl = previous?.Name == "input" && previous.GetAttributeValue("type", "") is "checkbox" or "radio" && next?.Name == "label";
				splitsControl.Should().BeFalse(help.Id);
				// At least half the form row wide, not a narrow editor column.
				var share = ancestors.TakeWhile(a => !HasClass(a, "form-group")).Select(ColumnShare).Aggregate(1.0, (x, y) => x * y);
				share.Should().BeGreaterThanOrEqualTo(0.5, $"{help.Id} gets {share:P0} of its form row");
			}
		}

		[Test]
		public async Task Module_switches_keep_their_checkbox_next_to_the_label_and_draw_their_state()
		{
			var html = await RenderAsync("Department/ModuleSettings", "Resgrid.Web.Areas.User.Models.Departments.DepartmentModulesSettingView");
			foreach (var field in SwitchFields)
			{
				var helpId = Regex.Escape("aa-help-" + _catalog.FieldHelpEntry("Department", "ModuleSettings", field).Id);
				html.Should().MatchRegex($"<input[^>]*id=\"{field}\"[^>]*>\\s*<label class=\"onoffswitch-label\" for=\"{field}\">", field);
				html.Should().MatchRegex($"(?s)<label class=\"onoffswitch-label\" for=\"{field}\">(?:(?!</label>).)*</label>\\s*</div>\\s*</div>\\s*<span class=\"help-block admin-assist-field-help\" id=\"{helpId}\">", field);
			}
			// An editor outside the wrapper keeps its help straight after it.
			var aiHelpId = Regex.Escape("aa-help-" + _catalog.FieldHelpEntry("Department", "ModuleSettings", "AiEnabled").Id);
			html.Should().MatchRegex($"<input[^>]*id=\"AiEnabled\"[^>]*>\\s*<span class=\"help-block admin-assist-field-help\" id=\"{aiHelpId}\"");

			// An Admin Assist link to a switch highlights its checkbox, which still sits right before the label the outline is drawn on.
			var linked = await RenderAsync("Department/ModuleSettings", "Resgrid.Web.Areas.User.Models.Departments.DepartmentModulesSettingView", "&aa=MappingEnabled");
			linked.Should().MatchRegex("<input[^>]*class=\"onoffswitch-checkbox admin-assist-field-highlight\"[^>]*id=\"MappingEnabled\"[^>]*>\\s*<label class=\"onoffswitch-label\" for=\"MappingEnabled\">");
		}

		[Test]
		public async Task Narrow_column_help_moves_to_its_own_column_under_the_editor()
		{
			var html = await RenderAsync("Records/Settings", "Resgrid.Web.Areas.User.Models.Records.RecordsSettingsView");
			var helpId = Regex.Escape("aa-help-" + _catalog.FieldHelpEntry("Records", "Settings", "SequenceWidth").Id);
			// (A number input is followed by the __Invariant marker MVC adds for culture-invariant parsing.)
			html.Should().MatchRegex($"<input[^>]*id=\"SequenceWidth\"[^>]*>(?:<input name=\"__Invariant\"[^>]*>)?\\s*</div>\\s*<div class=\"col-sm-offset-3 col-sm-9\"><span class=\"help-block admin-assist-field-help\" id=\"{helpId}\">");
		}

		private async Task<string> RenderAsync(string screen, string model, string query = "")
		{
			var parts = screen.Split('/');
			var response = await _client.GetAsync($"/User/AdminAssistFieldHelpLayout/Show?view={screen}&model={model}&c={parts[0]}&a={parts[1]}{query}");
			var html = await response.Content.ReadAsStringAsync();
			response.StatusCode.Should().Be(HttpStatusCode.OK, html);
			return html;
		}

		private static bool HasClass(HtmlNode node, string css) => node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(css);
		private static HtmlNode PreviousElement(HtmlNode node) { var n = node.PreviousSibling; while (n != null && n.NodeType != HtmlNodeType.Element) n = n.PreviousSibling; return n; }
		private static HtmlNode NextElement(HtmlNode node) { var n = node.NextSibling; while (n != null && n.NodeType != HtmlNodeType.Element) n = n.NextSibling; return n; }

		// The share of its row a Bootstrap column takes on a desktop screen: its narrowest sm/md/lg width.
		private static double ColumnShare(HtmlNode node)
		{
			var widths = Regex.Matches(node.GetAttributeValue("class", ""), @"\bcol-(?:sm|md|lg)-(\d+)\b").Select(m => int.Parse(m.Groups[1].Value)).ToList();
			return widths.Count == 0 ? 1.0 : widths.Min() / 12.0;
		}
	}
}
