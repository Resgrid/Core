using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using ApiController = Resgrid.Web.Services.Controllers.v4.InventoryController;

namespace Resgrid.Tests.Services
{
	/// <summary>Real MVC binding, authentication and action filters around mocked domain boundaries.</summary>
	[TestFixture, NonParallelizable]
	public sealed partial class InventoryApiTests
	{
		private const string Route = "/api/v4/Inventory/";
		private const string Canary = "SYNTHETIC-INVENTORY-API-PHI-CANARY";
		private const string ItemId = "11111111-1111-1111-1111-111111111111";
		private const string LocationId = "22222222-2222-2222-2222-222222222222";
		private const string LotId = "33333333-3333-3333-3333-333333333333";
		private Mock<IInventoryCatalogService> _catalog;
		private Mock<IInventoryStockService> _stock;
		private Mock<IInventoryTransferService> _transfers;
		private Mock<IInventoryIssuanceService> _issuance;
		private Mock<IInventoryMigrationService> _migration;
		private Mock<IInventoryAuthorizationService> _authorization;
		private Mock<IInventoryPurchasingService> _purchasing;
		private List<(InventoryActor Actor, InventoryCommand Command)> _posts;

		[SetUp]
		public void SetUp()
		{
			_catalog = new(); _stock = new(); _transfers = new(); _issuance = new(); _migration = new(); _authorization = new(); _purchasing = new(); _posts = new();
			_catalog.Setup(s => s.ListAsync<InventoryItem>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<InventoryItem>());
			_stock.Setup(s => s.PostTransactionAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCommand>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((InventoryActor actor, InventoryCommand command, CancellationToken ct) =>
				{
					_posts.Add((Copy(actor), Copy(command))); return new InventoryResult { OperationId = Guid.NewGuid().ToString("D"), TransactionIds = new() { Guid.NewGuid().ToString("D") } };
				});
			_authorization.Setup(s => s.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<Resgrid.Model.PermissionTypes?>(), It.IsAny<int?>())).Returns(Task.CompletedTask);
			_migration.Setup(s => s.IsMigratedAsync(77)).ReturnsAsync(true);
		}

		[Test]
		public async Task Http_routes_require_authentication_and_source_tenant_member_and_grant_only_from_request_context()
		{
			await WithServer(async client =>
			{
				(await client.GetAsync(Route + "GetAll")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
				SignIn(client); client.DefaultRequestHeaders.Add(DataProtectionController.GrantHeader, "synthetic-header-grant");
				var response = await client.GetAsync(Route + "GetAll?departmentId=88&userId=body-user&grantToken=body-grant");
				await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				_catalog.Verify(s => s.ListAsync<InventoryItem>(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager" && a.GrantToken == "synthetic-header-grant"), 0), Times.Once);
				var requestId = Guid.NewGuid().ToString("D");
				response = await client.PostAsync(Route + "PostTransaction", Json(new { RequestId = requestId, DepartmentId = 88, UserId = "body-user", GrantToken = "body-grant",
					Actor = new { DepartmentId = 88, UserId = "body-user", GrantToken = "body-grant" },
					Lines = new[] { new { ItemId, ToLocationId = LocationId, Quantity = 2.125001m, Type = (int)InventoryTransactionType.Receive, Note = Canary } } }));
				await Success(response); response.Headers.CacheControl.NoStore.Should().BeTrue();
				var captured = _posts.Single(); captured.Actor.DepartmentId.Should().Be(77); captured.Actor.UserId.Should().Be("manager"); captured.Actor.GrantToken.Should().Be("synthetic-header-grant");
				captured.Command.RequestId.Should().Be(requestId); captured.Command.Lines.Single().Quantity.Should().Be(2.125001m);
				(await response.Content.ReadAsStringAsync()).Should().NotContain("synthetic-header-grant").And.NotContain(Canary);
			});
		}

		[Test]
		public async Task Omitted_request_ids_and_invalid_identifier_shapes_do_not_invoke_mutating_services()
		{
			await WithServer(async client =>
			{
				SignIn(client);
				foreach (var action in new[] { "PostTransaction", "CreateTransfer", "CreateAsset", "Issue", "Return", "StatusChange", "IssueKit", "Witness" })
				{
					var response = await client.PostAsync(Route + action, Json(new { ItemId, ToLocationId = LocationId, Quantity = 1, Details = new { SerialNumber = "Synthetic serial" }, Attestation = "Count verified",
						Lines = new[] { new { ItemId, ToLocationId = LocationId, Quantity = 1, Type = 1 } } }));
					response.StatusCode.Should().Be(HttpStatusCode.BadRequest, action + ": " + await response.Content.ReadAsStringAsync());
				}
				foreach (var requestId in new[] { Guid.Empty.ToString("D"), "not-a-request-id" })
				{
					var response = await client.PostAsync(Route + "PostTransaction", Json(new { RequestId = requestId, Lines = new[] { new { ItemId, ToLocationId = LocationId, Quantity = 1, Type = 1 } } }));
					response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
				}
				_posts.Should().BeEmpty(); _transfers.Invocations.Should().BeEmpty(); _issuance.Invocations.Should().BeEmpty(); _stock.Invocations.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Update_compatibility_route_posts_an_explicit_directional_delta_and_carries_lot_and_asset_revision()
		{
			await WithServer(async client =>
			{
				SignIn(client); var requestId = Guid.NewGuid().ToString("D"); var assetId = Guid.NewGuid().ToString("D");
				var response = await client.PutAsync(Route + "UpdateItem", Json(new { RequestId = requestId, ItemId, AssetId = assetId, LotId,
					FromLocationId = LocationId, Quantity = 1.125001m, ExpectedAssetRevision = 7, Note = Canary, Amount = 999m, InventoryId = 45 }));
				await Success(response); var command = _posts.Single().Command; command.RequestId.Should().Be(requestId); var line = command.Lines.Single();
				line.Type.Should().Be(InventoryTransactionType.Adjust); line.Quantity.Should().Be(1.125001m); line.FromLocationId.Should().Be(LocationId); line.ToLocationId.Should().BeNull();
				line.ItemId.Should().Be(ItemId); line.AssetId.Should().Be(assetId); line.LotId.Should().Be(LotId); line.ExpectedAssetRevision.Should().Be(7);
				response = await client.PutAsync(Route + "UpdateItem", Json(new { RequestId = Guid.NewGuid().ToString("D"), ItemId, ToLocationId = LocationId, Quantity = 2m }));
				await Success(response); _posts.Last().Command.Lines.Single().FromLocationId.Should().BeNull(); _posts.Last().Command.Lines.Single().ToLocationId.Should().Be(LocationId);
				foreach (var invalid in new object[] {
					new { RequestId = Guid.NewGuid().ToString("D"), ItemId, Quantity = 9m, Amount = 9m },
					new { RequestId = Guid.NewGuid().ToString("D"), ItemId, FromLocationId = LocationId, ToLocationId = LotId, Quantity = 9m },
					new { RequestId = Guid.NewGuid().ToString("D"), ItemId, ToLocationId = LocationId, Quantity = -9m } })
				{
					response = await client.PutAsync(Route + "UpdateItem", Json(invalid)); response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
					(await response.Content.ReadAsStringAsync()).Should().Contain("ExplicitAdjustmentRequired");
				}
				_posts.Should().HaveCount(2);
			});
		}

		[Test]
		public async Task Lot_input_cannot_overpost_identity_tenant_protection_markers_or_raw_content()
		{
			InventoryActor capturedActor = null; InventoryLot capturedLot = null; InventoryLotContent capturedDetails = null;
			_catalog.Setup(s => s.SaveLotAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryLot>(), It.IsAny<InventoryLotContent>()))
				.ReturnsAsync((InventoryActor actor, InventoryLot lot, InventoryLotContent details) => { capturedActor = Copy(actor); capturedLot = Copy(lot); capturedDetails = Copy(details); return lot; });
			await WithServer(async client =>
			{
				SignIn(client); var forgedId = Guid.NewGuid().ToString("D"); var expires = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				var response = await client.PostAsync(Route + "CreateLot", Json(new { Id = forgedId, DepartmentId = 88, Revision = 500, IsDeleted = true, IsProtected = true,
					CreatedBy = "other-member", CreatedOn = new DateTime(2000, 1, 1), ReceivedOn = new DateTime(2000, 1, 1), Content = Canary, ItemId, ExpiresOn = expires,
					Details = new { LotNumber = "Synthetic lot 2027", UnitCost = 1.125001m } }));
				await Success(response); capturedActor.DepartmentId.Should().Be(77); capturedLot.Id.Should().NotBe(forgedId); Guid.TryParseExact(capturedLot.Id, "D", out _).Should().BeTrue();
				capturedLot.DepartmentId.Should().Be(0); capturedLot.Revision.Should().Be(1); capturedLot.IsDeleted.Should().BeFalse(); capturedLot.IsProtected.Should().BeFalse();
				capturedLot.Content.Should().BeNull(); capturedLot.CreatedBy.Should().BeNull(); capturedLot.CreatedOn.Should().Be(default(DateTime)); capturedLot.ReceivedOn.Should().Be(default(DateTime));
				capturedLot.ItemId.Should().Be(ItemId); capturedLot.ExpiresOn.Should().Be(expires); capturedDetails.LotNumber.Should().Be("Synthetic lot 2027"); capturedDetails.UnitCost.Should().Be(1.125001m);
			});
		}

		[Test]
		public async Task Protected_and_conflict_failures_return_safe_problem_codes_without_cacheable_or_raw_errors()
		{
			_catalog.Setup(s => s.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId)).ThrowsAsync(new InventoryException(403, "ProtectedDataRequired"));
			await WithServer(async client =>
			{
				SignIn(client); var response = await client.GetAsync(Route + "GetItem?itemId=" + ItemId); var text = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.Forbidden); response.Headers.CacheControl.NoStore.Should().BeTrue(); text.Should().Contain("protected_data_required").And.Contain("ProtectedDataRequired");
				var problem = JObject.Parse(text); (problem["IsRedacted"] ?? problem["isRedacted"]).Value<bool>().Should().BeTrue();
				_catalog.Setup(s => s.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId)).ThrowsAsync(new InvalidOperationException(Canary));
				response = await client.GetAsync(Route + "GetItem?itemId=" + ItemId); text = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.Conflict); response.Headers.CacheControl.NoStore.Should().BeTrue(); text.Should().Contain("OperationUnavailable").And.NotContain(Canary).And.NotContain("InvalidOperationException");
				response = await client.PostAsync(Route + "PostTransaction", Json(new { RequestId = Guid.NewGuid().ToString("D"), Lines = new[] { new { ItemId, Quantity = Canary, Type = 1 } } }));
				response.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary); _posts.Should().BeEmpty();
			});
		}

		[Test]
		public async Task Low_stock_uses_authorized_totals_for_the_catalog_page_and_reports_the_visibility_scope()
		{
			_catalog.Setup(s => s.ListAsync<InventoryItem>(It.IsAny<InventoryActor>(), 2)).ReturnsAsync(new InventoryPage<InventoryItem> { HasMore = true,
				Items = new() { new InventoryItem { Id = ItemId, DepartmentId = 77, Content = JsonConvert.SerializeObject(new InventoryItemContent { Name = "Synthetic gloves", UnitOfMeasure = "pair", ReorderPoint = 5m }) } } });
			_stock.Setup(s => s.GetVisibleQuantitiesAsync(It.IsAny<InventoryActor>(), It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains(ItemId))))
				.ReturnsAsync(new Dictionary<string, decimal> { [ItemId] = 3.125m });
			await WithServer(async client =>
			{
				SignIn(client); var response = await client.GetAsync(Route + "GetLowStockItems?page=2"); await Success(response);
				var json = JObject.Parse(await response.Content.ReadAsStringAsync()); var row = json["Data"]["Items"].Single();
				row.Value<decimal>("VisibleQuantity").Should().Be(3.125m); row.Value<string>("QuantityScope").Should().Be("AuthorizedLocations");
				json.Value<bool>("HasMore").Should().BeTrue();
				_stock.Verify(s => s.GetVisibleQuantitiesAsync(It.Is<InventoryActor>(a => a.DepartmentId == 77 && a.UserId == "manager"), It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
				_catalog.Verify(s => s.ListAsync<InventoryStock>(It.IsAny<InventoryActor>(), It.IsAny<int>()), Times.Never);
			});
		}

		private static StringContent Json(object value) => new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
		private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
		private static void SignIn(HttpClient client) => client.DefaultRequestHeaders.Add("Test-Member", "manager");
		private static async Task Success(HttpResponseMessage response) => response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
		private async Task WithServer(Func<HttpClient, Task> test)
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddApiVersioning();
			const string scheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
			builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(scheme, _ => { });
			builder.Services.AddAuthorization(options => options.AddPolicy(Resgrid.Providers.Claims.ResgridResources.Inventory_Delete, policy => policy.RequireClaim("inventory-delete", "true")));
			builder.Services.AddControllers().AddApplicationPart(typeof(ApiController).Assembly).AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
			builder.Services.AddSingleton(_catalog.Object); builder.Services.AddSingleton(_stock.Object); builder.Services.AddSingleton(_transfers.Object);
			builder.Services.AddSingleton(_issuance.Object); builder.Services.AddSingleton(_migration.Object); builder.Services.AddSingleton(_authorization.Object);
			builder.Services.AddSingleton(_purchasing.Object);
			await using var app = builder.Build(); var previous = ApiClaims._httpContextAccessor; ApiClaims._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
			try { await app.StartAsync(); using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }; await test(client); }
			finally { await app.StopAsync(); ApiClaims._httpContextAccessor = previous; }
		}
		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				if (!Request.Headers.TryGetValue("Test-Member", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
				var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, user.ToString()), new Claim(ClaimTypes.PrimaryGroupSid, "77") };
				if (Request.Headers["Test-Inventory-Delete"] == "true") claims.Add(new Claim("inventory-delete", "true"));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
			}
		}
	}
}
