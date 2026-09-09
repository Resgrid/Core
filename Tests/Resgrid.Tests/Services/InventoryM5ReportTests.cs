using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		[Test]
		public async Task M5_on_hand_keeps_exact_bulk_and_current_assets_inside_visible_active_holders()
		{
			var bulk = Item(); var serial = Item(InventoryTrackingMode.Serialized); var visible = Location(InventoryLocationType.Unit, 101); var hidden = Location();
			SeedStock(bulk, visible, 2.125001m); SeedStock(bulk, hidden, 9000); _deniedLocations.Add(hidden.Id);
			var available = M5Asset(serial, visible, 0, "visible-serial"); M5Asset(serial, visible, 6, "retired-canary"); M5Asset(serial, hidden, 0, "hidden-canary");
			var bag = M5Asset(serial, visible, 4, "lost-bag");
			var container = _store.Seed(new InventoryLocation { DepartmentId = Department, LocationType = (int)InventoryLocationType.Container, ContainerAssetId = bag.Id, Content = "{}" });
			SeedStock(bulk, container, 500); M5Asset(serial, container, 0, "in-lost-bag");
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.OnHand, UnitId = 101 });
			result.Rows.Should().HaveCount(2); result.Rows.Sum(row => (decimal)row["Amount"]).Should().Be(3.125001m);
			result.Rows.Single(row => row["AssetId"] != null)["AssetId"].Should().Be(available.Id);
			JsonConvert.SerializeObject(result).Should().Contain("visible-serial").And.NotContain("hidden-canary").And.NotContain("retired-canary").And.NotContain("in-lost-bag");
			_transaction.Should().BeNull(); _events.Should().BeEmpty(); _audits.Should().BeEmpty();
			_uow.Verify(x => x.CommitChanges(), Times.Once);
		}

		[Test]
		public async Task M5_usage_joins_authorized_Record_metadata_without_duplicating_ledger_or_inventing_legacy_Log_links()
		{
			var item = Item(); var location = Location(); var recordId = Guid.NewGuid().ToString("D");
			var consumed = M5Ledger(item, location, null, 3.125001m, InventoryTransactionType.Consume);
			consumed.ReferenceType = (int)InventoryReferenceType.RmsRecord; consumed.ReferenceId = recordId; _store.Seed(consumed);
			var usage = _store.Seed(new RecordInventoryUsage { DepartmentId = Department, SourceType = 1, SourceId = recordId, RecordKind = 1, ItemId = item.Id, SourceLocationId = location.Id,
				TransactionId = consumed.Id, Quantity = consumed.Quantity, UsageType = (int)InventoryUsageType.LeftAtScene, CallId = 123, RmsRevisionId = Guid.NewGuid().ToString("D"), Content = JsonConvert.SerializeObject(new InventoryLabel { Note = "protected-source-note" }) });
			var reversal = M5Ledger(item, null, location, 3.125001m, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = consumed.Id; reversal.ReferenceType = 2; reversal.ReferenceId = recordId; _store.Seed(reversal);
			_store.Seed(new RecordInventoryUsage { DepartmentId = Department, SourceType = 1, SourceId = recordId, RecordKind = 1, ItemId = item.Id, SourceLocationId = location.Id,
				TransactionId = reversal.Id, Quantity = reversal.Quantity, ReversesUsageId = usage.Id, UsageType = usage.UsageType, Content = JsonConvert.SerializeObject(new InventoryLabel { Note = "protected-correction-note" }) });
			var legacy = M5Ledger(item, location, null, 2, InventoryTransactionType.Migrated); legacy.ReferenceType = 8; legacy.LegacyInventoryId = 1234; _store.Seed(legacy);
			M5Ledger(item, null, location, 50, InventoryTransactionType.Receive);
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Usage });
			result.Rows.Should().HaveCount(3); result.Rows.Sum(row => (decimal)row["Amount"]).Should().Be(2);
			result.Rows.Single(row => (string)row["RecordUsageLedgerEntry"] == consumed.Id)["M5SourceId"].Should().Be(recordId);
			result.Rows.Single(row => (string)row["RecordUsageLedgerEntry"] == reversal.Id)["Amount"].Should().Be(-3.125001m);
			var migrated = result.Rows.Single(row => (string)row["RecordUsageLedgerEntry"] == legacy.Id);
			migrated["M5UsageSource"].Should().Be(InventoryReferenceType.Legacy); migrated["M5SourceId"].Should().BeNull(); migrated["M5CallId"].Should().BeNull();
			_recordsAuth.Setup(x => x.CanUserViewRecordAsync(_actor.UserId, recordId, Department)).ReturnsAsync(false);
			var limited = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Usage });
			limited.Rows.Sum(row => (decimal)row["Amount"]).Should().Be(2, "stock movements remain visible without access to the source document");
			JsonConvert.SerializeObject(limited).Should().NotContain(recordId).And.NotContain("protected-source-note").And.NotContain("protected-correction-note");
		}

		[Test]
		public async Task M5_expiration_uses_current_positive_stock_asset_or_lot_expiry_and_excludes_hidden_and_terminal_holdings()
		{
			var bulk = Item(); var serial = Item(InventoryTrackingMode.Serialized); var visible = Location(); var hidden = Location(); _deniedLocations.Add(hidden.Id);
			var expired = _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = bulk.Id, ExpiresOn = _clock.Utc.AddDays(-10), Content = JsonConvert.SerializeObject(new InventoryLotContent { LotNumber = "expired-lot" }) });
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = bulk.Id, LocationId = visible.Id, LotId = expired.Id, Quantity = 2 });
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = bulk.Id, LocationId = hidden.Id, LotId = expired.Id, Quantity = 1000 });
			var soon = M5Asset(serial, visible, 2, "repair-expiring"); soon.ExpiresOn = _clock.Utc.AddDays(5); _store.Seed(soon);
			var later = M5Asset(serial, visible, 0, "later-canary"); later.ExpiresOn = _clock.Utc.AddDays(90); _store.Seed(later);
			var terminal = M5Asset(serial, visible, 5, "consumed-canary"); terminal.ExpiresOn = _clock.Utc.AddDays(-1); _store.Seed(terminal);
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Expiration });
			result.Rows.Should().HaveCount(2); result.Rows.Sum(row => (decimal)row["Amount"]).Should().Be(3);
			result.Rows.Select(row => (int)row["M5DaysUntilExpiry"]).Should().BeEquivalentTo(new[] { -10, 5 });
			JsonConvert.SerializeObject(result).Should().NotContain("later-canary").And.NotContain("consumed-canary");
			var upcoming = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Expiration, FromUtc = _clock.Utc, UntilUtc = _clock.Utc.AddDays(10) });
			upcoming.Rows.Should().ContainSingle().Which["AssetId"].Should().Be(soon.Id);
		}

		[Test]
		public async Task M5_low_stock_includes_serialized_counts_and_zero_threshold_overrides_minimum_without_hidden_stock()
		{
			var bulk = Item(); var serial = Item(InventoryTrackingMode.Serialized); var above = Item(); var visible = Location(); var hidden = Location(); _deniedLocations.Add(hidden.Id);
			M5Threshold(bulk, 2, 10); M5Threshold(serial, null, 3); M5Threshold(above, 0, 10);
			SeedStock(bulk, visible, 2); SeedStock(bulk, hidden, 9000); SeedStock(above, visible, 1);
			M5Asset(serial, visible, 0, "in-service"); M5Asset(serial, visible, 3, "damaged"); M5Asset(serial, visible, 4, "lost"); M5Asset(serial, hidden, 0, "hidden");
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.LowStock });
			result.Rows.Should().HaveCount(2); result.Rows.Select(row => (decimal)row["OnHand"]).Should().Equal(2, 2);
			result.Rows.Select(row => (decimal)row["M5Shortfall"]).Should().BeEquivalentTo(new[] { 0m, 1m });
			result.Rows.Should().NotContain(row => (string)row["Name"] == JsonConvert.DeserializeObject<InventoryItemContent>(above.Content).Name);
		}

		[Test]
		public async Task M5_transfer_history_contains_original_and_reversal_in_range_and_requires_both_locations()
		{
			var item = Item(); var from = Location(); var to = Location(); var hidden = Location(); _deniedLocations.Add(hidden.Id);
			var moved = M5Ledger(item, from, to, 2.125001m, InventoryTransactionType.Transfer);
			var reverse = M5Ledger(item, to, from, 2.125001m, InventoryTransactionType.Adjust); reverse.ReversesTransactionId = moved.Id; _store.Seed(reverse);
			M5Ledger(item, from, hidden, 999, InventoryTransactionType.Transfer); M5Ledger(item, from, null, 50, InventoryTransactionType.Consume);
			var outside = M5Ledger(item, from, to, 100, InventoryTransactionType.Transfer); outside.OccurredOn = _clock.Utc.AddDays(1); _store.Seed(outside);
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.TransferHistory, FromUtc = _clock.Utc, UntilUtc = _clock.Utc.AddDays(1), LocationId = from.Id });
			result.Rows.Should().HaveCount(2); result.Rows.Select(row => (string)row["RecordUsageLedgerEntry"]).Should().BeEquivalentTo(new[] { moved.Id, reverse.Id });
			result.Rows.Single(row => (string)row["RecordUsageLedgerEntry"] == reverse.Id)["M5ReversesTransaction"].Should().Be(moved.Id);
		}

		[Test]
		public async Task M5_issuance_filters_recipient_and_date_while_preserving_partial_return_and_current_recipient_visibility()
		{
			var item = Item(); var location = Location();
			var current = M5Issuance(item, location, "visible-person", null, _clock.Utc, 5, 2); M5Issuance(item, location, "hidden-person", null, _clock.Utc, 999, 0);
			M5Issuance(item, location, "visible-person", null, _clock.Utc.AddDays(-60), 7, 0); var unit = M5Issuance(item, location, null, 101, _clock.Utc, 3, 0);
			_auth.Setup(x => x.CanLocationAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryLocation>())).ReturnsAsync((InventoryActor actor, InventoryLocation holder) => holder.DepartmentId == Department && holder.UserId != "hidden-person");
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Issuance, UserId = "visible-person" });
			result.Rows.Should().ContainSingle().Which["M5IssuanceId"].Should().Be(current.Id);
			result.Rows[0]["M5ReturnedQuantity"].Should().Be(2m); result.Rows[0]["M5OutstandingQuantity"].Should().Be(3m);
			var all = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Issuance }); all.Rows.Should().HaveCount(2);
			var unitResult = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Issuance, UnitId = 101 }); unitResult.Rows.Should().ContainSingle().Which["M5IssuanceId"].Should().Be(unit.Id);
			var older = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Issuance, UserId = "visible-person", FromUtc = _clock.Utc.AddDays(-61), UntilUtc = _clock.Utc.AddDays(1) }); older.Rows.Should().HaveCount(2);
		}

		[Test]
		public async Task M5_valuation_keeps_currency_partitions_negative_balances_unknown_costs_and_assets_once()
		{
			var usd = M4Item(defaultCost: 2); var negative = M4Item(defaultCost: 3); var eur = M4Item(defaultCost: 5, currency: "EUR"); var unknown = M4Item(defaultCost: null); var serial = M4Item(InventoryTrackingMode.Serialized);
			var visible = Location(); var hidden = Location(); _deniedLocations.Add(hidden.Id);
			SeedStock(usd, visible, 2); SeedStock(negative, visible, -2); SeedStock(eur, visible, 3); SeedStock(unknown, visible, 2); SeedStock(usd, hidden, 9999);
			var asset = M5Asset(serial, visible, 0, "costed"); asset.Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "costed", AcquisitionCost = 12 }); _store.Seed(asset);
			M5Asset(serial, visible, 6, "retired-cost-canary");
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Valuation });
			result.Rows.Should().HaveCount(5); result.Totals.Single(x => x.CurrencyCode == "USD").KnownValue.Should().Be(10); result.Totals.Single(x => x.CurrencyCode == "USD").UncostedRows.Should().Be(1);
			result.Totals.Single(x => x.CurrencyCode == "EUR").KnownValue.Should().Be(15); result.Rows.Count(row => row["AssetId"] != null).Should().Be(1);
			var filtered = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Valuation, ItemId = eur.Id });
			filtered.Rows.Should().ContainSingle(); filtered.Totals.Should().ContainSingle().Which.CurrencyCode.Should().Be("EUR");
			InventoryReportDocuments.Build(result, CultureInfo.GetCultureInfo("en")).Should().Contain(InventoryReportDocuments.Text("M4NegativeStock", CultureInfo.GetCultureInfo("en")));
		}

		[Test]
		public async Task M5_controlled_log_preserves_frozen_witness_timestamp_attestation_and_balances_with_signature_lines()
		{
			var medicine = Item(controlled: true); var ordinary = Item(); var location = Location(); var witnessed = _clock.Utc.AddMinutes(-5);
			var movement = M5Ledger(medicine, location, null, 2, InventoryTransactionType.Consume); movement.FromQuantityBefore = 5; movement.FromQuantityAfter = 3;
			movement.Content = JsonConvert.SerializeObject(new { ItemName = "Frozen medicine", UnitOfMeasure = "ampoule", PerformerId = "performer-id", WitnessUserId = "witness-id", WitnessedOn = witnessed, Attestation = "Witnessed <script>never execute</script>" }); _store.Seed(movement);
			M5Ledger(ordinary, location, null, 999, InventoryTransactionType.Consume);
			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.ControlledSubstanceLog });
			result.Rows.Should().ContainSingle(); var row = result.Rows[0]; row["Name"].Should().Be("Frozen medicine"); row["M5Performer"].Should().Be("performer-id"); row["M5Witness"].Should().Be("witness-id");
			row["M5WitnessedOn"].Should().Be(witnessed); row["M5FromBefore"].Should().Be(5m); row["M5FromAfter"].Should().Be(3m);
			var html = InventoryReportDocuments.Build(result, CultureInfo.GetCultureInfo("ar"));
			html.Should().Contain("dir=\"rtl\"").And.Contain("performer-id").And.Contain("witness-id").And.Contain("class=\"signatures\"").And.Contain("&lt;script&gt;").And.NotContain("<script>");
			_auth.Verify(x => x.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.ManageControlledSubstances, null), Times.AtLeast(2));
		}

		[TestCase(InventoryReportKind.OnHand)]
		[TestCase(InventoryReportKind.Usage)]
		[TestCase(InventoryReportKind.Valuation)]
		public async Task M5_reports_fail_closed_on_protected_data_without_partial_output_or_writes(InventoryReportKind kind)
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 2); M5Ledger(item, location, null, 1, InventoryTransactionType.Consume);
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { IsProtected = true, RedactedFields = new() { "inventoryitems.content" } }));
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = kind }), "ProtectedDataRequired", 403);
			_transaction.Should().BeNull(); Stock(item, location).Should().Be(2); _events.Should().BeEmpty(); _audits.Should().BeEmpty(); _uow.Verify(x => x.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task M5_controlled_permission_is_required_before_protected_content_resolution()
		{
			var item = Item(controlled: true); var location = Location(); M5Ledger(item, location, null, 1, InventoryTransactionType.Consume);
			_auth.Setup(x => x.RequireAsync(It.IsAny<InventoryActor>(), false, PermissionTypes.ManageControlledSubstances, null)).ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.ControlledSubstanceLog }), "PermissionRequired", 403); _read.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task M5_reports_enforce_history_boundaries_filter_identifiers_and_explicit_size_limits()
		{
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Usage, FromUtc = _clock.Utc.AddDays(-367), UntilUtc = _clock.Utc }), "InvalidReportRange", 400);
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Usage, FromUtc = _clock.Utc, UntilUtc = _clock.Utc }), "InvalidReportRange", 400);
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Usage, ItemId = "forged" }), "InvalidIdentifier", 400);
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.OnHand, FromUtc = _clock.Utc.AddDays(-1) }), "InvalidReportFilter", 400);
			var item = Item(); var location = Location();
			for (var index = 0; index < 5001; index++) _store.Seed(new InventoryTransaction { DepartmentId = Department, EntryId = index + 1, ItemId = item.Id, FromLocationId = location.Id, TransactionType = 2, Quantity = 1, OccurredOn = _clock.Utc, Content = "{}" });
			await Fails(() => _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.Usage }), "InventoryTooLarge", 409); _transaction.Should().BeNull();
		}

		[Test]
		public void M5_report_documents_encode_authored_values_support_locales_and_have_no_external_resources()
		{
			var report = new InventoryReport { Kind = InventoryReportKind.OnHand, GeneratedOn = _clock.Utc, Columns = new() { "Name", "Amount" },
				Rows = new() { new() { ["Name"] = "<img src='https://example.invalid/canary' onerror='alert(1)'> & <script>bad</script>", ["Amount"] = 1.125001m } } };
			var html = InventoryReportDocuments.Build(report, CultureInfo.GetCultureInfo("fr"));
			html.Should().Contain("lang=\"fr\"").And.Contain("1,125001").And.Contain("&lt;img").And.Contain("&amp;").And.NotContain("<img").And.NotContain("<script").And.NotContain("<link").And.NotContain("<iframe");
			InventoryReportDocuments.Locked(InventoryReportKind.OnHand, CultureInfo.GetCultureInfo("ar")).Should().Contain("dir=\"rtl\"").And.NotContain("canary");
		}

		private InventoryAsset M5Asset(InventoryItem item, InventoryLocation location, int status, string serial) => _store.Seed(new InventoryAsset {
			DepartmentId = Department, ItemId = item.Id, CurrentLocationId = location.Id, Status = status, Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = serial }) });
		private InventoryTransaction M5Ledger(InventoryItem item, InventoryLocation from, InventoryLocation to, decimal quantity, InventoryTransactionType type) => _store.Seed(new InventoryTransaction {
			DepartmentId = Department, ItemId = item.Id, FromLocationId = from?.Id, ToLocationId = to?.Id, Quantity = quantity, TransactionType = (int)type, OccurredOn = _clock.Utc,
			CreatedBy = _actor.UserId, Content = JsonConvert.SerializeObject(new { ItemName = "Frozen item", UnitOfMeasure = "each", Note = "ledger-note" }) });
		private void M5Threshold(InventoryItem item, decimal? reorder, decimal? minimum)
		{
			var details = JsonConvert.DeserializeObject<InventoryItemContent>(item.Content); details.ReorderPoint = reorder; details.MinLevel = minimum; item.Content = JsonConvert.SerializeObject(details); _store.Seed(item);
		}
		private InventoryIssuance M5Issuance(InventoryItem item, InventoryLocation location, string user, int? unit, DateTime date, decimal quantity, decimal returned) => _store.Seed(new InventoryIssuance {
			DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, IssuedToUserId = user, IssuedToUnitId = unit, Quantity = quantity, ReturnedQuantity = returned,
			Status = returned == 0 ? 0 : 2, IssuedOn = date, ExpectedReturnOn = date.AddDays(3), Content = "{}" });
	}
}
