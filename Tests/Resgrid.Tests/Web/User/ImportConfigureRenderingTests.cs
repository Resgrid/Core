using System;
using System.Collections.Generic;
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
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Web.Areas.User.Models.DepartmentImport;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	// Renders the production Configure tab (wizard plan section 6.3 step 9) with one row in each state.
	[Area("User")]
	public class ImportConfigureRenderingController : Controller
	{
		public IActionResult Show()
		{
			// The workspace partials (_Shell, _Tabs, _Badge) are found next to the owning controller's views.
			RouteData.Values["controller"] = "DepartmentImport";
			RouteData.Values["action"] = "Configure";
			var workspace = new ImportWorkspace { Id = "W1", DepartmentId = 12, Name = "Move from CAD", Status = (int)ImportWorkspaceStatus.Collecting, UpdatedOn = DateTime.UtcNow };
			var changes = new Dictionary<string, ImportConfigurationChange>
			{
				["terminology." + DepartmentTerms.Units] = Change("terminology." + DepartmentTerms.Units, "Apparatus", ImportConfigurationChangeState.Accepted, ImportConfigurationOrigin.Administrator),
				["numbering.invoice.next"] = Change("numbering.invoice.next", "500", ImportConfigurationChangeState.Failed, ImportConfigurationOrigin.Administrator, "IMP-CONFIG-NEEDS-PATTERN"),
				["permission.CreateCall"] = Change("permission.CreateCall", ImportConfigurationCatalog.Permission((int)PermissionActions.DepartmentAdminsAndSelectRoles, false, new[] { 3 }),
					ImportConfigurationChangeState.Accepted, ImportConfigurationOrigin.Administrator),
				["map.day_style"] = Change("map.day_style", "Dark", ImportConfigurationChangeState.Applied, ImportConfigurationOrigin.Administrator)
			};
			var current = new Dictionary<string, string>
			{
				["department.name"] = "Station 1",
				["department.time_zone"] = "Pacific Standard Time",
				["department.address"] = ImportConfigurationCatalog.AddressJson(new ImportConfigurationAddress { Address1 = "1 Main St", City = "Reno", State = "NV", Country = "US" }),
				["branding.logo"] = Resgrid.Services.Onboarding.ImportConfigurationTargets.LogoPresent,
				["modules.Shifts"] = "false",
				["map.day_style"] = "Dark",
				["numbering.calls.pattern"] = "{YY}-{SEQ}",
				["numbering.calls.next"] = "42",
				["permission.CreateCall"] = ImportConfigurationCatalog.Permission((int)PermissionActions.Everyone, false, null)
			};
			var suggestions = new Dictionary<string, (string Value, ImportConfigurationOrigin Origin, string Key)>
			{
				["terminology." + DepartmentTerms.Calls] = ("Missions", ImportConfigurationOrigin.Preset, "pack.sar"),
				["terminology." + DepartmentTerms.Personnel] = ("Members", ImportConfigurationOrigin.Product, "product.aladtec"),
				["modules.Shifts"] = ("true", ImportConfigurationOrigin.Preset, "pack.sar"),
				["modules.Mapping"] = ("true", ImportConfigurationOrigin.ImportedData, "pois")
			};

			var view = new ImportConfigurationView
			{
				Workspace = workspace,
				Profile = new DepartmentOperatingProfile { Revision = 2, Archetypes = new List<string> { "sar" }, WorkforceMix = "volunteer", DispatchModel = "self", OperatingHours = "on-call" },
				CanEdit = true,
				PermissionPreviewAvailable = true,
				Roles = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 3, Name = "Duty Officer" } }
			};
			foreach (var group in ImportConfigurationGroups.All)
			{
				var groupView = new ImportConfigurationGroupView { Key = group };
				foreach (var item in ImportConfigurationCatalog.All.Where(i => i.Group == group))
				{
					changes.TryGetValue(item.Key, out var change);
					var hasSuggestion = suggestions.TryGetValue(item.Key, out var suggestion);
					var value = current.TryGetValue(item.Key, out var now) ? now : string.Empty;
					groupView.Rows.Add(new ImportConfigurationRowView
					{
						Item = item,
						Current = value,
						Change = change,
						Suggested = hasSuggestion ? suggestion.Value : null,
						SuggestedOrigin = hasSuggestion ? suggestion.Origin : null,
						SuggestedOriginKey = hasSuggestion ? suggestion.Key : null,
						AwaitingDecision = hasSuggestion && change == null,
						Pending = change?.State == (int)ImportConfigurationChangeState.Accepted && change.Value != value,
						IssueCode = item.Key == "numbering.invoice.next" ? "IMP-CONFIG-NEEDS-PATTERN" : null
					});
				}
				groupView.Pending = groupView.Rows.Count(r => r.Pending);
				groupView.Suggestions = groupView.Rows.Count(r => r.AwaitingDecision);
				view.Groups.Add(groupView);
			}
			view.Pending = view.Groups.Sum(g => g.Pending);
			view.Suggestions = view.Groups.Sum(g => g.Suggestions);

			return PartialView("/Areas/User/Views/DepartmentImport/Configure.cshtml", new ImportConfigureView
			{
				Workspace = workspace,
				Catalog = ImportCatalog.Current,
				CanManage = true,
				Sources = new List<ImportSource>
				{
					new ImportSource { Id = "S1", ProductKey = "aladtec", CreatedOn = DateTime.UtcNow },
					new ImportSource { Id = "S2", ProductKey = "gartan", CreatedOn = DateTime.UtcNow }
				},
				Configuration = view,
				Impacts = new Dictionary<string, ConfigurationImpactReport>
				{
					["modules.Shifts"] = new ConfigurationImpactReport("module.ShiftsDisabled", "1", DateTime.UtcNow, "1", null,
						new[] { new ConfigurationImpactMetric("Impact.ModuleMenus", EvidenceState.Known, 0, 18) }, Array.Empty<ConfigurationImpactRule>(), Array.Empty<string>(), null)
				}
			});
		}

		/// <summary>The Files tab with two products chosen: the picker, their badges and the glossary drawer.</summary>
		public IActionResult ShowFiles()
		{
			RouteData.Values["controller"] = "DepartmentImport";
			RouteData.Values["action"] = "Files";
			return PartialView("/Areas/User/Views/DepartmentImport/Files.cshtml", new ImportFilesView
			{
				Workspace = new ImportWorkspace { Id = "W1", DepartmentId = 12, Name = "Move from Aladtec", Status = (int)ImportWorkspaceStatus.Collecting, UpdatedOn = DateTime.UtcNow },
				Catalog = ImportCatalog.Current,
				CanManage = true,
				ChunkSizeBytes = 4 * 1024 * 1024,
				Sources = new List<ImportSource>
				{
					new ImportSource { Id = "S1", ProductKey = "aladtec", CreatedOn = DateTime.UtcNow },
					new ImportSource { Id = "S2", ProductKey = "gartan", CreatedOn = DateTime.UtcNow.AddMinutes(1) }
				}
			});
		}

		private static ImportConfigurationChange Change(string key, string value, ImportConfigurationChangeState state, ImportConfigurationOrigin origin, string failure = null) =>
			new ImportConfigurationChange { ItemKey = key, Value = value, State = (int)state, Origin = (int)origin, FailureCode = failure, WorkspaceId = "W1" };
	}

	[TestFixture, NonParallelizable]
	public class ImportConfigureRenderingTests
	{
		[Test]
		public async Task Configure_tab_renders_every_group_with_origins_states_issues_and_editors()
		{
			var html = await RenderAsync("/User/ImportConfigureRendering/Show");

			html.Should().Contain("Profile and logo").And.Contain("Permissions").And.Contain("Numbering").And.Contain("Names");
			html.Should().Contain("Missions").And.Contain("Suggested from your operating profile").And.Contain("From the data you are importing");
			html.Should().Contain("Apparatus").And.Contain("Will be changed").And.Contain("Not changed").And.Contain("Changed");
			html.Should().Contain("Give this kind of document its own pattern first", "the failed and blocked row says why");
			html.Should().Contain("Duty Officer", "a permission for selected roles names them").And.Contain("Department Admins and Select Roles");
			html.Should().Contain("1 Main St, Reno, NV, US").And.Contain("Has a logo").And.Contain("Invoices &#xB7; next number");
			html.Should().Contain("/User/DepartmentImport/ConfigureDecide/W1").And.Contain("__RequestVerificationToken").And.Contain("name=\"Latitude\"")
				.And.Contain("name=\"Roles\"").And.Contain("enctype=\"multipart/form-data\"");
			html.Should().Contain("Accept all suggestions").And.Contain("0 &#x2192; 18", "Admin Assist's module impact is shown with the proposed value");
			html.Should().Contain("Suggested from the products you are moving from &#xB7; Aladtec", "a product's word names the product");
			html.Should().Contain("Record numbering").And.Contain("Run &#xB7; prefix").And.Contain("Permits &#xB7; next number").And.Contain("{PREFIX} (the record type)");
			html.Should().Contain("id=\"import-glossary\"").And.Contain("In Aladtec").And.Contain("Work groups").And.Contain("Shifts (not imported)")
				.And.Contain("Suggested name").And.NotContain("In Gartan", "a product with no pack has no glossary");
			foreach (var raw in new[] { ">ConfigGroup.", ">Config.", ">ConfigOrigin.", ">ConfigState.", ">ConfigModule.", ">ConfigKind.", ">Error.", "Pack.sar", "Profile.volunteer",
				"GlossaryHeader", "GlossaryCaption", "GlossaryProductColumn", "GlossaryFeatureNotImported" })
				html.Should().NotContain(raw, "every label is localized");
		}

		[Test]
		public async Task Files_tab_lists_the_chosen_products_with_what_Resgrid_has_for_them()
		{
			var html = await RenderAsync("/User/ImportConfigureRendering/ShowFiles");

			html.Should().Contain("Where your data comes from").And.Contain("Glossary available").And.Contain("No direct import &#x2014; use a spreadsheet");
			html.Should().Contain("/User/DepartmentImport/RemoveProduct/W1?sourceId=S1").And.Contain("/User/DepartmentImport/AddProduct/W1");
			html.Should().Contain("How to prepare a spreadsheet", "a product with nothing documented points at the spreadsheet guide");
			html.Should().Contain("<option value=\"d4h\">D4H Team Manager</option>").And.NotContain("<option value=\"aladtec\">", "a chosen product is not offered again");
			html.Should().Contain("id=\"import-glossary\"").And.Contain("Members");
			foreach (var raw in new[] { "ProductsHeader", "ProductStatus", "ProductsHelp", "ProductAdd", "GlossaryHeader", "GlossaryHelp" })
				html.Should().NotContain(raw, "every label is localized");
		}

		private static async Task<string> RenderAsync(string path)
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs();
			builder.Services.AddWebOptimizer(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddControllersWithViews().AddApplicationPart(typeof(Resgrid.Web.Areas.User.Controllers.DepartmentImportController).Assembly)
				.AddApplicationPart(typeof(ImportConfigureRenderingController).Assembly);
			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => {
				context.User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.PrimarySid, "user"), new Claim(ClaimTypes.PrimaryGroupSid, "12") }, "Test"));
				try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
			});
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
				var response = await client.GetAsync(path);
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				return html;
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; await app.StopAsync(); }
		}
	}
}
