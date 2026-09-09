using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Inventory;
using Resgrid.Web.Helpers;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using ApiInventoryController = Resgrid.Web.Services.Controllers.v4.InventoryController;
using DataProtectionController = Resgrid.Web.Services.Controllers.v4.DataProtectionController;
using MvcInventoryController = Resgrid.Web.Areas.User.Controllers.InventoryController;
using File = System.IO.File;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryApiTests
	{
		private const string M5CountId = "77777777-7777-7777-7777-777777777777";
		private const string M5LineId = "88888888-8888-8888-8888-888888888888";

		[Test]
		public async Task Operations_API_uses_authenticated_tenant_and_header_grant_for_count_commands_and_query_filters()
		{
			var operations = M5Operations();
			await WithM5Server(operations, async (client, _) =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "attended-grant");
				var requestId = Guid.NewGuid().ToString("D");
				await Success(await client.PostAsync(Route + "StartCount", Json(new { Id = M5CountId, LocationId, Name = Canary, DepartmentId = 88, UserId = "forged", GrantToken = "forged-grant" })));
				await Success(await client.PostAsync(Route + "SaveCount", Json(new { CountId = M5CountId, Revision = 3, Lines = new[] { new { Id = M5LineId, Quantity = 1.125001m } }, DepartmentId = 88 })));
				await Success(await client.PostAsync(Route + "CompleteCount", Json(new { CountId = M5CountId, Revision = 4, RequestId = requestId, DepartmentId = 88 })));
				await Success(await client.PostAsync(Route + "CancelCount", Json(new { CountId = M5CountId, Revision = 5, DepartmentId = 88 })));
				await Success(await client.PostAsync(Route + "RefreshAlerts", Json(new { DepartmentId = 88 })));
				var response = await client.GetAsync(Route + "GetCounts?page=3&locationId=" + LocationId + "&departmentId=88");
				await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var json = JObject.Parse(await response.Content.ReadAsStringAsync()); json.Value<int>("Page").Should().Be(3); json["Data"]["Items"].Should().NotBeNull();
				await Success(await client.GetAsync(Route + "GetAlerts?page=2&itemId=" + ItemId + "&locationId=" + LocationId + "&assetId=" + LotId));
				operations.Verify(x => x.StartCountAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager" && a.GrantToken == "attended-grant"), It.Is<InventoryCountInput>(i => i.Id == M5CountId && i.LocationId == LocationId && i.Name == Canary)), Times.Once);
				operations.Verify(x => x.SaveCountAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager"), It.Is<InventoryCountUpdate>(i => i.CountId == M5CountId && i.Revision == 3 && i.Lines.Single().Quantity == 1.125001m)), Times.Once);
				operations.Verify(x => x.CompleteCountAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77), It.Is<InventoryCountComplete>(i => i.RequestId == requestId && i.Revision == 4)), Times.Once);
				operations.Verify(x => x.CancelCountAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77), M5CountId, 5), Times.Once);
				operations.Verify(x => x.RefreshAlertsAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.GrantToken == "attended-grant")), Times.Once);
				_catalog.Verify(x => x.QueryAsync<InventoryCount>(It.Is<InventoryActor>(a => a.DepartmentId == 77), It.Is<InventoryQuery>(q => q.LocationId == LocationId), 3), Times.Once);
				_catalog.Verify(x => x.QueryAsync<InventoryAlert>(It.Is<InventoryActor>(a => a.DepartmentId == 77), It.Is<InventoryQuery>(q => q.ItemId == ItemId && q.LocationId == LocationId && q.AssetId == LotId), 2), Times.Once);
			});
		}

		[Test]
		public async Task Operations_API_count_read_rejects_missing_auth_query_grants_and_foreign_sources_without_disclosure()
		{
			var operations = M5Operations(); var foreignId = Guid.NewGuid().ToString("D");
			operations.Setup(x => x.GetCountAsync(It.IsAny<InventoryActor>(), It.IsAny<string>())).ReturnsAsync((InventoryActor actor, string id) =>
			{
				if (id == foreignId) throw new InventoryException(404, "NotFound");
				if (actor.GrantToken != "read-grant") throw new InventoryException(403, "ProtectedDataRequired");
				return M5Count();
			});
			await WithM5Server(operations, async (client, _) =>
			{
				(await client.GetAsync(Route + "GetCount?id=" + M5CountId)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				SignIn(client);
				var response = await client.GetAsync(Route + "GetCount?id=" + M5CountId + "&GrantToken=read-grant");
				response.StatusCode.Should().Be(HttpStatusCode.Forbidden); response.Headers.CacheControl.NoStore.Should().BeTrue();
				(await response.Content.ReadAsStringAsync()).Should().Contain("ProtectedDataRequired").And.NotContain(Canary).And.NotContain("read-grant");
				client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "read-grant");
				response = await client.GetAsync(Route + "GetCount?id=" + M5CountId); await Success(response);
				(await response.Content.ReadAsStringAsync()).Should().Contain(Canary).And.NotContain("read-grant");
				response = await client.GetAsync(Route + "GetCount?id=" + foreignId + "&departmentId=88");
				response.StatusCode.Should().Be(HttpStatusCode.NotFound); response.Headers.CacheControl.NoStore.Should().BeTrue();
				(await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				operations.Verify(x => x.GetCountAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager"), foreignId), Times.Once);
			});
		}

		[Test]
		public async Task Operations_MVC_all_reads_reveals_and_writes_require_CSRF_and_count_observations_preserve_decimal_precision()
		{
			var operations = M5Operations();
			await WithM5Server(operations, async (client, _) =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add("Test-Reports", "true");
				var page = await client.GetAsync(MvcRoute + "Operations"); await Success(page);
				var fields = new Dictionary<string, string> { ["Id"] = M5CountId, ["CountId"] = M5CountId, ["Revision"] = "7", ["RequestId"] = Guid.NewGuid().ToString("D"),
					["Lines[0].Id"] = M5LineId, ["Lines[0].Quantity"] = "1.125001", ["DepartmentId"] = "88", ["UserId"] = "forged", ["GrantToken"] = "forged-grant", [HttpProtectedGrantContext.FormFieldName] = "form-grant" };
				foreach (var action in new[] { "GetCounts", "GetCount", "GetAlerts", "StartCount", "SaveCount", "CompleteCount", "CancelCount", "RefreshAlerts", "ReopenOperations", "BuildReport", "ReportPdf" })
					(await client.PostAsync(MvcRoute + action, new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.BadRequest, action);
				operations.Verify(x => x.SaveCountAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCountUpdate>()), Times.Never);
				fields["__RequestVerificationToken"] = PurchasingCsrf(await page.Content.ReadAsStringAsync());
				var response = await client.PostAsync(MvcRoute + "SaveCount", new FormUrlEncodedContent(fields)); await Success(response);
				response.Headers.CacheControl.NoStore.Should().BeTrue(); (await response.Content.ReadAsStringAsync()).Should().NotContain("form-grant").And.NotContain("forged-grant");
				operations.Verify(x => x.SaveCountAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager" && a.GrantToken == "form-grant"), It.Is<InventoryCountUpdate>(i => i.CountId == M5CountId && i.Revision == 7 && i.Lines.Single().Quantity == 1.125001m)), Times.Once);
				fields["Lines[0].Quantity"] = Canary;
				response = await client.PostAsync(MvcRoute + "SaveCount", new FormUrlEncodedContent(fields)); response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				(await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				fields.Remove("RequestId");
				response = await client.PostAsync(MvcRoute + "CompleteCount", new FormUrlEncodedContent(fields)); response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				operations.Verify(x => x.CompleteCountAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCountComplete>()), Times.Never);
			}, mvc: true);
		}

		[Test]
		public async Task Operations_MVC_protected_GET_is_a_selection_preserving_shell_and_POST_reveal_renders_count_controls()
		{
			var operations = M5Operations();
			operations.Setup(x => x.GetCountAsync(It.IsAny<InventoryActor>(), M5CountId)).ReturnsAsync((InventoryActor actor, string _) =>
			{
				if (actor.GrantToken != "navigation-grant") throw new InventoryException(403, "ProtectedDataRequired");
				return M5Count();
			});
			await WithM5Server(operations, async (client, views) =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "navigation-grant");
				var address = MvcRoute + "Operations?tab=Counts&page=3&id=" + M5CountId + "&itemId=" + ItemId + "&locationId=" + LocationId;
				var response = await client.GetAsync(address); await Success(response); var html = await response.Content.ReadAsStringAsync();
				html.Should().NotContain(Canary).And.Contain("ReopenOperations"); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var locked = (InventoryWorkspaceView)views.LastView.Model;
				locked.Locked.Should().BeTrue(); locked.Id.Should().Be(M5CountId); locked.ItemId.Should().Be(ItemId); locked.LocationId.Should().Be(LocationId); locked.Page.Should().Be(3); locked.CountDetail.Should().BeNull();
				operations.Verify(x => x.GetCountAsync(It.IsAny<InventoryActor>(), It.IsAny<string>()), Times.Never);
				_catalog.Verify(x => x.ListAsync<InventoryItem>(It.IsAny<InventoryActor>(), It.IsAny<int>()), Times.Never);
				client.DefaultRequestHeaders.Remove(DataProtectionController.GrantHeader);
				var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = PurchasingCsrf(html), ["tab"] = "Counts", ["page"] = "3", ["id"] = M5CountId, ["itemId"] = ItemId,
					["locationId"] = LocationId, [HttpProtectedGrantContext.FormFieldName] = "navigation-grant", [HttpProtectedGrantContext.ExpiresOnFormFieldName] = "2030-01-01T00:00:00Z" };
				response = await client.PostAsync(MvcRoute + "ReopenOperations", new FormUrlEncodedContent(fields)); await Success(response); html = await response.Content.ReadAsStringAsync();
				html.Should().Contain(Canary).And.Contain("SaveCount").And.Contain("CompleteCount").And.Contain("CancelCount").And.Contain("Lines[0].Quantity").And.Contain("1.125001");
				response.Headers.CacheControl.NoStore.Should().BeTrue(); var revealed = (InventoryWorkspaceView)views.LastView.Model;
				revealed.Locked.Should().BeFalse(); revealed.CountDetail.Count.Id.Should().Be(M5CountId); revealed.Page.Should().Be(3); revealed.ItemId.Should().Be(ItemId); revealed.LocationId.Should().Be(LocationId);
				views.LastView.ViewData["ProtectedGrant"].Should().Be("navigation-grant");
				response = await client.GetAsync(address); await Success(response); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				((InventoryWorkspaceView)views.LastView.Model).Locked.Should().BeTrue();
			}, mvc: true, protectedData: true);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Operations_reports_require_Reports_View_in_addition_to_Inventory_access(bool mvc)
		{
			var operations = M5Operations();
			await WithM5Server(operations, async (client, views) =>
			{
				SignIn(client); string token = null;
				if (mvc)
				{
					var page = await client.GetAsync(MvcRoute + "Operations"); await Success(page); var html = await page.Content.ReadAsStringAsync(); token = PurchasingCsrf(html);
					((InventoryWorkspaceView)views.LastView.Model).CanViewReports.Should().BeFalse(); html.Should().NotContain("value=\"Reports\"");
					(await client.GetAsync(MvcRoute + "Operations?tab=Reports")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
				}
				HttpContent Body() => mvc ? new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Kind"] = "Expiration" }) : Json(new { Kind = InventoryReportKind.Expiration });
				var response = await client.PostAsync((mvc ? MvcRoute : Route) + "BuildReport", Body()); response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
				operations.Verify(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>()), Times.Never);
				client.DefaultRequestHeaders.Add("Test-Reports", "true");
				// The synthetic identity's claims changed; obtain the CSRF token bound to that identity.
				if (mvc) { var page = await client.GetAsync(MvcRoute + "Operations?tab=Reports"); await Success(page); token = PurchasingCsrf(await page.Content.ReadAsStringAsync()); }
				response = await client.PostAsync((mvc ? MvcRoute : Route) + "BuildReport", Body()); await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				operations.Verify(x => x.BuildReportAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager"), It.Is<InventoryReportInput>(i => i.Kind == InventoryReportKind.Expiration)), Times.Once);
				if (mvc)
				{
					response = await client.GetAsync(MvcRoute + "Operations?tab=Reports"); await Success(response);
					((InventoryWorkspaceView)views.LastView.Model).CanViewReports.Should().BeTrue(); (await response.Content.ReadAsStringAsync()).Should().Contain("ReportPdf").And.Contain("inventory-report-form");
				}
				else
				{
					response = await client.GetAsync(Route + "GetExpiring?itemId=" + ItemId + "&locationId=" + LocationId); await Success(response);
					operations.Verify(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.Is<InventoryReportInput>(i => i.Kind == InventoryReportKind.Expiration && i.ItemId == ItemId && i.LocationId == LocationId)), Times.Once);
				}
			}, mvc: mvc);
		}

		[TestCase("permission", HttpStatusCode.Forbidden, "PermissionRequired")]
		[TestCase("inventory", HttpStatusCode.Forbidden, "PermissionRequired")]
		[TestCase("grant", HttpStatusCode.Forbidden, "ProtectedDataRequired")]
		[TestCase("changed", HttpStatusCode.Conflict, "ReportChanged")]
		[TestCase("renderer", HttpStatusCode.Conflict, "ReportUnavailable")]
		public async Task Operations_PDF_rechecks_access_and_snapshot_after_rendering_and_never_returns_a_stale_document(string change, HttpStatusCode status, string code)
		{
			var operations = M5Operations(); var pdf = new Mock<IPdfProvider>(); var converted = false;
			operations.Setup(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>())).ReturnsAsync((InventoryActor _, InventoryReportInput input) =>
			{
				if (converted && change == "grant") throw new InventoryException(403, "ProtectedDataRequired");
				var report = M5Report(input.Kind); if (converted && change == "changed") report.Rows.Clear(); return report;
			});
			_authorization.Setup(x => x.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<PermissionTypes?>(), It.IsAny<int?>()))
				.Returns(() => converted && change == "inventory" ? Task.FromException(new InventoryException(403, "PermissionRequired")) : Task.CompletedTask);
			pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>())).Returns((string _) =>
			{
				converted = true; if (change == "renderer") throw new InvalidOperationException(Canary); return Encoding.ASCII.GetBytes("%PDF-SYNTHETIC");
			});
			await WithM5Server(operations, async (client, _) =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add("Test-Reports", "true");
				var page = await client.GetAsync(MvcRoute + "Operations"); await Success(page);
				var response = await client.PostAsync(MvcRoute + "ReportPdf", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = PurchasingCsrf(await page.Content.ReadAsStringAsync()), ["Kind"] = "Usage" }));
				response.StatusCode.Should().Be(status); response.Headers.CacheControl.NoStore.Should().BeTrue();
				(await response.Content.ReadAsStringAsync()).Should().Contain(code).And.NotContain(Canary).And.NotContain("%PDF-SYNTHETIC");
				pdf.Verify(x => x.ConvertHtmlToPdf(It.IsAny<string>()), Times.Once);
				operations.Verify(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>()), Times.Exactly(change == "renderer" ? 1 : 2));
			}, mvc: true, pdf: pdf.Object, reportsAllowed: () => !(converted && change == "permission"));
		}

		[TestCase(InventoryReportKind.Usage)]
		[TestCase(InventoryReportKind.Expiration)]
		public async Task Operations_PDF_success_rechecks_effective_scope_and_encodes_protected_text(InventoryReportKind kind)
		{
			var operations = M5Operations(); var pdf = new Mock<IPdfProvider>(); var inputs = new List<InventoryReportInput>();
			operations.Setup(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>())).ReturnsAsync((InventoryActor actor, InventoryReportInput input) =>
			{
				actor.GrantToken.Should().Be("pdf-grant"); inputs.Add(Copy(input)); var report = M5Report(input.Kind);
				report.Rows[0]["Name"] = Canary + "<script>"; report.GeneratedOn = report.GeneratedOn.AddSeconds(inputs.Count); return report;
			});
			pdf.Setup(x => x.ConvertHtmlToPdf(It.IsAny<string>())).Returns((string html) =>
			{
				html.Should().Contain(Canary + "&lt;script&gt;").And.NotContain("<script>").And.NotContain("pdf-grant"); return Encoding.ASCII.GetBytes("%PDF-SYNTHETIC");
			});
			await WithM5Server(operations, async (client, _) =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add("Test-Reports", "true");
				var page = await client.GetAsync(MvcRoute + "Operations"); await Success(page);
				var response = await client.PostAsync(MvcRoute + "ReportPdf", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = PurchasingCsrf(await page.Content.ReadAsStringAsync()),
					["Kind"] = kind.ToString(), ["ItemId"] = ItemId, ["LocationId"] = LocationId, ["UnitId"] = "31", ["UserId"] = "holder", [HttpProtectedGrantContext.FormFieldName] = "pdf-grant" }));
				await Success(response); response.Content.Headers.ContentType.MediaType.Should().Be("application/pdf"); response.Headers.CacheControl.NoStore.Should().BeTrue();
				(await response.Content.ReadAsByteArrayAsync()).Should().Equal(Encoding.ASCII.GetBytes("%PDF-SYNTHETIC"));
				inputs.Should().HaveCount(2); inputs[1].ItemId.Should().Be(ItemId); inputs[1].LocationId.Should().Be(LocationId); inputs[1].UnitId.Should().Be(31); inputs[1].UserId.Should().Be("holder");
				inputs[1].FromUtc.Should().Be(kind == InventoryReportKind.Expiration ? null : M5Report(kind).FromUtc);
				inputs[1].UntilUtc.Should().Be(kind == InventoryReportKind.Expiration ? null : M5Report(kind).UntilUtc);
			}, mvc: true, pdf: pdf.Object);
		}

		private Mock<IInventoryOperationsService> M5Operations()
		{
			ConfigurePurchasingMvc();
			_catalog.Setup(x => x.QueryAsync<InventoryCount>(It.IsAny<InventoryActor>(), It.IsAny<InventoryQuery>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryCount>());
			_catalog.Setup(x => x.QueryAsync<InventoryAlert>(It.IsAny<InventoryActor>(), It.IsAny<InventoryQuery>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryAlert>());
			var operations = new Mock<IInventoryOperationsService>();
			operations.Setup(x => x.GetCountAsync(It.IsAny<InventoryActor>(), It.IsAny<string>())).ReturnsAsync(M5Count());
			operations.Setup(x => x.StartCountAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCountInput>())).ReturnsAsync(M5Count());
			operations.Setup(x => x.SaveCountAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCountUpdate>())).ReturnsAsync(M5Count());
			operations.Setup(x => x.CompleteCountAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCountComplete>())).ReturnsAsync(new InventoryResult { OperationId = M5CountId });
			operations.Setup(x => x.BuildReportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryReportInput>())).ReturnsAsync((InventoryActor _, InventoryReportInput input) => M5Report(input.Kind));
			return operations;
		}
		private static InventoryCountDetail M5Count() => new()
		{
			Count = new InventoryCount { Id = M5CountId, DepartmentId = 77, Revision = 3, LocationId = LocationId, Content = JsonConvert.SerializeObject(new InventoryCountContent { Name = Canary }) },
			Lines = new() { new InventoryCountItem { Id = M5LineId, DepartmentId = 77, CountId = M5CountId, ItemId = ItemId, LocationId = LocationId, ExpectedQuantity = 2, CountedQuantity = 1.125001m,
				Content = JsonConvert.SerializeObject(new InventoryCountItemContent { ItemName = "Synthetic item", UnitOfMeasure = "each" }) } }
		};
		private static InventoryReport M5Report(InventoryReportKind kind) => new()
		{
			Kind = kind, GeneratedOn = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc), FromUtc = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc), UntilUtc = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
			Columns = new() { "Name" }, Rows = new() { new Dictionary<string, object> { ["Name"] = Canary } }
		};

		private async Task WithM5Server(Mock<IInventoryOperationsService> operations, Func<HttpClient, PurchasingBodyFilter, Task> test,
			bool mvc = false, bool protectedData = false, IPdfProvider pdf = null, Func<bool> reportsAllowed = null)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			if (directory == null) throw new InvalidOperationException("The test workspace root is unavailable.");
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(directory.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddApiVersioning();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, M5Authentication>(scheme, _ => { });
			builder.Services.AddAuthorization(options =>
			{
				options.AddPolicy(ResgridResources.Inventory_Delete, policy => policy.RequireClaim("inventory-delete", "true"));
				options.AddPolicy(ResgridResources.Reports_View, policy => policy.RequireAssertion(context => context.User.HasClaim("reports-view", "true") && (reportsAllowed?.Invoke() ?? true)));
			});
			var views = new PurchasingBodyFilter();
			if (mvc) builder.Services.AddControllersWithViews(options => options.Filters.Add(views)).AddApplicationPart(typeof(MvcInventoryController).Assembly)
				.AddNewtonsoftJson(options => options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			else builder.Services.AddControllers().AddApplicationPart(typeof(ApiInventoryController).Assembly)
				.AddNewtonsoftJson(options => options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			builder.Services.AddSingleton(_catalog.Object); builder.Services.AddSingleton(_stock.Object); builder.Services.AddSingleton(_transfers.Object);
			builder.Services.AddSingleton(_issuance.Object); builder.Services.AddSingleton(_migration.Object); builder.Services.AddSingleton(_authorization.Object); builder.Services.AddSingleton(_purchasing.Object);
			builder.Services.AddSingleton(operations.Object); builder.Services.AddSingleton(pdf ?? Mock.Of<IPdfProvider>());
			builder.Services.AddSingleton(Mock.Of<IUnitsService>()); builder.Services.AddSingleton(Mock.Of<IDepartmentGroupsService>()); builder.Services.AddSingleton(Mock.Of<IDepartmentsService>());
			builder.Services.AddSingleton<IProtectedGrantContext, HttpProtectedGrantContext>();
			var protection = new Mock<IDepartmentDataProtectionService>(); protection.Setup(x => x.IsProtectionEnforcedAsync(77)).ReturnsAsync(protectedData); builder.Services.AddSingleton(protection.Object);
			await using var app = builder.Build(); var previousMvc = ClaimsAuthorizationHelper._httpContextAccessor; var previousApi = ApiClaims._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>(); ApiClaims._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("fr").AddSupportedCultures("fr").AddSupportedUICultures("fr"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
			if (mvc) app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}"); else app.MapControllers();
			try
			{
				await app.StartAsync(); using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
				using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) }; await test(client, views);
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previousMvc; ApiClaims._httpContextAccessor = previousApi; }
		}
		private sealed class M5Authentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public M5Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				if (!Request.Headers.TryGetValue("Test-Member", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
				var claims = new List<Claim> { new(ClaimTypes.PrimarySid, user.ToString()), new(ClaimTypes.PrimaryGroupSid, "77") };
				if (Request.Headers["Test-Reports"] == "true") claims.Add(new Claim("reports-view", "true"));
				if (Request.Headers["Test-Inventory-Delete"] == "true") claims.Add(new Claim("inventory-delete", "true"));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
			}
		}
	}
}
