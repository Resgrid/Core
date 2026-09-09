using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Inventory;
using Resgrid.Web.Helpers;
using InventoryStrings = Resgrid.Localization.Areas.User.Inventory.Inventory;

namespace Resgrid.Tests.Web.User
{
	[TestFixture, NonParallelizable]
	public sealed class InventoryWorkspaceTests
	{
		private const int DepartmentId = 77;
		private const string UserId = "inventory-member";
		private const string ItemId = "11111111-1111-1111-1111-111111111111";
		private const string LocationId = "22222222-2222-2222-2222-222222222222";
		private const string AssetId = "33333333-3333-3333-3333-333333333333";
		private Mock<IInventoryCatalogService> _catalog;
		private Mock<IInventoryAuthorizationService> _authorization;
		private Mock<IInventoryMigrationService> _migration;
		private Mock<IDepartmentDataProtectionService> _protection;
		private InventoryController _controller;
		private DefaultHttpContext _http;
		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp()
		{
			_catalog = new(); _authorization = new(); _migration = new(); _protection = new();
			_authorization.Setup(s => s.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<PermissionTypes?>(), It.IsAny<int?>())).Returns(Task.CompletedTask);
			_authorization.Setup(s => s.CanLocationAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryLocation>())).ReturnsAsync(true);
			_migration.Setup(s => s.IsMigratedAsync(DepartmentId)).ReturnsAsync(true);
			Empty<InventoryItem>(); Empty<InventoryCategory>(); Empty<InventoryLocation>(); Empty<InventoryAsset>(); Empty<InventoryLot>();
			Empty<InventoryStock>(); Empty<InventoryIssuance>(); Empty<InventoryKit>(); Empty<InventoryKitItem>(); Empty<InventoryTransaction>();
			_catalog.Setup(s => s.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId)).ReturnsAsync(new InventoryItem { Id = ItemId });
			_catalog.Setup(s => s.GetAsync<InventoryLocation>(It.IsAny<InventoryActor>(), LocationId)).ReturnsAsync(new InventoryLocation { Id = LocationId });
			_catalog.Setup(s => s.GetAsync<InventoryAsset>(It.IsAny<InventoryActor>(), AssetId)).ReturnsAsync(new InventoryAsset { Id = AssetId, ItemId = ItemId });
			var units = new Mock<IUnitsService>(); units.Setup(s => s.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			var groups = new Mock<IDepartmentGroupsService>(); groups.Setup(s => s.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>());
			groups.Setup(s => s.GetGroupForUserAsync(UserId, DepartmentId)).ReturnsAsync(new DepartmentGroup { DepartmentId = DepartmentId, DepartmentGroupId = 9 });
			var departments = new Mock<IDepartmentsService>(); departments.Setup(s => s.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>());
			var strings = new Mock<IStringLocalizer<InventoryStrings>>(); strings.Setup(s => s[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			_http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
			_http.Request.Method = "GET";
			var accessor = new HttpContextAccessor { HttpContext = _http }; _previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor; ClaimsAuthorizationHelper._httpContextAccessor = accessor;
			_controller = new InventoryController(_catalog.Object, Mock.Of<IInventoryStockService>(), Mock.Of<IInventoryTransferService>(), Mock.Of<IInventoryIssuanceService>(),
				_migration.Object, _authorization.Object, new HttpProtectedGrantContext(accessor), _protection.Object, units.Object, groups.Object, departments.Object, strings.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private void Empty<T>() where T : InventoryRow
		{
			_catalog.Setup(s => s.ListAsync<T>(It.IsAny<InventoryActor>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<T>());
			_catalog.Setup(s => s.QueryAsync<T>(It.IsAny<InventoryActor>(), It.IsAny<InventoryQuery>(), It.IsAny<int>())).ReturnsAsync(new InventoryPage<T>());
		}
		private static InventoryWorkspaceView Model(IActionResult result) => (InventoryWorkspaceView)((ViewResult)result).Model;
		private ActionExecutingContext Context(Dictionary<string, object> arguments = null) => new(new ActionContext(_http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(), arguments ?? new(), _controller);

		[TestCase(PermissionTypes.IssueInventory)]
		[TestCase(PermissionTypes.TransferInventory)]
		[TestCase(PermissionTypes.ManageControlledSubstances)]
		public async Task Specialized_permissions_expose_their_action_without_general_adjust_access(PermissionTypes granted)
		{
			_authorization.Setup(s => s.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<PermissionTypes?>(), It.IsAny<int?>()))
				.Returns((InventoryActor actor, bool write, PermissionTypes? permission, int? group) => !write || permission == granted ? Task.CompletedTask : Task.FromException(new InventoryException(403, "PermissionRequired")));
			var view = Model(await _controller.Index());
			view.CanWrite.Should().BeFalse(); view.CanIssue.Should().Be(granted == PermissionTypes.IssueInventory);
			view.CanTransfer.Should().Be(granted == PermissionTypes.TransferInventory); view.CanWitness.Should().Be(granted == PermissionTypes.ManageControlledSubstances);
			_authorization.Verify(s => s.RequireAsync(It.Is<InventoryActor>(a => a.DepartmentId == DepartmentId && a.UserId == UserId), true, granted, 9), Times.Once);
		}

		[Test]
		public async Task Protected_GET_keeps_structural_selection_for_attended_unlock_without_exposing_rows()
		{
			_protection.Setup(s => s.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(true);
			var context = Context(new() { ["tab"] = "History", ["page"] = 3, ["id"] = AssetId, ["unitId"] = 42, ["userId"] = "holder", ["itemId"] = ItemId, ["locationId"] = LocationId });
			var executed = new ActionExecutedContext(context, new List<IFilterMetadata>(), _controller) { Exception = new InventoryException(409, "ProtectedDataRequired") };
			await _controller.OnActionExecutionAsync(context, () => Task.FromResult(executed));
			executed.ExceptionHandled.Should().BeTrue(); var view = Model(executed.Result);
			view.Locked.Should().BeTrue(); view.Tab.Should().Be("History"); view.Page.Should().Be(3); view.Id.Should().Be(AssetId);
			view.UnitId.Should().Be(42); view.UserId.Should().Be("holder"); view.ItemId.Should().Be(ItemId); view.LocationId.Should().Be(LocationId);
			view.Rows.Should().BeEmpty(); view.Items.Should().BeEmpty(); _http.Response.Headers.CacheControl.ToString().Should().Be("no-store");
		}

		[TestCase("OnHand")]
		[TestCase("History")]
		public async Task Reopen_keeps_request_grant_expiry_and_passes_item_location_filters_before_paging(string tab)
		{
			_http.Request.Method = "POST"; _http.Request.ContentType = "application/x-www-form-urlencoded";
			_http.Request.QueryString = new QueryString("?grantToken=forged&userId=other&departmentId=88");
			_http.Request.Form = new FormCollection(new Dictionary<string, StringValues> { [HttpProtectedGrantContext.FormFieldName] = "synthetic-form-grant", [HttpProtectedGrantContext.ExpiresOnFormFieldName] = "2030-01-01T00:00:00Z" });
			_protection.Setup(s => s.IsProtectionEnforcedAsync(DepartmentId)).ReturnsAsync(true);
			var context = Context(); var executed = new ActionExecutedContext(context, new List<IFilterMetadata>(), _controller);
			await _controller.OnActionExecutionAsync(context, async () => { executed.Result = await _controller.Reopen(tab, page: 2, itemId: ItemId, locationId: LocationId); return executed; });
			var view = Model(executed.Result); view.ItemId.Should().Be(ItemId); view.LocationId.Should().Be(LocationId); view.Page.Should().Be(2);
			((string)_controller.ViewBag.ProtectedGrant).Should().Be("synthetic-form-grant"); ((DateTime?)_controller.ViewBag.GrantExpiresOn).Should().Be(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			if (tab == "OnHand") _catalog.Verify(s => s.QueryAsync<InventoryStock>(It.Is<InventoryActor>(a => a.DepartmentId == DepartmentId && a.UserId == UserId && a.GrantToken == "synthetic-form-grant"), It.Is<InventoryQuery>(q => q.ItemId == ItemId && q.LocationId == LocationId), 2), Times.Once);
			else _catalog.Verify(s => s.QueryAsync<InventoryTransaction>(It.Is<InventoryActor>(a => a.DepartmentId == DepartmentId && a.UserId == UserId && a.GrantToken == "synthetic-form-grant"), It.Is<InventoryQuery>(q => q.ItemId == ItemId && q.LocationId == LocationId), 2), Times.Once);
		}

		[Test]
		public async Task Asset_history_and_personnel_gear_pass_subject_filter_before_paging()
		{
			var transaction = new InventoryTransaction { AssetId = AssetId, ItemId = ItemId, EntryId = 900 };
			_catalog.Setup(s => s.QueryAsync<InventoryTransaction>(It.IsAny<InventoryActor>(), It.Is<InventoryQuery>(q => q.AssetId == AssetId), 4)).ReturnsAsync(new InventoryPage<InventoryTransaction> { Items = new() { transaction }, HasMore = true });
			var asset = Model(await _controller.Index("AssetDetail", page: 4, id: AssetId));
			asset.Rows.Should().Contain(transaction); asset.HasMore.Should().BeTrue();
			await _controller.Index("PersonnelGear", page: 3, userId: "holder");
			_catalog.Verify(s => s.QueryAsync<InventoryIssuance>(It.IsAny<InventoryActor>(), It.Is<InventoryQuery>(q => q.IssuedToUserId == "holder"), 3), Times.Once);
			_catalog.Verify(s => s.ListAsync<InventoryTransaction>(It.IsAny<InventoryActor>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Kits_load_complete_BOMs_for_each_visible_kit_instead_of_a_global_first_page()
		{
			var first = new InventoryKit(); var second = new InventoryKit();
			_catalog.Setup(s => s.ListAsync<InventoryKit>(It.IsAny<InventoryActor>(), 0)).ReturnsAsync(new InventoryPage<InventoryKit> { Items = new() { first, second } });
			var firstLine = new InventoryKitItem { KitId = first.Id, ItemId = ItemId, Quantity = 1 };
			var otherLine = new InventoryKitItem { KitId = second.Id, ItemId = ItemId, Quantity = 3 };
			_catalog.Setup(s => s.QueryAsync<InventoryKitItem>(It.IsAny<InventoryActor>(), It.Is<InventoryQuery>(q => q.KitId == first.Id), 0)).ReturnsAsync(new InventoryPage<InventoryKitItem> { Items = new() { firstLine } });
			_catalog.Setup(s => s.QueryAsync<InventoryKitItem>(It.IsAny<InventoryActor>(), It.Is<InventoryQuery>(q => q.KitId == second.Id), 0)).ReturnsAsync(new InventoryPage<InventoryKitItem> { Items = new() { otherLine } });
			var view = Model(await _controller.Index("Kits"));
			view.KitContents.Should().BeEquivalentTo(new[] { firstLine, otherLine });
			view.Items.Select(item => item.Id).Should().Contain(ItemId);
			_catalog.Verify(s => s.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId), Times.Once);
			_catalog.Verify(s => s.ListAsync<InventoryKitItem>(It.IsAny<InventoryActor>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Incomplete_kit_contents_fail_closed_before_an_edit_form_can_replace_the_BOM()
		{
			var kit = new InventoryKit();
			_catalog.Setup(s => s.ListAsync<InventoryKit>(It.IsAny<InventoryActor>(), 0)).ReturnsAsync(new InventoryPage<InventoryKit> { Items = new() { kit } });
			_catalog.Setup(s => s.QueryAsync<InventoryKitItem>(It.IsAny<InventoryActor>(), It.Is<InventoryQuery>(q => q.KitId == kit.Id), 0)).ReturnsAsync(new InventoryPage<InventoryKitItem> { Items = new() { new InventoryKitItem { KitId = kit.Id, ItemId = ItemId, Quantity = 1 } }, HasMore = true });
			Func<Task> open = async () => await _controller.Index("Kits");
			(await open.Should().ThrowAsync<InventoryException>()).Which.Code.Should().Be("InventoryTooLarge");
		}
	}
}
