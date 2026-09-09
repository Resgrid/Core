using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class InventoryDatabaseTests
	{
		[Test, Order(0)]
		public void Empty_schema_and_writer_fences_can_reverse_and_reapply()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>(); runner.MigrateDown(0); runner.MigrateUp(); runner.MigrateUp();
		}

		[Test]
		public async Task Schema_covers_every_persisted_model_column_and_six_decimal_quantities()
		{
			await using var db = Connect(_connection);
			InventoryTables.All.Should().HaveCount(21);
			foreach (var binding in InventoryTables.All)
			{
				var actual = (await db.QueryAsync<string>("SELECT LOWER(column_name) FROM information_schema.columns WHERE LOWER(table_name)=@table", new { table = binding.Value.ToLowerInvariant() })).ToHashSet();
				var expected = binding.Key.GetProperties().Where(p => p.CanWrite && !Attribute.IsDefined(p, typeof(NotMappedAttribute))).Select(p => p.Name.ToLowerInvariant());
				actual.Should().Contain(expected, binding.Value + " must store every mapped property");
				foreach (var column in binding.Key.GetProperties().Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?)))
				{
					var query = " FROM information_schema.columns WHERE LOWER(table_name)=@table AND LOWER(column_name)=@column";
					var parameters = new { table = binding.Value.ToLowerInvariant(), column = column.Name.ToLowerInvariant() };
					(await db.ExecuteScalarAsync<int>("SELECT numeric_precision" + query, parameters)).Should().Be(24);
					(await db.ExecuteScalarAsync<int>("SELECT numeric_scale" + query, parameters)).Should().Be(6);
				}
			}
		}

		[Test]
		public void Usage_catalog_upgrade_adds_only_new_content_and_keeps_existing_inventory_at_version_19()
		{
			var catalog = new ProtectedFieldCatalog();
			var originalTables = InventoryTables.All.Values.Except(new[] { "RecordInventoryUsages", "InventoryVendors", "InventoryPurchaseOrders", "InventoryPurchaseOrderItems", "InventoryCounts", "InventoryCountItems", "InventoryAlerts", "InventoryAlertDeliveries" }).ToArray();
			originalTables.Should().HaveCount(13);
			foreach (var table in originalTables)
				catalog.GetForTable(table).Should().ContainSingle().Which.AddedInCatalogVersion.Should().Be(19);
			catalog.GetForTable("RecordInventoryUsages").Should().ContainSingle().Which.AddedInCatalogVersion.Should().Be(20);
			catalog.GetForTableAndVersion("RecordInventoryUsages", 19).Should().BeEmpty();
			var binding = AdpTableBindings.ForVersionRange(catalog, 19, 20).Should().ContainSingle().Which;
			binding.TableName.Should().Be("RecordInventoryUsages"); binding.PkColumn.Should().Be("Id"); binding.PkIsNumeric.Should().BeFalse();
			binding.DepartmentColumn.Should().Be("DepartmentId"); binding.ProtectedMarkerColumn.Should().Be("IsProtected");
			binding.Columns.Select(c => c.FieldId).Should().Equal("recordinventoryusages.content");
		}

		[Test]
		public void Purchasing_catalog_upgrade_registers_only_the_three_new_protected_content_fields()
		{
			var catalog = new ProtectedFieldCatalog();
			var tables = new[] { "InventoryVendors", "InventoryPurchaseOrders", "InventoryPurchaseOrderItems" };
			var bindings = AdpTableBindings.ForVersionRange(catalog, 20, 21).ToArray();
			bindings.Select(b => b.TableName).Should().BeEquivalentTo(tables);
			foreach (var binding in bindings)
			{
				catalog.GetForTable(binding.TableName).Should().ContainSingle().Which.AddedInCatalogVersion.Should().Be(21);
				catalog.GetForTableAndVersion(binding.TableName, 20).Should().BeEmpty();
				binding.PkColumn.Should().Be("Id"); binding.PkIsNumeric.Should().BeFalse();
				binding.DepartmentColumn.Should().Be("DepartmentId"); binding.ProtectedMarkerColumn.Should().Be("IsProtected");
				binding.Columns.Select(c => c.FieldId).Should().Equal(binding.TableName.ToLowerInvariant() + ".content");
			}
			catalog.GetForTable("RecordInventoryUsages").Should().ContainSingle().Which.AddedInCatalogVersion.Should().Be(20);
		}

		[Test]
		public async Task Purchasing_vendor_links_preserve_contact_identity_type_tenant_and_active_uniqueness()
		{
			var contactId = await InsertContactAsync(contactId: new string('V', 128));
			var foreignContact = await InsertContactAsync(88);
			var vendor = NewRow<InventoryVendor>(); vendor.ContactId = contactId;
			await WriteAsync(store => store.InsertAsync(vendor));
			await RejectAsync(store => { var duplicate = NewRow<InventoryVendor>(); duplicate.ContactId = contactId; return store.InsertAsync(duplicate); });
			await RejectAsync(store => { var foreign = NewRow<InventoryVendor>(); foreign.ContactId = foreignContact; return store.InsertAsync(foreign); });
			await using var db = Connect(_connection);
			if (_type == DatabaseTypes.Postgres)
				(await db.QueryAsync<string>("SELECT udt_name FROM information_schema.columns WHERE table_name IN ('contacts','inventoryvendors') AND column_name='contactid'"))
					.Should().HaveCount(2).And.OnlyContain(t => t == "citext");
			else
				(await db.QueryAsync<int>("SELECT character_maximum_length FROM information_schema.columns WHERE table_name IN ('Contacts','InventoryVendors') AND column_name='ContactId'"))
					.Should().HaveCount(2).And.OnlyContain(length => length == 128);
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q("Contacts")} WHERE {Q("ContactId")}=@contactId", new { contactId })).Should().ThrowAsync<DbException>();
			vendor.IsDeleted = true; vendor.Revision++;
			var replacement = NewRow<InventoryVendor>(); replacement.ContactId = contactId;
			await WriteAsync(async store => { await store.UpdateAsync(vendor, 1); await store.InsertAsync(replacement); });
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.GetAsync<InventoryVendor>(77, replacement.Id)).ContactId.Should().Be(contactId);
			(await repository.GetAsync<InventoryVendor>(88, replacement.Id)).Should().BeNull();
			(await repository.RelatedAsync<InventoryVendor>(77, "ContactId", contactId)).Select(v => v.Id).Should().BeEquivalentTo(new[] { vendor.Id, replacement.Id });
			(await repository.QueryAsync<InventoryVendor>(77, new InventoryQuery())).Should().ContainSingle().Which.Id.Should().Be(replacement.Id);
		}

		[Test]
		public async Task Purchasing_order_and_line_foreign_keys_are_tenant_scoped_and_statuses_are_bounded()
		{
			var seed = await SeedPurchasingAsync(); var foreign = await SeedPurchasingAsync(88);
			foreach (var corrupt in new Action<InventoryPurchaseOrder>[]
			{
				row => row.DepartmentId = 88, row => row.VendorId = foreign.Vendor.Id, row => row.Status = -1, row => row.Status = 5
			})
				await RejectAsync(store =>
				{
					var row = NewRow<InventoryPurchaseOrder>(); row.VendorId = seed.Vendor.Id; row.CurrencyCode = "USD";
					corrupt(row); return store.InsertAsync(row);
				});
			foreach (var corrupt in new Action<InventoryPurchaseOrderItem>[]
			{
				row => row.DepartmentId = 88, row => row.PurchaseOrderId = foreign.Order.Id, row => row.ItemId = foreign.Item.Id
			})
				await RejectAsync(store => { var row = PurchaseLine(seed.Order, seed.Item, 2); corrupt(row); return store.InsertAsync(row); });
			await WriteAsync(async store =>
			{
				foreach (InventoryPurchaseOrderStatus status in Enum.GetValues(typeof(InventoryPurchaseOrderStatus)))
				{
					var row = NewRow<InventoryPurchaseOrder>(); row.VendorId = seed.Vendor.Id; row.CurrencyCode = "USD"; row.Status = (int)status;
					await store.InsertAsync(row);
				}
			});
			using var uow = new UnitOfWork(Connections());
			(await Store(uow).RelatedAsync<InventoryPurchaseOrder>(77, "VendorId", seed.Vendor.Id)).Select(o => o.Status).Distinct().Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
		}

		[Test]
		public async Task Purchasing_lines_enforce_positive_ordered_nonnegative_received_and_active_line_numbers()
		{
			var seed = await SeedPurchasingAsync();
			foreach (var corrupt in new Action<InventoryPurchaseOrderItem>[]
			{
				row => row.LineNumber = 0, row => row.QuantityOrdered = 0, row => row.QuantityOrdered = -1,
				row => row.QuantityReceived = -0.000001m, row => row.QuantityReceived = row.QuantityOrdered + 0.000001m
			})
				await RejectAsync(store => { var row = PurchaseLine(seed.Order, seed.Item, 2); corrupt(row); return store.InsertAsync(row); });
			await RejectAsync(store => store.InsertAsync(PurchaseLine(seed.Order, seed.Item)));
			seed.Line.IsDeleted = true; seed.Line.Revision++;
			var replacement = PurchaseLine(seed.Order, seed.Item); replacement.QuantityReceived = replacement.QuantityOrdered;
			var next = PurchaseLine(seed.Order, seed.Item, 2); next.QuantityReceived = 0.123456m;
			await WriteAsync(async store => { await store.UpdateAsync(seed.Line, 1); await store.InsertAsync(replacement); await store.InsertAsync(next); });
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.RelatedAsync<InventoryPurchaseOrderItem>(77, "PurchaseOrderId", seed.Order.Id)).Should().HaveCount(3);
			(await repository.QueryAsync<InventoryPurchaseOrderItem>(77, new InventoryQuery())).Select(l => l.Id).Should().BeEquivalentTo(new[] { replacement.Id, next.Id });
			(await repository.GetAsync<InventoryPurchaseOrderItem>(77, replacement.Id)).QuantityReceived.Should().Be(3.123456m);
			(await repository.GetAsync<InventoryPurchaseOrderItem>(77, next.Id)).QuantityReceived.Should().Be(0.123456m);
		}

		[Test]
		public async Task Purchasing_receipt_backlinks_require_same_tenant_lines_and_purchase_order_reference_type()
		{
			var seed = await SeedPurchasingAsync(); var foreign = await SeedPurchasingAsync(88);
			await RejectAsync(store => store.InsertAsync(Receipt(seed.Order, foreign.Line, seed.Item, seed.Location)));
			await RejectAsync(store => { var invalid = Receipt(seed.Order, seed.Line, seed.Item, seed.Location); invalid.ReferenceType = (int)InventoryReferenceType.None; return store.InsertAsync(invalid); });
			var receipt = Receipt(seed.Order, seed.Line, seed.Item, seed.Location);
			await WriteAsync(store => store.InsertAsync(receipt));
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			var saved = await repository.GetAsync<InventoryTransaction>(77, receipt.Id);
			saved.PurchaseOrderItemId.Should().Be(seed.Line.Id); saved.ReferenceId.Should().Be(seed.Order.Id); saved.Quantity.Should().Be(0.123456m);
			(await repository.RelatedAsync<InventoryTransaction>(77, "PurchaseOrderItemId", seed.Line.Id)).Should().ContainSingle().Which.Id.Should().Be(receipt.Id);
			(await repository.RelatedAsync<InventoryTransaction>(88, "PurchaseOrderItemId", seed.Line.Id)).Should().BeEmpty();
			await using var db = Connect(_connection);
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q("InventoryPurchaseOrderItems")} WHERE {Q("Id")}=@Id", new { seed.Line.Id })).Should().ThrowAsync<DbException>();
		}

		[Test]
		public async Task Purchasing_receipt_counter_stock_and_ledger_rollback_as_one_transaction()
		{
			var seed = await SeedPurchasingAsync();
			await WriteAsync(async store => { await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, 5, "inventory-test-author"); });
			var receipt = Receipt(seed.Order, seed.Line, seed.Item, seed.Location);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			await uow.CreateOrGetConnectionAsync(); await repository.LockDepartmentAsync(77);
			seed.Line.QuantityReceived = receipt.Quantity; seed.Line.Revision++;
			seed.Order.Status = (int)InventoryPurchaseOrderStatus.PartiallyReceived; seed.Order.Revision++;
			await repository.UpdateAsync(seed.Line, 1); await repository.UpdateAsync(seed.Order, 1); await repository.InsertAsync(receipt);
			await repository.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, receipt.Quantity, "inventory-test-author");
			uow.DiscardChanges();
			(await repository.GetAsync<InventoryTransaction>(77, receipt.Id)).Should().BeNull();
			var line = await repository.GetAsync<InventoryPurchaseOrderItem>(77, seed.Line.Id); line.QuantityReceived.Should().Be(0); line.Revision.Should().Be(1);
			var order = await repository.GetAsync<InventoryPurchaseOrder>(77, seed.Order.Id); order.Status.Should().Be((int)InventoryPurchaseOrderStatus.Draft); order.Revision.Should().Be(1);
			(await repository.RelatedAsync<InventoryStock>(77, "ItemId", seed.Item.Id)).Single().Quantity.Should().Be(5);
		}

		[Test]
		public async Task Purchasing_repository_updates_preserve_protected_content_revisions_and_tenant_boundaries()
		{
			var seed = await SeedPurchasingAsync();
			foreach (var row in new InventoryMutableRow[] { seed.Vendor, seed.Order, seed.Line })
			{ row.Revision++; row.IsProtected = true; row.Content = new string('p', 18000); }
			seed.Order.Status = (int)InventoryPurchaseOrderStatus.Received; seed.Order.OrderedOn = new DateTime(2026, 9, 1); seed.Order.ReceivedOn = new DateTime(2026, 9, 2);
			seed.Line.QuantityReceived = seed.Line.QuantityOrdered;
			await WriteAsync(async store => { await store.UpdateAsync(seed.Vendor, 1); await store.UpdateAsync(seed.Order, 1); await store.UpdateAsync(seed.Line, 1); });
			await FluentActions.Awaiting(() => WriteAsync(store => store.UpdateAsync(seed.Vendor, 1))).Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed or is unavailable*");
			await FluentActions.Awaiting(() => WriteAsync(store => store.UpdateAsync(seed.Order, 1))).Should().ThrowAsync<InvalidOperationException>();
			await FluentActions.Awaiting(() => WriteAsync(store => store.UpdateAsync(seed.Line, 1))).Should().ThrowAsync<InvalidOperationException>();
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			var vendor = await repository.GetAsync<InventoryVendor>(77, seed.Vendor.Id);
			var order = await repository.GetAsync<InventoryPurchaseOrder>(77, seed.Order.Id);
			var line = await repository.GetAsync<InventoryPurchaseOrderItem>(77, seed.Line.Id);
			foreach (var row in new InventoryMutableRow[] { vendor, order, line })
			{ row.Revision.Should().Be(2); row.IsProtected.Should().BeTrue(); row.Content.Should().Be(new string('p', 18000)); }
			order.CurrencyCode.Should().Be("USD"); order.OrderedOn.Should().Be(seed.Order.OrderedOn); order.ReceivedOn.Should().Be(seed.Order.ReceivedOn);
			line.QuantityReceived.Should().Be(3.123456m);
			(await repository.GetAsync<InventoryVendor>(88, vendor.Id)).Should().BeNull();
			(await repository.GetAsync<InventoryPurchaseOrder>(88, order.Id)).Should().BeNull();
			(await repository.GetAsync<InventoryPurchaseOrderItem>(88, line.Id)).Should().BeNull();
			order.IsDeleted = true; order.Revision++;
			await WriteAsync(store => store.UpdateAsync(order, 2));
			(await repository.QueryAsync<InventoryPurchaseOrder>(77, new InventoryQuery())).Should().BeEmpty();
			(await repository.RelatedAsync<InventoryPurchaseOrder>(77, "VendorId", vendor.Id)).Should().ContainSingle().Which.IsDeleted.Should().BeTrue();
		}

		[Test]
		public async Task Purchasing_migration_refuses_rollback_without_erasing_orders_or_receipt_provenance()
		{
			var seed = await SeedPurchasingAsync(); var receipt = Receipt(seed.Order, seed.Line, seed.Item, seed.Location);
			receipt.IsProtected = true; receipt.Content = "SYNTHETIC-OPAQUE-RECEIPT";
			await WriteAsync(store => store.InsertAsync(receipt));
			await using var db = Connect(_connection); var before = await CleanupSnapshotAsync(db);
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			try { FluentActions.Invoking(() => runner.MigrateDown(200)).Should().Throw<Exception>(); }
			finally { _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp(); }
			(await CleanupSnapshotAsync(db)).Should().BeEquivalentTo(before);
		}

		[Test]
		public async Task Usage_foreign_keys_isolate_tenant_item_asset_lot_transaction_and_correction_links()
		{
			var seed = await SeedAsync(); var other = await SeedAsync(); var foreign = await SeedAsync(88);
			InventoryLot Lot(InventoryItem item) { var row = NewRow<InventoryLot>(item.DepartmentId); row.ItemId = item.Id; row.ReceivedOn = DateTime.UtcNow; return row; }
			InventoryAsset Asset(InventoryItem item, InventoryLocation location) { var row = NewRow<InventoryAsset>(item.DepartmentId); row.ItemId = item.Id; row.CurrentLocationId = location.Id; return row; }
			var lot = Lot(seed.Item); var otherLot = Lot(other.Item); var foreignLot = Lot(foreign.Item);
			var asset = Asset(seed.Item, seed.Location); var otherAsset = Asset(other.Item, other.Location); var foreignAsset = Asset(foreign.Item, foreign.Location);
			var transaction = Posting(seed.Item, seed.Location, null, 0.123456m, InventoryTransactionType.Consume);
			var foreignTransaction = Posting(foreign.Item, foreign.Location, null, 1, InventoryTransactionType.Consume);
			var foreignUsage = Usage(foreignTransaction);
			await WriteAsync(async store => { await store.InsertAsync(lot); await store.InsertAsync(otherLot); await store.InsertAsync(asset); await store.InsertAsync(otherAsset); await store.InsertAsync(transaction); });
			await WriteAsync(async store => { await store.InsertAsync(foreignLot); await store.InsertAsync(foreignAsset); await store.InsertAsync(foreignTransaction); await store.InsertAsync(foreignUsage); }, 88);
			foreach (var corrupt in new Action<RecordInventoryUsage>[]
			{
				r => r.DepartmentId = 88, r => r.ItemId = foreign.Item.Id, r => r.SourceLocationId = foreign.Location.Id,
				r => r.TransactionId = foreignTransaction.Id, r => r.AssetId = foreignAsset.Id, r => r.LotId = foreignLot.Id,
				r => r.AssetId = otherAsset.Id, r => r.LotId = otherLot.Id, r => r.ReversesUsageId = foreignUsage.Id
			})
				await RejectAsync(store => { var invalid = Usage(transaction); corrupt(invalid); return store.InsertAsync(invalid); });
			var usage = Usage(transaction); usage.AssetId = asset.Id; usage.LotId = lot.Id;
			usage.CallId = 987654; usage.RmsRevisionId = Guid.NewGuid().ToString("D"); usage.Content = new string('u', 18000);
			await WriteAsync(store => store.InsertAsync(usage));
			using var uow = new UnitOfWork(Connections()); var saved = await Store(uow).GetAsync<RecordInventoryUsage>(77, usage.Id);
			saved.Quantity.Should().Be(0.123456m); saved.Content.Should().HaveLength(18000); saved.RmsRevisionId.Should().Be(usage.RmsRevisionId);
			(await Store(uow).GetAsync<RecordInventoryUsage>(88, usage.Id)).Should().BeNull();
		}

		[Test]
		public async Task Usage_checks_reject_null_rms_kind_invalid_source_quantity_and_self_reversal()
		{
			var seed = await SeedAsync(); var transaction = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume);
			await WriteAsync(store => store.InsertAsync(transaction));
			foreach (var corrupt in new Action<RecordInventoryUsage>[]
			{
				r => r.SourceType = -1, r => r.SourceType = 2, r => r.RecordKind = null, r => r.RecordKind = 3,
				r => r.SourceType = (int)InventoryUsageSourceType.LegacyLog, r => r.Quantity = 0, r => r.Quantity = -1,
				r => r.UsageType = -1, r => r.UsageType = 4, r => r.Revision = 0, r => r.ReversesUsageId = r.Id
			})
				await RejectAsync(store => { var invalid = Usage(transaction); corrupt(invalid); return store.InsertAsync(invalid); });
			var legacy = Usage(transaction); legacy.SourceType = (int)InventoryUsageSourceType.LegacyLog; legacy.RecordKind = null; legacy.SourceId = "314";
			await WriteAsync(store => store.InsertAsync(legacy));
			using var uow = new UnitOfWork(Connections());
			(await Store(uow).GetAsync<RecordInventoryUsage>(77, legacy.Id)).SourceId.Should().Be("314", "legacy source IDs remain soft references rather than GUID-only Record identities");
		}

		[Test]
		public async Task Usage_transaction_and_reversal_uniqueness_prevent_duplicate_claims()
		{
			var seed = await SeedAsync(); var transaction = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume);
			var usage = Usage(transaction);
			var reversal = Posting(seed.Item, null, seed.Location, 1, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = transaction.Id;
			var correction = Usage(reversal, usage.SourceId); correction.ReversesUsageId = usage.Id;
			var secondReversal = Posting(seed.Item, null, seed.Location, 1, InventoryTransactionType.Adjust); secondReversal.ReversesTransactionId = transaction.Id;
			await WriteAsync(async store => { await store.InsertAsync(transaction); await store.InsertAsync(usage); await store.InsertAsync(reversal); await store.InsertAsync(correction); await store.InsertAsync(secondReversal); });
			await RejectAsync(store => store.InsertAsync(Usage(transaction, Guid.NewGuid().ToString("D"))));
			await RejectAsync(store => { var duplicate = Usage(secondReversal, usage.SourceId); duplicate.ReversesUsageId = usage.Id; return store.InsertAsync(duplicate); });
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.ListAsync<RecordInventoryUsage>(77)).Select(r => r.Id).Should().BeEquivalentTo(new[] { usage.Id, correction.Id });
			await using var db = Connect(_connection);
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q("InventoryTransactions")} WHERE {Q("Id")}=@Id", new { reversal.Id })).Should().ThrowAsync<DbException>();
		}

		[Test]
		public async Task Usage_store_updates_are_rejected_without_rewriting_original_evidence()
		{
			var seed = await SeedAsync(); var transaction = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume); var usage = Usage(transaction);
			usage.Content = "ORIGINAL-USAGE";
			await WriteAsync(async store => { await store.InsertAsync(transaction); await store.InsertAsync(usage); });
			usage.Quantity = 2; usage.Content = "REWRITTEN-USAGE"; usage.Revision = 2;
			await FluentActions.Awaiting(() => WriteAsync(store => store.UpdateAsync(usage, 1))).Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
			using var uow = new UnitOfWork(Connections()); var saved = await Store(uow).GetAsync<RecordInventoryUsage>(77, usage.Id);
			saved.Quantity.Should().Be(1); saved.Content.Should().Be("ORIGINAL-USAGE"); saved.Revision.Should().Be(1);
		}

		[Test]
		public async Task Usage_source_filters_apply_before_paging_and_preserve_kind_and_tenant_boundaries()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88); var sourceId = Guid.NewGuid().ToString("D");
			var matches = new System.Collections.Generic.List<RecordInventoryUsage>();
			await WriteAsync(async store =>
			{
				for (var i = 0; i < 1003; i++)
				{
					var transaction = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume); await store.InsertAsync(transaction);
					var row = Usage(transaction, i < 501 ? "unrelated" : sourceId); row.Id = $"00000000-0000-0000-0000-{i + 1:000000000000}";
					if (i >= 501) matches.Add(row); await store.InsertAsync(row);
				}
				foreach (var source in new[] { (Type: InventoryUsageSourceType.RmsRecord, Kind: (int?)RmsRecordKind.IncidentReport), (Type: InventoryUsageSourceType.LegacyLog, Kind: (int?)null) })
				{
					var transaction = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume); await store.InsertAsync(transaction);
					var row = Usage(transaction, sourceId); row.SourceType = (int)source.Type; row.RecordKind = source.Kind;
					row.Id = $"ffffffff-ffff-ffff-ffff-{(int)source.Type + 1:000000000000}"; await store.InsertAsync(row);
				}
			});
			await WriteAsync(async store => { var transaction = Posting(foreign.Item, foreign.Location, null, 1, InventoryTransactionType.Consume); await store.InsertAsync(transaction); await store.InsertAsync(Usage(transaction, sourceId)); }, 88);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			var filter = new InventoryQuery { SourceType = (int)InventoryUsageSourceType.RmsRecord, SourceId = sourceId, RecordKind = (int)RmsRecordKind.Operational };
			(await repository.QueryAsync<RecordInventoryUsage>(77, new InventoryQuery())).Should().HaveCount(501).And.OnlyContain(r => r.SourceId == "unrelated");
			(await repository.QueryAsync<RecordInventoryUsage>(77, filter)).Select(r => r.Id).Should().Equal(matches.Take(501).Select(r => r.Id));
			(await repository.QueryAsync<RecordInventoryUsage>(77, filter, 500)).Select(r => r.Id).Should().Equal(matches.Skip(500).Select(r => r.Id));
			(await repository.QueryAsync<RecordInventoryUsage>(88, filter)).Should().ContainSingle().Which.DepartmentId.Should().Be(88);
			(await repository.QueryAsync<RecordInventoryUsage>(77, new InventoryQuery { SourceId = "' OR 1=1;--" })).Should().BeEmpty();
			await FluentActions.Awaiting(() => repository.QueryAsync<InventoryItem>(77, filter)).Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Usage_stock_and_ledger_joined_transaction_roll_back_together()
		{
			var seed = await SeedAsync(); await WriteAsync(async store => { await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, 5, "inventory-test-author"); });
			var transaction = Posting(seed.Item, seed.Location, null, 0.123456m, InventoryTransactionType.Consume); var usage = Usage(transaction);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow); await uow.CreateOrGetConnectionAsync(); await repository.LockDepartmentAsync(77);
			await repository.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, -usage.Quantity, "inventory-test-author");
			await repository.InsertAsync(transaction); await repository.InsertAsync(usage); uow.DiscardChanges();
			(await repository.GetAsync<InventoryTransaction>(77, transaction.Id)).Should().BeNull();
			(await repository.GetAsync<RecordInventoryUsage>(77, usage.Id)).Should().BeNull();
			(await repository.RelatedAsync<InventoryStock>(77, "ItemId", seed.Item.Id)).Single().Quantity.Should().Be(5);
		}

		[Test]
		public async Task Usage_migration_refuses_rollback_while_usage_and_correction_provenance_exist()
		{
			var seed = await SeedAsync(); var transaction = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume);
			var usage = Usage(transaction); var reversal = Posting(seed.Item, null, seed.Location, 1, InventoryTransactionType.Adjust);
			reversal.ReversesTransactionId = transaction.Id; var correction = Usage(reversal, usage.SourceId); correction.ReversesUsageId = usage.Id;
			await WriteAsync(async store => { await store.InsertAsync(transaction); await store.InsertAsync(usage); await store.InsertAsync(reversal); await store.InsertAsync(correction); });
			await using var db = Connect(_connection); var before = await CleanupSnapshotAsync(db);
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			try { FluentActions.Invoking(() => runner.MigrateDown(199)).Should().Throw<Exception>(); }
			finally { _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp(); }
			(await CleanupSnapshotAsync(db)).Should().BeEquivalentTo(before);
		}

		[Test]
		public async Task Ledger_allocates_bigint_identity_keeps_guid_and_round_trips_six_decimals()
		{
			var seed = await SeedAsync(); var row = Posting(seed.Item, null, seed.Location, 0.123456m);
			row.ToQuantityBefore = 10.111111m; row.ToQuantityAfter = 10.234567m;
			row.Content = new string('x', 18000); await WriteAsync(store => store.InsertAsync(row));
			row.EntryId.Should().BeGreaterThan(0); Guid.TryParseExact(row.Id, "D", out _).Should().BeTrue();
			using var uow = new UnitOfWork(Connections()); var store = Store(uow);
			var saved = await store.GetAsync<InventoryTransaction>(77, row.Id);
			saved.EntryId.Should().Be(row.EntryId); saved.Quantity.Should().Be(0.123456m); saved.ToQuantityAfter.Should().Be(10.234567m); saved.Content.Should().HaveLength(18000);
			(await store.GetAsync<InventoryTransaction>(88, row.Id)).Should().BeNull();
			await uow.CreateOrGetConnectionAsync(); row.Revision++;
			await FluentActions.Awaiting(() => store.UpdateAsync(row, 1)).Should().ThrowAsync<InvalidOperationException>(); uow.DiscardChanges();
		}

		[Test]
		public async Task Transaction_queries_filter_before_paging_and_order_by_ledger_identity()
		{
			var seed = await SeedAsync(); var unrelated = await SeedAsync(); var foreign = await SeedAsync(88);
			var asset = NewRow<InventoryAsset>(); asset.ItemId = seed.Item.Id; asset.CurrentLocationId = seed.Location.Id;
			var matches = Enumerable.Range(0, 502).Select(i =>
			{
				var row = Posting(seed.Item, i % 2 == 0 ? null : seed.Location, i % 2 == 0 ? seed.Location : null, 1,
					i % 2 == 0 ? InventoryTransactionType.Receive : InventoryTransactionType.Consume);
				row.AssetId = asset.Id; row.OccurredOn = DateTime.UtcNow.AddMinutes(-i); return row;
			}).ToList();
			await WriteAsync(async store =>
			{
				await store.InsertAsync(asset);
				foreach (var row in matches) await store.InsertAsync(row);
				for (var i = 0; i < 501; i++) await store.InsertAsync(Posting(unrelated.Item, null, unrelated.Location, 1));
			});
			await WriteAsync(store => store.InsertAsync(Posting(foreign.Item, null, foreign.Location, 1)), 88);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.QueryAsync<InventoryTransaction>(77, new InventoryQuery())).Should().HaveCount(501).And.OnlyContain(t => t.ItemId == unrelated.Item.Id);
			var filter = new InventoryQuery { ItemId = seed.Item.Id, AssetId = asset.Id, LocationId = seed.Location.Id };
			var first = await repository.QueryAsync<InventoryTransaction>(77, filter);
			var second = await repository.QueryAsync<InventoryTransaction>(77, filter, 500);
			first.Select(t => t.Id).Should().Equal(matches.AsEnumerable().Reverse().Take(501).Select(t => t.Id));
			second.Select(t => t.Id).Should().Equal(matches.AsEnumerable().Reverse().Skip(500).Select(t => t.Id));
			first.Should().Contain(t => t.FromLocationId == seed.Location.Id).And.Contain(t => t.ToLocationId == seed.Location.Id);
			(await repository.QueryAsync<InventoryTransaction>(88, filter)).Should().BeEmpty();
		}

		[Test]
		public async Task Typed_stock_asset_kit_and_person_filters_preserve_tenant_and_archive_boundaries()
		{
			var seed = await SeedAsync(); var other = await SeedAsync(); var foreign = await SeedAsync(88);
			var stock = NewRow<InventoryStock>(); stock.ItemId = seed.Item.Id; stock.LocationId = seed.Location.Id; stock.Quantity = 3;
			var otherStock = NewRow<InventoryStock>(); otherStock.ItemId = seed.Item.Id; otherStock.LocationId = other.Location.Id; otherStock.Quantity = 4;
			var asset = NewRow<InventoryAsset>(); asset.ItemId = seed.Item.Id; asset.CurrentLocationId = seed.Location.Id;
			var archivedAsset = NewRow<InventoryAsset>(); archivedAsset.ItemId = seed.Item.Id; archivedAsset.CurrentLocationId = seed.Location.Id; archivedAsset.IsDeleted = true;
			var kit = NewRow<InventoryKit>(); var otherKit = NewRow<InventoryKit>();
			var line = NewRow<InventoryKitItem>(); line.KitId = kit.Id; line.ItemId = seed.Item.Id; line.Quantity = 1;
			var archivedLine = NewRow<InventoryKitItem>(); archivedLine.KitId = kit.Id; archivedLine.ItemId = other.Item.Id; archivedLine.Quantity = 1; archivedLine.IsDeleted = true;
			var otherLine = NewRow<InventoryKitItem>(); otherLine.KitId = otherKit.Id; otherLine.ItemId = seed.Item.Id; otherLine.Quantity = 1;
			var issuance = NewRow<InventoryIssuance>(); issuance.ItemId = seed.Item.Id; issuance.LocationId = seed.Location.Id;
			issuance.IssuedToUserId = "inventory-test-author"; issuance.IssuedOn = DateTime.UtcNow; issuance.Quantity = 1;
			var unitIssuance = NewRow<InventoryIssuance>(); unitIssuance.ItemId = seed.Item.Id; unitIssuance.LocationId = seed.Location.Id;
			unitIssuance.IssuedToUnitId = 12; unitIssuance.IssuedOn = DateTime.UtcNow; unitIssuance.Quantity = 1;
			await WriteAsync(async store =>
			{
				await store.InsertAsync(stock); await store.InsertAsync(otherStock); await store.InsertAsync(asset); await store.InsertAsync(archivedAsset);
				await store.InsertAsync(kit); await store.InsertAsync(otherKit); await store.InsertAsync(line); await store.InsertAsync(archivedLine); await store.InsertAsync(otherLine);
				await store.InsertAsync(issuance); await store.InsertAsync(unitIssuance);
			});
			var foreignIssuance = NewRow<InventoryIssuance>(88); foreignIssuance.ItemId = foreign.Item.Id; foreignIssuance.LocationId = foreign.Location.Id;
			foreignIssuance.IssuedToUserId = "inventory-test-author"; foreignIssuance.IssuedOn = DateTime.UtcNow; foreignIssuance.Quantity = 1;
			await WriteAsync(store => store.InsertAsync(foreignIssuance), 88);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.QueryAsync<InventoryStock>(77, new InventoryQuery { ItemId = seed.Item.Id, LocationId = seed.Location.Id })).Select(r => r.Id).Should().Equal(stock.Id);
			(await repository.QueryAsync<InventoryAsset>(77, new InventoryQuery { ItemId = seed.Item.Id })).Select(r => r.Id).Should().Equal(asset.Id);
			(await repository.QueryAsync<InventoryKitItem>(77, new InventoryQuery { KitId = kit.Id })).Select(r => r.Id).Should().Equal(line.Id);
			(await repository.QueryAsync<InventoryKitItem>(77, new InventoryQuery { KitId = otherKit.Id, ItemId = seed.Item.Id })).Select(r => r.Id).Should().Equal(otherLine.Id);
			(await repository.QueryAsync<InventoryIssuance>(77, new InventoryQuery { IssuedToUserId = "inventory-test-author" })).Select(r => r.Id).Should().Equal(issuance.Id);
			(await repository.QueryAsync<InventoryKitItem>(88, new InventoryQuery { KitId = kit.Id })).Should().BeEmpty();
		}

		[Test]
		public async Task Unsupported_query_filters_are_rejected_and_filter_values_cannot_become_sql()
		{
			var seed = await SeedAsync();
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			await FluentActions.Awaiting(() => repository.QueryAsync<InventoryItem>(77, new InventoryQuery { ItemId = seed.Item.Id })).Should().ThrowAsync<ArgumentException>();
			await FluentActions.Awaiting(() => repository.QueryAsync<InventoryStock>(77, new InventoryQuery { AssetId = Guid.NewGuid().ToString("D") })).Should().ThrowAsync<ArgumentException>();
			await FluentActions.Awaiting(() => repository.QueryAsync<InventoryTransaction>(77, new InventoryQuery(), -1)).Should().ThrowAsync<ArgumentOutOfRangeException>();
			(await repository.QueryAsync<InventoryTransaction>(77, new InventoryQuery { ItemId = "' OR 1=1; DELETE FROM InventoryItems;--" })).Should().BeEmpty();
			(await repository.GetAsync<InventoryItem>(77, seed.Item.Id)).Should().NotBeNull();
		}

		[Test]
		public async Task Inventory_foreign_keys_reject_cross_tenant_locations_and_mismatched_item_lots_and_assets()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88); var other = NewRow<InventoryItem>();
			var lot = NewRow<InventoryLot>(); lot.ItemId = seed.Item.Id; lot.ReceivedOn = DateTime.UtcNow;
			var asset = NewRow<InventoryAsset>(); asset.ItemId = seed.Item.Id; asset.CurrentLocationId = seed.Location.Id; asset.LotId = lot.Id;
			await WriteAsync(async store => { await store.InsertAsync(other); await store.InsertAsync(lot); await store.InsertAsync(asset); });
			await RejectAsync(store => { var row = NewRow<InventoryStock>(); row.ItemId = seed.Item.Id; row.LocationId = foreign.Location.Id; row.Quantity = 1; return store.InsertAsync(row); });
			await RejectAsync(store => { var row = NewRow<InventoryStock>(); row.ItemId = other.Id; row.LocationId = seed.Location.Id; row.LotId = lot.Id; row.Quantity = 1; return store.InsertAsync(row); });
			await RejectAsync(store => { var row = Posting(other, null, seed.Location, 1); row.AssetId = asset.Id; return store.InsertAsync(row); });
			await using var db = Connect(_connection);
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q("InventoryItems")} WHERE {Q("Id")}=@Id", new { seed.Item.Id })).Should().ThrowAsync<DbException>();
		}

		[Test]
		public async Task Typed_holder_checks_and_tenant_unit_foreign_key_reject_invalid_locations()
		{
			await RejectAsync(store => { var row = NewRow<InventoryLocation>(); row.LocationType = (int)InventoryLocationType.Station; row.GroupId = 223; return store.InsertAsync(row); });
			var station = NewRow<InventoryLocation>(); station.LocationType = (int)InventoryLocationType.Station; station.GroupId = 123;
			await WriteAsync(store => store.InsertAsync(station));
			await RejectAsync(store => { var row = NewRow<InventoryLocation>(); row.LocationType = (int)InventoryLocationType.Unit; row.UnitId = 12; row.GroupId = 123; return store.InsertAsync(row); });
			await RejectAsync(store => { var row = NewRow<InventoryLocation>(); row.LocationType = (int)InventoryLocationType.Unit; row.UnitId = 22; return store.InsertAsync(row); });
			await RejectAsync(store => { var row = NewRow<InventoryLocation>(); row.LocationType = (int)InventoryLocationType.Personnel; return store.InsertAsync(row); });
			await RejectAsync(store => { var row = NewRow<InventoryLocation>(); row.ParentLocationId = row.Id; return store.InsertAsync(row); });
			var unit = NewRow<InventoryLocation>(); unit.LocationType = (int)InventoryLocationType.Unit; unit.UnitId = 12;
			await WriteAsync(store => store.InsertAsync(unit));
			await RejectAsync(store => { var duplicate = NewRow<InventoryLocation>(); duplicate.LocationType = unit.LocationType; duplicate.UnitId = unit.UnitId; return store.InsertAsync(duplicate); });
		}

		[Test]
		public async Task Container_links_and_issuance_holder_constraints_preserve_tenant_boundaries()
		{
			var seed = await SeedAsync();
			var asset = NewRow<InventoryAsset>(); asset.ItemId = seed.Item.Id; asset.CurrentLocationId = seed.Location.Id;
			await WriteAsync(store => store.InsertAsync(asset));
			var container = NewRow<InventoryLocation>(); container.LocationType = (int)InventoryLocationType.Container; container.ContainerAssetId = asset.Id;
			await WriteAsync(store => store.InsertAsync(container));
			await RejectAsync(store => { var row = NewRow<InventoryLocation>(88); row.LocationType = (int)InventoryLocationType.Container; row.ContainerAssetId = asset.Id; return store.InsertAsync(row); });
			await RejectAsync(store => { var row = NewRow<InventoryIssuance>(); row.ItemId = seed.Item.Id; row.AssetId = asset.Id; row.LocationId = seed.Location.Id; row.Quantity = 1; row.IssuedOn = DateTime.UtcNow; row.IssuedToUnitId = 12; row.IssuedToUserId = "inventory-test-author"; return store.InsertAsync(row); });
			var issuance = NewRow<InventoryIssuance>(); issuance.ItemId = seed.Item.Id; issuance.AssetId = asset.Id; issuance.LocationId = seed.Location.Id; issuance.Quantity = 1; issuance.IssuedOn = DateTime.UtcNow; issuance.IssuedToUnitId = 12;
			await WriteAsync(store => store.InsertAsync(issuance));
			await RejectAsync(store => { var duplicate = NewRow<InventoryIssuance>(); duplicate.ItemId = seed.Item.Id; duplicate.AssetId = asset.Id; duplicate.LocationId = seed.Location.Id; duplicate.Quantity = 1; duplicate.IssuedOn = DateTime.UtcNow; duplicate.IssuedToUnitId = 13; return store.InsertAsync(duplicate); });
		}

		[Test]
		public async Task Concurrent_missing_stock_upserts_keep_one_exact_balance_and_rollback_restores_it()
		{
			var seed = await SeedAsync();
			async Task Add(decimal delta) => await WriteAsync(async store => { await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, delta, "inventory-test-author"); });
			await Task.WhenAll(Add(0.123456m), Add(1.111111m));
			using var uow = new UnitOfWork(Connections()); var store = Store(uow);
			var balance = (await store.RelatedAsync<InventoryStock>(77, "ItemId", seed.Item.Id)).Single(); balance.Quantity.Should().Be(1.234567m);
			await uow.CreateOrGetConnectionAsync(); await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, -1m, "inventory-test-author");
			var discarded = Posting(seed.Item, seed.Location, null, 1, InventoryTransactionType.Consume); await store.InsertAsync(discarded); uow.DiscardChanges();
			(await store.GetAsync<InventoryTransaction>(77, discarded.Id)).Should().BeNull();
			(await store.RelatedAsync<InventoryStock>(77, "ItemId", seed.Item.Id)).Single().Quantity.Should().Be(1.234567m);
		}

		[Test]
		public async Task Stock_rebuild_expands_transfer_legs_preserves_lots_and_excludes_serialized_assets()
		{
			var seed = await SeedAsync(); var destination = NewRow<InventoryLocation>(); var lot = NewRow<InventoryLot>(); lot.ItemId = seed.Item.Id; lot.ReceivedOn = DateTime.UtcNow;
			var serialized = NewRow<InventoryItem>(); serialized.TrackingMode = (int)InventoryTrackingMode.Serialized;
			await WriteAsync(async store =>
			{
				await store.InsertAsync(destination); await store.InsertAsync(lot); await store.InsertAsync(serialized);
				var received = Posting(seed.Item, null, seed.Location, 10.123456m); received.LotId = lot.Id; await store.InsertAsync(received);
				var consumed = Posting(seed.Item, seed.Location, null, 0.123456m, InventoryTransactionType.Consume); consumed.LotId = lot.Id; await store.InsertAsync(consumed);
				var transferred = Posting(seed.Item, seed.Location, destination, 3m, InventoryTransactionType.Transfer); transferred.LotId = lot.Id; await store.InsertAsync(transferred);
				var asset = NewRow<InventoryAsset>(); asset.ItemId = serialized.Id; asset.CurrentLocationId = seed.Location.Id; await store.InsertAsync(asset);
				var assetReceipt = Posting(serialized, null, seed.Location, 1); assetReceipt.AssetId = asset.Id; await store.InsertAsync(assetReceipt);
				await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, lot.Id, 999m, "inventory-test-author");
				await store.RebuildStocksAsync(77);
			});
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			var stocks = await repository.ListAsync<InventoryStock>(77); stocks.Should().HaveCount(2); stocks.Sum(s => s.Quantity).Should().Be(10m);
			stocks.Single(s => s.LocationId == seed.Location.Id).Quantity.Should().Be(7m); stocks.Single(s => s.LocationId == destination.Id).Quantity.Should().Be(3m);
			stocks.Should().OnlyContain(s => s.ItemId == seed.Item.Id && s.LotId == lot.Id);
		}

		[Test]
		public async Task Request_operation_line_and_legacy_id_uniqueness_survive_retries()
		{
			var seed = await SeedAsync(); var op = NewRow<InventoryOperation>(); op.RequestId = Guid.NewGuid().ToString("D");
			var legacyItem = NewRow<InventoryItem>(); legacyItem.LegacyInventoryTypeId = 456;
			var transaction = Posting(seed.Item, null, seed.Location, 1); transaction.OperationId = op.Id; transaction.LineNumber = 0; transaction.LegacyInventoryId = 789;
			await WriteAsync(async store => { await store.InsertAsync(op); await store.InsertAsync(legacyItem); await store.InsertAsync(transaction); });
			await RejectAsync(store => { var duplicate = NewRow<InventoryOperation>(); duplicate.RequestId = op.RequestId; return store.InsertAsync(duplicate); });
			await RejectAsync(store => { var duplicate = Posting(seed.Item, null, seed.Location, 1); duplicate.OperationId = op.Id; return store.InsertAsync(duplicate); });
			await RejectAsync(store => { var duplicate = NewRow<InventoryItem>(); duplicate.LegacyInventoryTypeId = 456; return store.InsertAsync(duplicate); });
			await RejectAsync(store => { var duplicate = Posting(seed.Item, null, seed.Location, 1); duplicate.LegacyInventoryId = 789; return store.InsertAsync(duplicate); });
			await WriteAsync(store => { var other = NewRow<InventoryOperation>(88); other.RequestId = op.RequestId; return store.InsertAsync(other); }, 88);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.LegacyItemAsync(77, 456)).Id.Should().Be(legacyItem.Id); (await repository.LegacyTransactionAsync(77, 789)).Id.Should().Be(transaction.Id);
			(await repository.LegacyTransactionAsync(88, 789)).Should().BeNull();
		}

		[TestCase("Inventories"), TestCase("InventoryTypes")]
		public async Task Cutover_blocks_multirow_mutations_and_both_department_move_directions_but_preserves_reads(string table)
		{
			await using var db = Connect(_connection); var type77 = await InsertLegacyTypeAsync(db, 77); var type88 = await InsertLegacyTypeAsync(db, 88);
			await db.ExecuteAsync($"INSERT INTO {Q("Inventories")} ({Q("DepartmentId")},{Q("TypeId")},{Q("Amount")}) VALUES(77,@type77,1),(88,@type88,2)", new { type77, type88 });
			var changedColumn = Q(table == "Inventories" ? "Amount" : "Type");
			await db.ExecuteAsync($"UPDATE {Q(table)} SET {changedColumn}={changedColumn} WHERE {Q("DepartmentId")}=77");
			await MarkMigratedAsync();
			await FluentActions.Awaiting(() => db.ExecuteAsync($"UPDATE {Q(table)} SET {changedColumn}={changedColumn} WHERE {Q("DepartmentId")} IN (77,88)")).Should().ThrowAsync<DbException>();
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")} IN (77,88)")).Should().ThrowAsync<DbException>();
			await FluentActions.Awaiting(() => db.ExecuteAsync($"UPDATE {Q(table)} SET {Q("DepartmentId")}=88 WHERE {Q("DepartmentId")}=77")).Should().ThrowAsync<DbException>();
			await FluentActions.Awaiting(() => db.ExecuteAsync($"UPDATE {Q(table)} SET {Q("DepartmentId")}=77 WHERE {Q("DepartmentId")}=88")).Should().ThrowAsync<DbException>();
			var insert = table == "Inventories" ? $"INSERT INTO {Q(table)} ({Q("DepartmentId")},{Q("TypeId")},{Q("Amount")}) VALUES(88,@type88,3),(77,@type77,3)"
				: $"INSERT INTO {Q(table)} ({Q("DepartmentId")},{Q("Type")}) VALUES(88,'synthetic'),(77,'synthetic')";
			await FluentActions.Awaiting(() => db.ExecuteAsync(insert, new { type77, type88 })).Should().ThrowAsync<DbException>();
			(await LegacyCountAsync(db, table, 77)).Should().Be(1); (await LegacyCountAsync(db, table, 88)).Should().Be(1);
			await db.ExecuteAsync($"UPDATE {Q(table)} SET {changedColumn}={changedColumn} WHERE {Q("DepartmentId")}=88");
			// Teardown removes the department's modern marker first, without disabling any trigger globally.
			await db.ExecuteAsync($"DELETE FROM {Q("InventoryOperations")} WHERE {Q("DepartmentId")}=77");
			await db.ExecuteAsync($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")}=77");
			(await LegacyCountAsync(db, table, 77)).Should().Be(0); (await LegacyCountAsync(db, table, 88)).Should().Be(1);
		}

		[Test]
		public async Task Legacy_writer_waits_for_cutover_department_lock_then_observes_committed_marker()
		{
			await using var legacy = Connect(_connection); await legacy.OpenAsync();
			using var cutover = new UnitOfWork(Connections()); var store = Store(cutover);
			await cutover.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var marker = NewRow<InventoryOperation>(); marker.RequestId = MigrationRequest; marker.State = 2; await store.InsertAsync(marker);
			var pending = legacy.ExecuteAsync($"INSERT INTO {Q("InventoryTypes")} ({Q("DepartmentId")},{Q("Type")}) VALUES(77,'concurrent synthetic')", commandTimeout: 10);
			try
			{
				await Task.WhenAny(pending, Task.Delay(150)); pending.IsCompleted.Should().BeFalse("the migration owns the shared department lock");
				cutover.CommitChanges(); await FluentActions.Awaiting(async () => { await pending; }).Should().ThrowAsync<DbException>();
			}
			finally { if (cutover.Transaction != null) cutover.DiscardChanges(); }
			(await LegacyCountAsync(legacy, "InventoryTypes", 77)).Should().Be(0);
		}

		[Test]
		public async Task Legacy_commit_before_cutover_is_visible_after_the_shared_department_lock()
		{
			await using var legacy = Connect(_connection); await legacy.OpenAsync(); await using var transaction = await legacy.BeginTransactionAsync();
			await legacy.ExecuteAsync($"INSERT INTO {Q("InventoryTypes")} ({Q("DepartmentId")},{Q("Type")}) VALUES(77,'pre-cutover synthetic')", transaction: transaction);
			using var cutover = new UnitOfWork(Connections()); var store = Store(cutover); await cutover.CreateOrGetConnectionAsync();
			var pending = store.LockDepartmentAsync(77);
			await Task.WhenAny(pending, Task.Delay(150)); pending.IsCompleted.Should().BeFalse("the legacy mutation holds the same department lock until commit");
			await transaction.CommitAsync(); await pending;
			(await cutover.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("InventoryTypes")} WHERE {Q("DepartmentId")}=77", transaction: cutover.Transaction)).Should().Be(1);
			cutover.DiscardChanges();
		}

		[Test]
		public async Task Legacy_repeatable_read_uses_sql_locking_or_postgres_explicit_fail_closed_policy()
		{
			await using var db = Connect(_connection); await db.OpenAsync(); await using var transaction = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead);
			Func<Task> insert = async () => { await db.ExecuteAsync($"INSERT INTO {Q("InventoryTypes")} ({Q("DepartmentId")},{Q("Type")}) VALUES(77,'isolation synthetic')", transaction: transaction); };
			if (_type == DatabaseTypes.Postgres) await insert.Should().ThrowAsync<DbException>(); else await insert();
			await transaction.RollbackAsync();
		}

		[Test]
		public async Task Populated_migration_refuses_rollback_without_erasing_inventory_evidence()
		{
			var seed = await SeedAsync(); var runner = _runner.GetRequiredService<IMigrationRunner>();
			try { FluentActions.Invoking(() => runner.MigrateDown(0)).Should().Throw<Exception>(); }
			finally { _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp(); }
			using var uow = new UnitOfWork(Connections()); (await Store(uow).GetAsync<InventoryItem>(77, seed.Item.Id)).Should().NotBeNull();
		}

		[Test]
		public async Task Department_cleanup_refuses_active_legal_holds_without_changing_inventory_or_shared_evidence()
		{
			await SeedCleanupEvidenceAsync(); await SeedCleanupEvidenceAsync(88);
			await using var db = Connect(_connection); await db.OpenAsync();
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHolds")} VALUES(@id,77,NULL)", new { id = Guid.NewGuid().ToString("D") });
			var before = await CleanupSnapshotAsync(db);
			await using (var held = await db.BeginTransactionAsync())
			{
				await FluentActions.Awaiting(() => ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, held, 77, _type))
					.Should().ThrowAsync<InvalidOperationException>().WithMessage("*legal hold*");
				await held.CommitAsync(); // Refusal must precede every mutation, even if the caller commits.
			}
			(await CleanupSnapshotAsync(db)).Should().BeEquivalentTo(before);
			await FluentActions.Awaiting(() => InsertLegacyTypeAsync(db, 77)).Should().ThrowAsync<DbException>();
		}

		[Test]
		public async Task Department_cleanup_rollback_restores_every_inventory_row_history_and_legacy_writer_fence()
		{
			await SeedCleanupEvidenceAsync(); await SeedCleanupEvidenceAsync(88);
			await using var db = Connect(_connection); await db.OpenAsync();
			var before = await CleanupSnapshotAsync(db);
			await using (var rollback = await db.BeginTransactionAsync())
			{
				await ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, rollback, 77, _type);
				foreach (var table in InventoryTables.All.Values)
					(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q(table)} WHERE {Q("DepartmentId")}=77", transaction: rollback)).Should().Be(0, table);
				await rollback.RollbackAsync();
			}
			(await CleanupSnapshotAsync(db)).Should().BeEquivalentTo(before);
			await FluentActions.Awaiting(() => InsertLegacyTypeAsync(db, 77)).Should().ThrowAsync<DbException>();
		}

		[Test]
		public async Task Department_cleanup_purges_inventory_child_first_and_preserves_other_producers_and_departments()
		{
			await SeedCleanupEvidenceAsync(); await SeedCleanupEvidenceAsync(88);
			await using var db = Connect(_connection); await db.OpenAsync();
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHolds")} VALUES(@released,77,@now),(@foreign,88,NULL)",
				new { released = Guid.NewGuid().ToString("D"), foreign = Guid.NewGuid().ToString("D"), now = DateTime.UtcNow });
			var foreignBefore = await CleanupSnapshotAsync(db, 88);
			foreach (var table in InventoryTables.All.Values)
				(await LegacyCountAsync(db, table, 77)).Should().BePositive("the test must exercise deletion of " + table);
			await using (var commit = await db.BeginTransactionAsync())
			{
				// No ChecklistDefinitions table exists in this fixture: Inventory cleanup must still run.
				await ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, commit, 77, _type);
				(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("Inventories")} WHERE {Q("DepartmentId")}=77", transaction: commit)).Should().Be(1);
				// Legacy data is retained until its separate parent-deletion stage, in the same transaction.
				await db.ExecuteAsync($"DELETE FROM {Q("Inventories")} WHERE {Q("DepartmentId")}=77; DELETE FROM {Q("InventoryTypes")} WHERE {Q("DepartmentId")}=77", transaction: commit);
				await commit.CommitAsync();
			}
			foreach (var table in InventoryTables.All.Values.Concat(new[] { "Inventories", "InventoryTypes" }))
				(await LegacyCountAsync(db, table, 77)).Should().Be(0, table);
			(await CleanupSnapshotAsync(db, 88)).Should().BeEquivalentTo(foreignBefore);
			(await db.QueryAsync<int>($"SELECT {Q("TriggerEventType")} FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=77"))
				.Should().BeEquivalentTo(new[] { 0, 67, 70 });
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")} IN (SELECT {Q("WorkflowRunId")} FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=77)")).Should().Be(3);
			(await db.QueryAsync<string>($"SELECT {Q("ProducerSubsystem")} FROM {Q("DomainEventOutbox")} WHERE {Q("DepartmentId")}=77"))
				.Should().BeEquivalentTo(new[] { "Records", "Checklists", "WorkOrders" });
			(await db.QueryAsync<int>($"SELECT {Q("LogType")} FROM {Q("AuditLogs")} WHERE {Q("DepartmentId")}=77")).Should().BeEquivalentTo(new[] { 0 });
			(await LegacyCountAsync(db, "RmsRecordLegalHolds", 77)).Should().Be(1, "retention records belong to the caller's separate retention policy");
			(await LegacyCountAsync(db, "Contacts", 77)).Should().Be(1, "Inventory cleanup must preserve the Contacts-owned organization identity");
			await FluentActions.Awaiting(() => InsertLegacyTypeAsync(db, 88)).Should().ThrowAsync<DbException>();
		}
	}
}
