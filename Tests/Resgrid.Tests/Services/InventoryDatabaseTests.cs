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
using Resgrid.Model.Inventories;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Transactions;

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
			InventoryTables.All.Should().HaveCount(13);
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
			await FluentActions.Awaiting(() => InsertLegacyTypeAsync(db, 88)).Should().ThrowAsync<DbException>();
		}
	}
}
