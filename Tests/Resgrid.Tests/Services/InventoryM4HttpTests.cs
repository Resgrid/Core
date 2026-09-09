using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
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
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Inventory;
using Resgrid.Web.Helpers;
using DataProtectionController = Resgrid.Web.Services.Controllers.v4.DataProtectionController;
using MvcInventoryController = Resgrid.Web.Areas.User.Controllers.InventoryController;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryApiTests
	{
		private const string PurchaseOrderId = "44444444-4444-4444-4444-444444444444";
		private const string PurchaseLineId = "55555555-5555-5555-5555-555555555555";
		private const string VendorId = "66666666-6666-6666-6666-666666666666";
		private const string MvcRoute = "/User/Inventory/";

		[Test]
		public async Task Purchasing_API_sources_reads_and_typed_vendor_and_draft_writes_from_current_actor_and_header_grant()
		{
			InventoryActor vendorActor = null; InventoryVendorInput vendorInput = null; InventoryPurchaseOrderInput orderInput = null;
			_purchasing.Setup(x => x.GetVendorContactsAsync(It.IsAny<InventoryActor>())).ReturnsAsync(new List<InventoryVendorChoice>());
			_catalog.Setup(x => x.ListAsync<InventoryVendor>(It.IsAny<InventoryActor>(), 2)).ReturnsAsync(new InventoryPage<InventoryVendor> { HasMore = true });
			_catalog.Setup(x => x.ListAsync<InventoryPurchaseOrder>(It.IsAny<InventoryActor>(), 3)).ReturnsAsync(new InventoryPage<InventoryPurchaseOrder>());
			_purchasing.Setup(x => x.GetValuationAsync(It.IsAny<InventoryActor>(), LocationId)).ReturnsAsync(new InventoryValuation());
			_purchasing.Setup(x => x.SaveVendorAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryVendorInput>()))
				.ReturnsAsync((InventoryActor actor, InventoryVendorInput input) => { vendorActor = Copy(actor); vendorInput = Copy(input); return new InventoryVendor { Id = VendorId }; });
			_purchasing.Setup(x => x.SavePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseOrderInput>()))
				.ReturnsAsync((InventoryActor actor, InventoryPurchaseOrderInput input) => { actor.GrantToken.Should().Be("header-grant"); actor.DepartmentId.Should().Be(77); actor.UserId.Should().Be("manager"); orderInput = Copy(input); return new InventoryPurchaseOrderDetail { Order = new InventoryPurchaseOrder { Id = input.Id } }; });
			await WithServer(async client =>
			{
				(await client.GetAsync(Route + "GetVendorContacts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "header-grant");
				foreach (var route in new[] { "GetVendorContacts?departmentId=88&userId=forged&grantToken=query-grant", "GetVendors?page=2", "GetPurchaseOrders?page=3", "GetValuation?locationId=" + LocationId })
				{
					var read = await client.GetAsync(Route + route); await Success(read); read.Headers.CacheControl.NoStore.Should().BeTrue();
				}
				var contactId = new string('c', 128);
				var response = await client.PostAsync(Route + "SaveVendor", Json(new { Id = VendorId, Revision = 4, ContactId = contactId,
					DepartmentId = 88, UserId = "forged", GrantToken = "body-grant", Content = "raw-overpost", IsProtected = true,
					Details = new { AccountNumber = "Synthetic account", Note = Canary } }));
				await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				vendorActor.DepartmentId.Should().Be(77); vendorActor.UserId.Should().Be("manager"); vendorActor.GrantToken.Should().Be("header-grant");
				vendorInput.ContactId.Should().Be(contactId); vendorInput.Revision.Should().Be(4); vendorInput.Details.Note.Should().Be(Canary);
				JsonConvert.SerializeObject(vendorInput).Should().NotContain("raw-overpost").And.NotContain("body-grant");
				response = await client.PostAsync(Route + "SavePurchaseOrder", Json(new { Id = PurchaseOrderId, Revision = 0, VendorId, CurrencyCode = "EUR", Number = "Synthetic PO", Note = Canary,
					DepartmentId = 88, UserId = "forged", GrantToken = "body-grant", Status = 3, Content = "raw-overpost", QuantityReceived = 999,
					Lines = new[] { new { Id = PurchaseLineId, ItemId, QuantityOrdered = 5.125001m, UnitCost = 2.125001m, Note = Canary, QuantityReceived = 999, DepartmentId = 88 } } }));
				await Success(response); orderInput.Id.Should().Be(PurchaseOrderId); orderInput.Revision.Should().Be(0); orderInput.CurrencyCode.Should().Be("EUR");
				orderInput.Lines.Single().QuantityOrdered.Should().Be(5.125001m); orderInput.Lines.Single().UnitCost.Should().Be(2.125001m);
				JsonConvert.SerializeObject(orderInput).Should().NotContain("QuantityReceived").And.NotContain("raw-overpost").And.NotContain("body-grant");
				_purchasing.Verify(x => x.GetVendorContactsAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager" && a.GrantToken == "header-grant")), Times.Once);
				_catalog.Verify(x => x.ListAsync<InventoryVendor>(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager"), 2), Times.Once);
				_purchasing.Verify(x => x.GetValuationAsync(It.Is<InventoryActor>(a => a.GrantToken == "header-grant"), LocationId), Times.Once);
			});
		}

		[Test]
		public async Task Purchasing_API_binds_explicit_status_commands_and_partial_serialized_receipt_without_stock_fallback()
		{
			var changes = new List<(InventoryPurchaseOrderChange Input, InventoryPurchaseOrderStatus Status)>(); InventoryPurchaseReceiptInput receipt = null;
			_purchasing.Setup(x => x.ChangePurchaseOrderStatusAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseOrderChange>(), It.IsAny<InventoryPurchaseOrderStatus>()))
				.ReturnsAsync((InventoryActor actor, InventoryPurchaseOrderChange input, InventoryPurchaseOrderStatus status) => { changes.Add((Copy(input), status)); return new InventoryResult { PurchaseOrderId = input.Id }; });
			_purchasing.Setup(x => x.ReceivePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseReceiptInput>()))
				.ReturnsAsync((InventoryActor actor, InventoryPurchaseReceiptInput input) => { actor.DepartmentId.Should().Be(77); actor.UserId.Should().Be("manager"); actor.GrantToken.Should().Be("receipt-grant"); receipt = Copy(input); return new InventoryResult { PurchaseOrderId = input.PurchaseOrderId, AwaitingWitness = true }; });
			await WithServer(async client =>
			{
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "receipt-grant");
				foreach (var route in new[] { "OrderPurchaseOrder", "CancelPurchaseOrder" })
				{
					var request = Guid.NewGuid().ToString("D");
					var changed = await client.PostAsync(Route + route, Json(new { Id = PurchaseOrderId, Revision = 7, RequestId = request, Status = 3 }));
					await Success(changed); changes.Last().Input.RequestId.Should().Be(request); changes.Last().Input.Revision.Should().Be(7);
				}
				changes.Select(x => x.Status).Should().Equal(InventoryPurchaseOrderStatus.Ordered, InventoryPurchaseOrderStatus.Cancelled);
				var requestId = Guid.NewGuid().ToString("D");
				var response = await client.PostAsync(Route + "ReceivePurchaseOrder", Json(new { PurchaseOrderId, Revision = 9, RequestId = requestId,
					DepartmentId = 88, UserId = "body-user", GrantToken = "body-grant", Lines = new object[] {
						new { PurchaseOrderItemId = PurchaseLineId, LocationId, LotId, Quantity = 1.125001m, UnitCost = 999, QuantityReceived = 999 },
						new { PurchaseOrderItemId = ItemId, LocationId, Quantity = 1m, Asset = new { SerialNumber = Canary, AssetTag = "tag", Barcode = "barcode", ExpiresOn = "2027-09-09T00:00:00Z" } } } }));
				await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				JObject.Parse(await response.Content.ReadAsStringAsync())["Data"].Value<bool>("AwaitingWitness").Should().BeTrue();
				receipt.RequestId.Should().Be(requestId); receipt.Revision.Should().Be(9); receipt.Lines.Should().HaveCount(2);
				receipt.Lines[0].Quantity.Should().Be(1.125001m); receipt.Lines[0].LotId.Should().Be(LotId); receipt.Lines[0].Asset.Should().BeNull();
				receipt.Lines[1].Asset.SerialNumber.Should().Be(Canary); receipt.Lines[1].Asset.ExpiresOn.Should().Be(new DateTime(2027, 9, 9, 0, 0, 0, DateTimeKind.Utc));
				JsonConvert.SerializeObject(receipt).Should().NotContain("UnitCost").And.NotContain("QuantityReceived").And.NotContain("body-grant");
				_stock.Invocations.Should().BeEmpty();
			});
		}

		[TestCase("OrderPurchaseOrder")]
		[TestCase("CancelPurchaseOrder")]
		[TestCase("ReceivePurchaseOrder")]
		[TestCase("SavePurchaseOrder")]
		public async Task Purchasing_API_never_synthesizes_missing_or_invalid_command_identity(string action)
		{
			await WithServer(async client =>
			{
				SignIn(client);
				foreach (var invalid in new[] { null, "", Guid.Empty.ToString("D"), "not-a-guid" })
				{
					var input = JObject.FromObject(new { Id = PurchaseOrderId, PurchaseOrderId, Revision = 0, VendorId, CurrencyCode = "USD", Number = "Synthetic PO",
						Lines = new[] { new { PurchaseOrderItemId = PurchaseLineId, ItemId, LocationId, Quantity = 1, QuantityOrdered = 1, UnitCost = 2 } } });
					input[action == "SavePurchaseOrder" ? "Id" : "RequestId"] = invalid;
					var response = await client.PostAsync(Route + action, Json(input));
					response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
					response.Headers.CacheControl.NoStore.Should().BeTrue();
				}
				_purchasing.Invocations.Should().BeEmpty(); _stock.Invocations.Should().BeEmpty();
			});
		}

		[TestCase(403, "ProtectedDataRequired")]
		[TestCase(404, "Unavailable")]
		[TestCase(409, "PurchaseOrderStateConflict")]
		public async Task Purchasing_API_domain_errors_are_safe_and_not_cacheable(int status, string code)
		{
			_purchasing.Setup(x => x.GetPurchaseOrderAsync(It.IsAny<InventoryActor>(), PurchaseOrderId)).ThrowsAsync(new InventoryException(status, code));
			await WithServer(async client =>
			{
				SignIn(client); var response = await client.GetAsync(Route + "GetPurchaseOrder?id=" + PurchaseOrderId);
				((int)response.StatusCode).Should().Be(status); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var text = await response.Content.ReadAsStringAsync(); text.Should().Contain(code).And.NotContain(Canary);
				if (status == 403) { var problem = JObject.Parse(text); (problem["IsRedacted"] ?? problem["isRedacted"]).Value<bool>().Should().BeTrue(); }
			});
		}

		[Test]
		public async Task Purchasing_API_sanitizes_exception_and_binding_details_before_any_mutation()
		{
			_purchasing.Setup(x => x.GetPurchaseOrderAsync(It.IsAny<InventoryActor>(), PurchaseOrderId)).ThrowsAsync(new InvalidOperationException(Canary));
			await WithServer(async client =>
			{
				SignIn(client); var response = await client.GetAsync(Route + "GetPurchaseOrder?id=" + PurchaseOrderId);
				response.StatusCode.Should().Be(HttpStatusCode.Conflict); response.Headers.CacheControl.NoStore.Should().BeTrue();
				(await response.Content.ReadAsStringAsync()).Should().Contain("OperationUnavailable").And.NotContain(Canary).And.NotContain("InvalidOperationException");
				response = await client.PostAsync(Route + "SavePurchaseOrder", Json(new { Id = PurchaseOrderId, Lines = new[] { new { ItemId, QuantityOrdered = Canary, UnitCost = Canary } } }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				response = await client.PostAsync(Route + "ReceivePurchaseOrder", Json(new { PurchaseOrderId, RequestId = Guid.NewGuid().ToString("D"), Revision = 1,
					Lines = new[] { new { PurchaseOrderItemId = PurchaseLineId, LocationId, Quantity = 1, Asset = new { SerialNumber = "synthetic", ExpiresOn = Canary } } } }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				_purchasing.Verify(x => x.SavePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseOrderInput>()), Times.Never);
				_purchasing.Verify(x => x.ReceivePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseReceiptInput>()), Times.Never);
			});
		}

		[Test]
		public async Task Purchasing_API_vendor_archive_requires_the_same_delete_policy_as_other_inventory_archives()
		{
			_catalog.Setup(x => x.ArchiveAsync<InventoryVendor>(It.IsAny<InventoryActor>(), VendorId, 4)).Returns(Task.CompletedTask);
			await WithServer(async client =>
			{
				SignIn(client); var response = await client.PostAsync(Route + "ArchiveVendor", Json(new { Id = VendorId, Revision = 4 }));
				response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
				_catalog.Verify(x => x.ArchiveAsync<InventoryVendor>(It.IsAny<InventoryActor>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
				client.DefaultRequestHeaders.Add("Test-Inventory-Delete", "true");
				await Success(await client.PostAsync(Route + "ArchiveVendor", Json(new { Id = VendorId, Revision = 4, DepartmentId = 88 })));
				_catalog.Verify(x => x.ArchiveAsync<InventoryVendor>(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager"), VendorId, 4), Times.Once);
			});
		}

		[Test]
		public async Task Purchasing_MVC_posts_require_CSRF_and_bind_attended_actor_grant_and_exact_decimal_values()
		{
			ConfigurePurchasingMvc(); InventoryActor actor = null; InventoryPurchaseReceiptInput receipt = null;
			_purchasing.Setup(x => x.ReceivePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseReceiptInput>()))
				.ReturnsAsync((InventoryActor a, InventoryPurchaseReceiptInput input) => { actor = Copy(a); receipt = Copy(input); return new InventoryResult { AwaitingWitness = true, PurchaseOrderId = input.PurchaseOrderId }; });
			await WithPurchasingMvc(async (client, views) =>
			{
				(await client.GetAsync(MvcRoute + "Purchasing")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				SignIn(client); client.DefaultRequestHeaders.Add("Test-Inventory-Delete", "true");
				var page = await client.GetAsync(MvcRoute + "Purchasing"); await Success(page); var token = PurchasingCsrf(await page.Content.ReadAsStringAsync());
				var fields = new Dictionary<string, string> { ["PurchaseOrderId"] = PurchaseOrderId, ["Revision"] = "7", ["RequestId"] = Guid.NewGuid().ToString("D"),
					["Lines[0].PurchaseOrderItemId"] = PurchaseLineId, ["Lines[0].LocationId"] = LocationId, ["Lines[0].LotId"] = LotId, ["Lines[0].Quantity"] = "1.125001",
					["DepartmentId"] = "88", ["UserId"] = "body-user", ["GrantToken"] = "forged-grant", [HttpProtectedGrantContext.FormFieldName] = "form-grant" };
				foreach (var action in new[] { "SaveVendor", "ArchiveVendor", "SavePurchaseOrder", "OrderPurchaseOrder", "CancelPurchaseOrder", "ReceivePurchaseOrder", "ReopenPurchasing" })
					(await client.PostAsync(MvcRoute + action, new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.BadRequest, action);
				_purchasing.Verify(x => x.ReceivePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseReceiptInput>()), Times.Never);
				fields["__RequestVerificationToken"] = token;
				var response = await client.PostAsync(MvcRoute + "ReceivePurchaseOrder", new FormUrlEncodedContent(fields)); await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				actor.DepartmentId.Should().Be(77); actor.UserId.Should().Be("manager"); actor.GrantToken.Should().Be("form-grant");
				receipt.RequestId.Should().Be(fields["RequestId"]); receipt.Revision.Should().Be(7); receipt.Lines.Single().Quantity.Should().Be(1.125001m); receipt.Lines.Single().LotId.Should().Be(LotId);
				(await response.Content.ReadAsStringAsync()).Should().NotContain("form-grant").And.NotContain("forged-grant");
				fields.Remove("RequestId");
				response = await client.PostAsync(MvcRoute + "ReceivePurchaseOrder", new FormUrlEncodedContent(fields)); response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				_purchasing.Verify(x => x.ReceivePurchaseOrderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseReceiptInput>()), Times.Once);
			}, protectedData: true);
		}

		[Test]
		public async Task Purchasing_MVC_locked_GET_and_protected_POST_navigation_keep_the_same_selection_without_persisting_the_grant()
		{
			ConfigurePurchasingMvc();
			_purchasing.Setup(x => x.GetVendorContactsAsync(It.IsAny<InventoryActor>())).ReturnsAsync((InventoryActor actor) =>
			{
				if (actor.GrantToken != "navigation-grant") throw new InventoryException(403, "ProtectedDataRequired");
				return new List<InventoryVendorChoice>();
			});
			_purchasing.Setup(x => x.GetPurchaseOrderAsync(It.IsAny<InventoryActor>(), PurchaseOrderId)).ReturnsAsync(new InventoryPurchaseOrderDetail {
				Order = new InventoryPurchaseOrder { Id = PurchaseOrderId, DepartmentId = 77, VendorId = VendorId, CurrencyCode = "USD", Content = JsonConvert.SerializeObject(new InventoryPurchaseOrderContent { Number = Canary }) } });
			await WithPurchasingMvc(async (client, views) =>
			{
				SignIn(client); var address = MvcRoute + "Purchasing?tab=PurchaseOrders&page=3&id=" + PurchaseOrderId + "&locationId=" + LocationId + "&grantToken=navigation-grant";
				var response = await client.GetAsync(address); await Success(response); var html = await response.Content.ReadAsStringAsync();
				html.Should().NotContain(Canary).And.Contain("ReopenPurchasing"); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var locked = (InventoryWorkspaceView)views.LastView.Model; locked.Locked.Should().BeTrue(); locked.Id.Should().Be(PurchaseOrderId); locked.Page.Should().Be(3); locked.LocationId.Should().Be(LocationId);
				var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = PurchasingCsrf(html), ["tab"] = "PurchaseOrders", ["page"] = "3", ["id"] = PurchaseOrderId,
					["locationId"] = LocationId, [HttpProtectedGrantContext.FormFieldName] = "navigation-grant", [HttpProtectedGrantContext.ExpiresOnFormFieldName] = "2030-01-01T00:00:00Z" };
				response = await client.PostAsync(MvcRoute + "ReopenPurchasing", new FormUrlEncodedContent(fields)); await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				(await response.Content.ReadAsStringAsync()).Should().Contain(Canary);
				var revealed = (InventoryWorkspaceView)views.LastView.Model; revealed.Locked.Should().BeFalse(); revealed.Id.Should().Be(PurchaseOrderId); revealed.Page.Should().Be(3); revealed.LocationId.Should().Be(LocationId);
				views.LastView.ViewData["ProtectedGrant"].Should().Be("navigation-grant"); views.LastView.ViewData["GrantExpiresOn"].Should().Be(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
				_purchasing.Verify(x => x.GetPurchaseOrderAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager" && a.GrantToken == "navigation-grant"), PurchaseOrderId), Times.Once);
				response = await client.GetAsync(address); await Success(response); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				((InventoryWorkspaceView)views.LastView.Model).Locked.Should().BeTrue();
			}, protectedData: true);
		}

		[Test]
		public async Task Purchasing_MVC_conflicts_and_binding_errors_use_safe_noncacheable_responses()
		{
			ConfigurePurchasingMvc();
			_purchasing.Setup(x => x.ChangePurchaseOrderStatusAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseOrderChange>(), InventoryPurchaseOrderStatus.Ordered))
				.ThrowsAsync(new InventoryException(409, "PurchaseOrderStateConflict"));
			await WithPurchasingMvc(async (client, views) =>
			{
				SignIn(client); var page = await client.GetAsync(MvcRoute + "Purchasing"); await Success(page);
				var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = PurchasingCsrf(await page.Content.ReadAsStringAsync()), ["Id"] = PurchaseOrderId,
					["Revision"] = "3", ["RequestId"] = Guid.NewGuid().ToString("D"), ["Note"] = Canary };
				var response = await client.PostAsync(MvcRoute + "OrderPurchaseOrder", new FormUrlEncodedContent(fields)); response.StatusCode.Should().Be(HttpStatusCode.Conflict);
				response.Headers.CacheControl.NoStore.Should().BeTrue(); (await response.Content.ReadAsStringAsync()).Should().Contain("PurchaseOrderStateConflict").And.NotContain(Canary);
				fields["Revision"] = Canary;
				response = await client.PostAsync(MvcRoute + "OrderPurchaseOrder", new FormUrlEncodedContent(fields)); response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				response.Headers.CacheControl.NoStore.Should().BeTrue(); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
				_purchasing.Verify(x => x.ChangePurchaseOrderStatusAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryPurchaseOrderChange>(), InventoryPurchaseOrderStatus.Ordered), Times.Once);
			});
		}

		private void ConfigurePurchasingMvc()
		{
			_catalog.Setup(x => x.ListAsync<InventoryVendor>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryVendor>());
			_catalog.Setup(x => x.ListAsync<InventoryPurchaseOrder>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryPurchaseOrder>());
			_catalog.Setup(x => x.ListAsync<InventoryLocation>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryLocation>());
			_catalog.Setup(x => x.ListAsync<InventoryLot>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryLot>());
			_purchasing.Setup(x => x.GetVendorContactsAsync(It.IsAny<InventoryActor>())).ReturnsAsync(new List<InventoryVendorChoice>());
		}
		private static string PurchasingCsrf(string html)
		{
			var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
			token.Should().NotBeNullOrEmpty("the real Purchasing view must issue an antiforgery token"); return token;
		}
		private sealed class PurchasingBodyFilter : IResultFilter
		{
			public ViewResult LastView { get; private set; }
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is not ViewResult view) return;
				LastView = view;
				context.Result = new PartialViewResult { ViewName = "/Areas/User/Views/Inventory/" + view.ViewName + ".cshtml", ViewData = view.ViewData, TempData = view.TempData, StatusCode = view.StatusCode };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}
		private async Task WithPurchasingMvc(Func<HttpClient, PurchasingBodyFilter, Task> test, bool protectedData = false)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			if (directory == null) throw new InvalidOperationException("The test workspace root is unavailable.");
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(directory.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(scheme, _ => { });
			builder.Services.AddAuthorization(options => options.AddPolicy(Resgrid.Providers.Claims.ResgridResources.Inventory_Delete, policy => policy.RequireClaim("inventory-delete", "true")));
			var views = new PurchasingBodyFilter();
			builder.Services.AddControllersWithViews(options => options.Filters.Add(views)).AddApplicationPart(typeof(MvcInventoryController).Assembly)
				.AddNewtonsoftJson(options => options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			builder.Services.AddSingleton(_catalog.Object); builder.Services.AddSingleton(_stock.Object); builder.Services.AddSingleton(_transfers.Object);
			builder.Services.AddSingleton(_issuance.Object); builder.Services.AddSingleton(_migration.Object); builder.Services.AddSingleton(_authorization.Object); builder.Services.AddSingleton(_purchasing.Object);
			builder.Services.AddSingleton(Mock.Of<IUnitsService>()); builder.Services.AddSingleton(Mock.Of<IDepartmentGroupsService>()); builder.Services.AddSingleton(Mock.Of<IDepartmentsService>());
			builder.Services.AddSingleton<IProtectedGrantContext, HttpProtectedGrantContext>();
			var protection = new Mock<IDepartmentDataProtectionService>(); protection.Setup(x => x.IsProtectionEnforcedAsync(77)).ReturnsAsync(protectedData); builder.Services.AddSingleton(protection.Object);
			await using var app = builder.Build(); var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("fr").AddSupportedCultures("fr").AddSupportedUICultures("fr"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync(); using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
				using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) }; await test(client, views);
			}
			finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}
	}
}
