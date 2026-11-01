using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Dashboard.Widgets.Readiness;
using static Resgrid.Tests.Dashboard.Widgets.ReadinessRecordsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>inventory.alerts, inventory.valuation and the alert summary they read (plan §7.7, §9A.4).</summary>
	[TestFixture]
	public class InventoryWidgetProviderTests
	{
		private static bool IsViewerActor(InventoryActor actor) => actor.DepartmentId == DepartmentId && actor.UserId == UserId;

		[Test]
		public async Task Alerts_map_the_summary_counts_and_items()
		{
			var operations = new Mock<IInventoryOperationsService>(MockBehavior.Strict);
			var migration = new Mock<IInventoryMigrationService>(MockBehavior.Strict);
			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(true);
			operations.Setup(o => o.GetAlertSummaryAsync(It.Is<InventoryActor>(a => IsViewerActor(a) && a.GrantToken == Grant), 5)).ReturnsAsync(new InventoryAlertSummary
			{
				Total = 6,
				Truncated = true,
				CountsByType = new Dictionary<int, int> { [(int)InventoryAlertType.LowStock] = 2, [(int)InventoryAlertType.Expired] = 3, [(int)InventoryAlertType.DepartedHolder] = 1 },
				Top = new List<InventoryAlertSummaryItem>
				{
					new InventoryAlertSummaryItem { AlertType = (int)InventoryAlertType.Expired, ItemId = "a", ItemName = "Gauze", Count = 3 },
					new InventoryAlertSummaryItem { AlertType = (int)InventoryAlertType.LowStock, ItemId = "b", ItemName = ProtectedDataEnvelope.RedactionValue, Count = 2 }
				}
			});

			var provider = new InventoryAlertsWidgetProvider(operations.Object, migration.Object, Mock.Of<IInventoryAuthorizationService>(MockBehavior.Strict), Mock.Of<IInventoryService>(MockBehavior.Strict));
			var data = (InventoryAlertsWidgetData)await provider.GetDataAsync(Context("inventory.alerts", grant: Grant), CancellationToken.None);

			data.Migrated.Should().BeTrue();
			data.LowStock.Should().Be(2);
			data.Expired.Should().Be(3);
			data.DepartedHolder.Should().Be(1);
			data.ExpiringSoon.Should().Be(0);
			data.Total.Should().Be(6);
			data.Truncated.Should().BeTrue();
			data.Items.Select(i => i.Tone).Should().Equal("danger", "warning");
			data.Items[1].ItemName.Should().BeNull();
		}

		[Test]
		public async Task Alerts_before_initialization_count_the_departments_own_legacy_low_stock()
		{
			var migration = new Mock<IInventoryMigrationService>(MockBehavior.Strict);
			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(false);
			var authorization = new Mock<IInventoryAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(a => a.RequireAsync(It.Is<InventoryActor>(x => IsViewerActor(x)), false, null, null)).Returns(Task.CompletedTask);
			var legacy = new Mock<IInventoryService>(MockBehavior.Strict);
			legacy.Setup(l => l.GetConsolidatedInventoryForDepartment(DepartmentId)).ReturnsAsync(new List<Inventory>
			{
				new Inventory { DepartmentId = DepartmentId, TypeId = 1, Amount = 0 },
				new Inventory { DepartmentId = DepartmentId, TypeId = 2, Amount = -2 },
				new Inventory { DepartmentId = DepartmentId, TypeId = 3, Amount = 12 },
				new Inventory { DepartmentId = OtherDepartmentId, TypeId = 4, Amount = 0 }
			});
			legacy.Setup(l => l.GetAllTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<InventoryType>
			{
				new InventoryType { InventoryTypeId = 1, DepartmentId = DepartmentId, Type = "Gloves" },
				new InventoryType { InventoryTypeId = 2, DepartmentId = DepartmentId, Type = "Saline" }
			});

			var provider = new InventoryAlertsWidgetProvider(Mock.Of<IInventoryOperationsService>(MockBehavior.Strict), migration.Object, authorization.Object, legacy.Object);
			var data = (InventoryAlertsWidgetData)await provider.GetDataAsync(Context("inventory.alerts"), CancellationToken.None);

			data.Migrated.Should().BeFalse();
			data.LowStock.Should().Be(2, "another department's rows never count");
			data.Items.Select(i => i.ItemName).Should().Equal("Saline", "Gloves");
		}

		[Test]
		public async Task Alerts_refuse_a_non_member_and_map_the_protected_sentinel()
		{
			var migration = new Mock<IInventoryMigrationService>();
			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(false);
			var authorization = new Mock<IInventoryAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(a => a.RequireAsync(It.IsAny<InventoryActor>(), false, null, null)).ThrowsAsync(new InventoryException(403, "MembershipRequired"));
			var legacy = new Mock<IInventoryService>(MockBehavior.Strict);

			var provider = new InventoryAlertsWidgetProvider(Mock.Of<IInventoryOperationsService>(MockBehavior.Strict), migration.Object, authorization.Object, legacy.Object);
			(await StateAsync(() => provider.GetDataAsync(Context("inventory.alerts"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
			legacy.VerifyNoOtherCalls();

			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(true);
			var operations = new Mock<IInventoryOperationsService>(MockBehavior.Strict);
			operations.Setup(o => o.GetAlertSummaryAsync(It.IsAny<InventoryActor>(), It.IsAny<int>())).ThrowsAsync(new InventoryException(403, "ProtectedDataRequired"));
			provider = new InventoryAlertsWidgetProvider(operations.Object, migration.Object, authorization.Object, legacy.Object);
			(await StateAsync(() => provider.GetDataAsync(Context("inventory.alerts"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Protected);
		}

		[Test]
		public async Task Valuation_totals_by_holder_with_names_from_the_authorized_catalog()
		{
			var migration = new Mock<IInventoryMigrationService>(MockBehavior.Strict);
			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(true);
			var purchasing = new Mock<IInventoryPurchasingService>(MockBehavior.Strict);
			purchasing.Setup(p => p.GetValuationAsync(It.Is<InventoryActor>(a => IsViewerActor(a)), null)).ReturnsAsync(new InventoryValuation
			{
				AsOfUtc = Now,
				Lines = new List<InventoryValuationLine>
				{
					new InventoryValuationLine { LocationId = "station", CurrencyCode = "USD", Value = 100 },
					new InventoryValuationLine { LocationId = "station", CurrencyCode = "USD", Value = 50 },
					new InventoryValuationLine { LocationId = "engine", CurrencyCode = "USD", Value = 400 },
					new InventoryValuationLine { LocationId = "engine", CurrencyCode = "USD", Value = null }
				},
				Totals = new List<InventoryValuationTotal> { new InventoryValuationTotal { CurrencyCode = "USD", KnownValue = 550, UncostedRows = 1 } }
			});
			var catalog = new Mock<IInventoryCatalogService>(MockBehavior.Strict);
			catalog.Setup(c => c.GetManyAsync<InventoryLocation>(It.IsAny<InventoryActor>(), It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(new Dictionary<string, InventoryLocation>
			{
				["engine"] = new InventoryLocation { Id = "engine", DepartmentId = DepartmentId, Content = "{\"Name\":\"Engine 1\"}" },
				["station"] = new InventoryLocation { Id = "station", DepartmentId = DepartmentId, Content = "{\"Name\":\"Station 1\"}" }
			});

			var data = (InventoryValuationWidgetData)await new InventoryValuationWidgetProvider(purchasing.Object, migration.Object, catalog.Object)
				.GetDataAsync(Context("inventory.valuation"), CancellationToken.None);

			data.Migrated.Should().BeTrue();
			data.Totals.Single().KnownValue.Should().Be(550);
			data.Locations.Select(l => (l.Name, l.KnownValue, l.Lines)).Should().Equal(("Engine 1", 400m, 2), ("Station 1", 150m, 2));
		}

		[Test]
		public async Task Valuation_is_empty_before_initialization_and_refuses_without_the_inventory_permission()
		{
			var migration = new Mock<IInventoryMigrationService>(MockBehavior.Strict);
			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(false);
			var purchasing = new Mock<IInventoryPurchasingService>(MockBehavior.Strict);
			var provider = new InventoryValuationWidgetProvider(purchasing.Object, migration.Object, Mock.Of<IInventoryCatalogService>(MockBehavior.Strict));

			((InventoryValuationWidgetData)await provider.GetDataAsync(Context("inventory.valuation"), CancellationToken.None)).Migrated.Should().BeFalse();
			purchasing.VerifyNoOtherCalls();

			migration.Setup(m => m.IsMigratedAsync(DepartmentId)).ReturnsAsync(true);
			purchasing.Setup(p => p.GetValuationAsync(It.IsAny<InventoryActor>(), null)).ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			(await StateAsync(() => provider.GetDataAsync(Context("inventory.valuation"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task Alert_summary_applies_the_alerts_tab_row_rule_once_per_item_and_holder()
		{
			const string gauze = "11111111-1111-1111-1111-111111111111";
			const string missingItem = "22222222-2222-2222-2222-222222222222";
			const string missingLocation = "33333333-3333-3333-3333-333333333333";
			const string otherItem = "44444444-4444-4444-4444-444444444444";

			var store = new Mock<IInventoryStore>();
			store.Setup(s => s.OpenAlertGroupCountsAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync(new List<InventoryAlertGroupCount>
			{
				new InventoryAlertGroupCount { AlertType = (int)InventoryAlertType.LowStock, ItemId = gauze, AlertCount = 2, Quantity = 1, LatestOpenedOn = Now.AddDays(-1) },
				new InventoryAlertGroupCount { AlertType = (int)InventoryAlertType.Expired, ItemId = gauze, AlertCount = 3, EarliestDueOn = Now.AddDays(-2), LatestOpenedOn = Now },
				new InventoryAlertGroupCount { AlertType = (int)InventoryAlertType.OverdueReturn, ItemId = otherItem, LocationId = missingLocation, AlertCount = 1, LatestOpenedOn = Now },
				new InventoryAlertGroupCount { AlertType = (int)InventoryAlertType.ExpiringSoon, ItemId = missingItem, AlertCount = 4, LatestOpenedOn = Now }
			});
			store.Setup(s => s.RelatedAsync<InventoryStock>(DepartmentId, "ItemId", It.IsAny<string>())).ReturnsAsync(new List<InventoryStock>());
			store.Setup(s => s.RelatedAsync<InventoryAsset>(DepartmentId, "ItemId", It.IsAny<string>())).ReturnsAsync(new List<InventoryAsset>());
			store.Setup(s => s.GetAsync<InventoryItem>(DepartmentId, gauze)).ReturnsAsync(new InventoryItem { Id = gauze, DepartmentId = DepartmentId });
			store.Setup(s => s.GetAsync<InventoryItem>(DepartmentId, missingItem)).ReturnsAsync((InventoryItem)null);
			store.Setup(s => s.GetAsync<InventoryLocation>(DepartmentId, missingLocation)).ReturnsAsync((InventoryLocation)null);
			store.Setup(s => s.RelatedManyAsync<InventoryItem>(DepartmentId, "Id", It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(new List<InventoryItem>
			{
				new InventoryItem { Id = gauze, DepartmentId = DepartmentId, Content = "{\"Name\":\"Gauze\"}" }
			});
			var authorization = new Mock<IInventoryAuthorizationService>(MockBehavior.Strict);
			authorization.Setup(a => a.RequireAsync(It.IsAny<InventoryActor>(), false, null, null)).Returns(Task.CompletedTask);
			authorization.Setup(a => a.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.AdjustInventory, null)).Returns(Task.CompletedTask);

			var service = new InventoryModernizationService(store.Object, authorization.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IProtectedReadService>(), Mock.Of<IProtectedWriteService>(),
				Mock.Of<IDomainEventOutboxService>(), Mock.Of<IAuditLogsRepository>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentGroupsService>());
			var summary = await service.GetAlertSummaryAsync(new InventoryActor { DepartmentId = DepartmentId, UserId = UserId }, 5);

			summary.Total.Should().Be(5, "the unknown item and the missing holder are skipped like the Alerts tab skips them");
			summary.CountsByType.Should().BeEquivalentTo(new Dictionary<int, int> { [(int)InventoryAlertType.LowStock] = 2, [(int)InventoryAlertType.Expired] = 3 });
			summary.Top.Select(t => (t.AlertType, t.ItemName)).Should().Equal(((int)InventoryAlertType.Expired, "Gauze"), ((int)InventoryAlertType.LowStock, "Gauze"));
			store.Verify(s => s.GetAsync<InventoryItem>(DepartmentId, gauze), Times.Once(), "one decision per item and holder");

			authorization.Setup(a => a.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.AdjustInventory, null)).ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			var refused = await service.GetAlertSummaryAsync(new InventoryActor { DepartmentId = DepartmentId, UserId = UserId }, 5);
			refused.Total.Should().Be(0, "department-wide alerts need the adjust permission");
			refused.Top.Should().BeEmpty();
		}
	}
}
