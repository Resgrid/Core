using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Model.Inventories;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class InventoryDatabaseTests
	{
		[Test]
		public void Operations_catalog_covers_count_snapshots_and_null_only_alert_content_slots()
		{
			var catalog = new ProtectedFieldCatalog();
			var bindings = AdpTableBindings.ForVersionRange(catalog, 21, 22).ToArray();
			bindings.Select(b => b.TableName).Should().BeEquivalentTo(new[] { "InventoryCounts", "InventoryCountItems", "InventoryAlerts", "InventoryAlertDeliveries" });
			foreach (var binding in bindings)
			{
				catalog.GetForTable(binding.TableName).Should().ContainSingle().Which.AddedInCatalogVersion.Should().Be(22);
				catalog.GetForTableAndVersion(binding.TableName, 21).Should().BeEmpty();
				binding.DepartmentColumn.Should().Be("DepartmentId"); binding.PkColumn.Should().Be("Id");
				binding.Columns.Select(c => c.FieldId).Should().Equal(binding.TableName.ToLowerInvariant() + ".content");
			}
		}

		[Test]
		public async Task Count_schema_enforces_tenant_item_and_position_identity_without_rejecting_negative_opening_stock()
		{
			var seed = await SeedAsync(); var other = await SeedAsync(); var foreign = await SeedAsync(88);
			var count = M5DatabaseCount(seed.Location); var foreignCount = M5DatabaseCount(foreign.Location);
			var lot = M5DatabaseLot(seed.Item); var otherLot = M5DatabaseLot(other.Item); var foreignLot = M5DatabaseLot(foreign.Item);
			var asset = M5DatabaseAsset(seed.Item, seed.Location); var otherAsset = M5DatabaseAsset(other.Item, seed.Location); var foreignAsset = M5DatabaseAsset(foreign.Item, foreign.Location);
			await WriteAsync(async store => { await store.InsertAsync(count); await store.InsertAsync(lot); await store.InsertAsync(otherLot); await store.InsertAsync(asset); await store.InsertAsync(otherAsset); });
			await WriteAsync(async store => { await store.InsertAsync(foreignCount); await store.InsertAsync(foreignLot); await store.InsertAsync(foreignAsset); }, 88);
			foreach (var corrupt in new Action<InventoryCountItem>[]
			{
				row => row.DepartmentId = 88, row => row.CountId = foreignCount.Id, row => row.ItemId = foreign.Item.Id,
				row => row.LocationId = foreign.Location.Id, row => row.LotId = foreignLot.Id, row => row.AssetId = foreignAsset.Id,
				row => row.LotId = otherLot.Id, row => row.AssetId = otherAsset.Id, row => row.CountedQuantity = -0.000001m
			})
				await RejectAsync(store => { var row = M5DatabaseLine(count, seed.Item, seed.Location); corrupt(row); return store.InsertAsync(row); });
			var line = M5DatabaseLine(count, seed.Item, seed.Location); line.ExpectedQuantity = -1.123456m; line.CountedQuantity = 0;
			var lotLine = M5DatabaseLine(count, seed.Item, seed.Location); lotLine.LotId = lot.Id;
			var assetLine = M5DatabaseLine(count, seed.Item, seed.Location); assetLine.AssetId = asset.Id;
			await WriteAsync(async store => { await store.InsertAsync(line); await store.InsertAsync(lotLine); await store.InsertAsync(assetLine); });
			await RejectAsync(store => store.InsertAsync(M5DatabaseLine(count, seed.Item, seed.Location)));
			await RejectAsync(store => { var duplicate = M5DatabaseLine(count, seed.Item, seed.Location); duplicate.LotId = lot.Id; return store.InsertAsync(duplicate); });
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			var saved = await repository.GetAsync<InventoryCountItem>(77, line.Id); saved.ExpectedQuantity.Should().Be(-1.123456m); saved.CountedQuantity.Should().Be(0);
			(await repository.RelatedAsync<InventoryCountItem>(77, "CountId", count.Id)).Should().HaveCount(3);
			(await repository.RelatedAsync<InventoryCountItem>(88, "CountId", count.Id)).Should().BeEmpty();
			(await repository.GetAsync<InventoryCount>(88, count.Id)).Should().BeNull();
		}

		[Test]
		public async Task Count_schema_bounds_status_fingerprint_and_serialized_observations()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88);
			foreach (var corrupt in new Action<InventoryCount>[]
			{
				row => row.Status = -1, row => row.Status = 4, row => row.Revision = 0,
				row => row.SnapshotFingerprint = "short", row => row.LocationId = foreign.Location.Id,
				row => row.OperationId = Guid.NewGuid().ToString("D")
			})
				await RejectAsync(store => { var row = M5DatabaseCount(seed.Location); corrupt(row); return store.InsertAsync(row); });
			var count = M5DatabaseCount(seed.Location); var asset = M5DatabaseAsset(seed.Item, seed.Location);
			await WriteAsync(async store => { await store.InsertAsync(count); await store.InsertAsync(asset); });
			foreach (var corrupt in new Action<InventoryCountItem>[] { row => row.ExpectedQuantity = -1, row => row.ExpectedQuantity = 2, row => row.CountedQuantity = 0.5m, row => row.CountedQuantity = 2 })
				await RejectAsync(store => { var line = M5DatabaseLine(count, seed.Item, seed.Location); line.AssetId = asset.Id; corrupt(line); return store.InsertAsync(line); });
			var serialized = M5DatabaseLine(count, seed.Item, seed.Location); serialized.AssetId = asset.Id; serialized.CountedQuantity = 0;
			await WriteAsync(store => store.InsertAsync(serialized));
			using var uow = new UnitOfWork(Connections());
			(await Store(uow).GetAsync<InventoryCountItem>(77, serialized.Id)).CountedQuantity.Should().Be(0);
		}

		[Test]
		public async Task Count_ledger_backlinks_require_same_tenant_item_and_count_discriminator_and_remain_unique()
		{
			var seed = await SeedAsync(); var other = await SeedAsync(); var foreign = await SeedAsync(88);
			var count = M5DatabaseCount(seed.Location); var otherCount = M5DatabaseCount(seed.Location); var foreignCount = M5DatabaseCount(foreign.Location);
			var line = M5DatabaseLine(count, seed.Item, seed.Location); var otherLine = M5DatabaseLine(otherCount, other.Item, seed.Location); var foreignLine = M5DatabaseLine(foreignCount, foreign.Item, foreign.Location);
			var otherTransaction = Posting(other.Item, null, other.Location, 1); var foreignTransaction = Posting(foreign.Item, null, foreign.Location, 1);
			await WriteAsync(async store => { await store.InsertAsync(count); await store.InsertAsync(otherCount); await store.InsertAsync(line); await store.InsertAsync(otherLine); await store.InsertAsync(otherTransaction); });
			await WriteAsync(async store => { await store.InsertAsync(foreignCount); await store.InsertAsync(foreignLine); await store.InsertAsync(foreignTransaction); }, 88);
			foreach (var corrupt in new Action<InventoryTransaction>[]
			{
				row => row.CountItemId = foreignLine.Id, row => row.CountItemId = otherLine.Id,
				row => row.ReferenceType = (int)InventoryReferenceType.None, row => row.TransactionType = (int)InventoryTransactionType.Adjust
			})
				await RejectAsync(store => { var row = M5DatabaseCountPosting(count, line, seed.Item, seed.Location); corrupt(row); return store.InsertAsync(row); });
			foreach (var transactionId in new[] { otherTransaction.Id, foreignTransaction.Id })
				await RejectAsync(store => { var changed = M5DatabaseLine(count, seed.Item, other.Location); changed.TransactionId = transactionId; return store.InsertAsync(changed); });
			var receipt = M5DatabaseCountPosting(count, line, seed.Item, seed.Location);
			await WriteAsync(store => store.InsertAsync(receipt));
			await RejectAsync(store => store.InsertAsync(M5DatabaseCountPosting(count, line, seed.Item, seed.Location)));
			line.TransactionId = receipt.Id; line.Revision++;
			await WriteAsync(store => store.UpdateAsync(line, 1));
			await RejectAsync(store => { var duplicate = M5DatabaseLine(count, seed.Item, other.Location); duplicate.TransactionId = receipt.Id; return store.InsertAsync(duplicate); });
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.RelatedAsync<InventoryTransaction>(77, "CountItemId", line.Id)).Should().ContainSingle().Which.Id.Should().Be(receipt.Id);
			(await repository.RelatedAsync<InventoryCountItem>(77, "TransactionId", receipt.Id)).Should().ContainSingle().Which.Id.Should().Be(line.Id);
			await using var db = Connect(_connection);
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q("InventoryCountItems")} WHERE {Q("Id")}=@Id", new { line.Id })).Should().ThrowAsync<DbException>();
			await FluentActions.Awaiting(() => db.ExecuteAsync($"DELETE FROM {Q("InventoryTransactions")} WHERE {Q("Id")}=@Id", new { receipt.Id })).Should().ThrowAsync<DbException>();
		}

		[Test]
		public async Task Count_completion_and_negative_opening_correction_commit_or_rollback_with_their_ledger_and_stock()
		{
			var seed = await SeedAsync(); var count = M5DatabaseCount(seed.Location); var line = M5DatabaseLine(count, seed.Item, seed.Location);
			line.ExpectedQuantity = -1.123456m; line.CountedQuantity = 0;
			await WriteAsync(async store => { await store.InsertAsync(count); await store.InsertAsync(line); await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, line.ExpectedQuantity, "inventory-test-author"); });
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			async Task<InventoryTransaction> Complete()
			{
				await uow.CreateOrGetConnectionAsync(); await repository.LockDepartmentAsync(77);
				var currentCount = await repository.GetAsync<InventoryCount>(77, count.Id); var currentLine = await repository.GetAsync<InventoryCountItem>(77, line.Id);
				var receipt = M5DatabaseCountPosting(currentCount, currentLine, seed.Item, seed.Location, 1.123456m);
				await repository.InsertAsync(receipt); await repository.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, receipt.Quantity, "inventory-test-author");
				currentLine.TransactionId = receipt.Id; currentLine.Revision++; await repository.UpdateAsync(currentLine, 1);
				currentCount.Status = 2; currentCount.CompletedOn = DateTime.UtcNow; currentCount.Revision++;
				currentCount.Content = "SYNTHETIC-OPAQUE-COUNT"; currentCount.IsProtected = true; await repository.UpdateAsync(currentCount, 1);
				return receipt;
			}
			var abandoned = await Complete(); uow.DiscardChanges();
			(await repository.GetAsync<InventoryTransaction>(77, abandoned.Id)).Should().BeNull();
			(await repository.GetAsync<InventoryCountItem>(77, line.Id)).TransactionId.Should().BeNull();
			(await repository.GetAsync<InventoryCount>(77, count.Id)).Status.Should().Be(0);
			(await repository.RelatedAsync<InventoryStock>(77, "ItemId", seed.Item.Id)).Single().Quantity.Should().Be(-1.123456m);
			var completed = await Complete(); uow.CommitChanges();
			(await repository.GetAsync<InventoryTransaction>(77, completed.Id)).CountItemId.Should().Be(line.Id);
			(await repository.GetAsync<InventoryCountItem>(77, line.Id)).TransactionId.Should().Be(completed.Id);
			var saved = await repository.GetAsync<InventoryCount>(77, count.Id); saved.Status.Should().Be(2); saved.Revision.Should().Be(2);
			saved.Content.Should().Be("SYNTHETIC-OPAQUE-COUNT"); saved.IsProtected.Should().BeTrue();
			(await repository.RelatedAsync<InventoryStock>(77, "ItemId", seed.Item.Id)).Single().Quantity.Should().Be(0);
		}

		[Test]
		public async Task Alert_schema_isolates_tenants_items_and_issuances_and_accepts_only_reviewed_metadata()
		{
			var seed = await SeedAsync(); var other = await SeedAsync(); var foreign = await SeedAsync(88);
			var otherAsset = M5DatabaseAsset(other.Item, other.Location); var otherLot = M5DatabaseLot(other.Item);
			var issuance = M5DatabaseIssuance(other.Item, other.Location); var foreignIssuance = M5DatabaseIssuance(foreign.Item, foreign.Location);
			await WriteAsync(async store => { await store.InsertAsync(otherAsset); await store.InsertAsync(otherLot); await store.InsertAsync(issuance); });
			await WriteAsync(store => store.InsertAsync(foreignIssuance), 88);
			foreach (var corrupt in new Action<InventoryAlert>[]
			{
				row => row.DepartmentId = 88, row => row.ItemId = foreign.Item.Id, row => row.LocationId = foreign.Location.Id,
				row => row.AssetId = otherAsset.Id, row => row.LotId = otherLot.Id, row => row.IssuanceId = issuance.Id,
				row => row.IssuanceId = foreignIssuance.Id, row => row.AlertType = -1, row => row.AlertType = 4,
				row => row.Status = -1, row => row.Status = 2, row => row.DedupKey = "short", row => row.Content = "{}"
			})
				await RejectAsync(store => { var row = M5DatabaseAlert(seed.Item, seed.Location); corrupt(row); return store.InsertAsync(row); });
			var alert = M5DatabaseAlert(other.Item, other.Location); alert.IssuanceId = issuance.Id; alert.AlertType = 3; alert.Quantity = 0.123456m;
			await WriteAsync(store => store.InsertAsync(alert));
			using var uow = new UnitOfWork(Connections()); var saved = await Store(uow).GetAsync<InventoryAlert>(77, alert.Id);
			saved.Content.Should().BeNull(); saved.Quantity.Should().Be(0.123456m); saved.IssuanceId.Should().Be(issuance.Id);
		}

		[Test]
		public async Task Alerts_have_one_open_identity_and_one_delivery_per_recipient_with_revision_control()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88);
			var alert = M5DatabaseAlert(seed.Item, seed.Location); var foreignAlert = M5DatabaseAlert(foreign.Item, foreign.Location); foreignAlert.DedupKey = alert.DedupKey;
			await WriteAsync(store => store.InsertAsync(alert)); await WriteAsync(store => store.InsertAsync(foreignAlert), 88);
			await RejectAsync(store => { var duplicate = M5DatabaseAlert(seed.Item, seed.Location); duplicate.DedupKey = alert.DedupKey; return store.InsertAsync(duplicate); });
			var delivery = M5DatabaseDelivery(alert); await WriteAsync(store => store.InsertAsync(delivery));
			foreach (var corrupt in new Action<InventoryAlertDelivery>[]
			{
				row => row.AlertId = foreignAlert.Id, row => row.DepartmentId = 88, row => row.UserId = "missing-user",
				row => row.State = -1, row => row.State = 4, row => row.AttemptCount = -1, row => row.Content = "{}"
			})
				await RejectAsync(store => { var row = M5DatabaseDelivery(alert, "inventory-test-other"); corrupt(row); return store.InsertAsync(row); });
			await RejectAsync(store => store.InsertAsync(M5DatabaseDelivery(alert)));
			alert.Status = 1; alert.ResolvedOn = DateTime.UtcNow; alert.Revision++;
			var reopened = M5DatabaseAlert(seed.Item, seed.Location); reopened.DedupKey = alert.DedupKey;
			delivery.State = 1; delivery.AttemptCount = 1; delivery.ClaimToken = Guid.NewGuid().ToString("D"); delivery.LeaseUntil = DateTime.UtcNow.AddMinutes(30); delivery.Revision++;
			await WriteAsync(async store => { await store.UpdateAsync(alert, 1); await store.InsertAsync(reopened); await store.UpdateAsync(delivery, 1); await store.InsertAsync(M5DatabaseDelivery(alert, "inventory-test-other")); await store.InsertAsync(M5DatabaseDelivery(reopened)); });
			await FluentActions.Awaiting(() => WriteAsync(store => store.UpdateAsync(delivery, 1))).Should().ThrowAsync<InvalidOperationException>();
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.RelatedAsync<InventoryAlert>(77, "DedupKey", alert.DedupKey)).Should().HaveCount(2).And.ContainSingle(a => a.Status == 0);
			(await repository.RelatedAsync<InventoryAlertDelivery>(77, "AlertId", alert.Id)).Should().HaveCount(2);
			(await repository.RelatedAsync<InventoryAlertDelivery>(77, "UserId", delivery.UserId)).Should().HaveCount(2);
			(await repository.GetAsync<InventoryAlertDelivery>(88, delivery.Id)).Should().BeNull();
			var saved = await repository.GetAsync<InventoryAlertDelivery>(77, delivery.Id); saved.ClaimToken.Should().Be(delivery.ClaimToken); saved.Revision.Should().Be(2); saved.Content.Should().BeNull();
		}

		[Test]
		public async Task Counts_and_alerts_prevent_schema_rollback_from_erasing_completed_evidence()
		{
			var seed = await SeedAsync(); var count = M5DatabaseCount(seed.Location); var line = M5DatabaseLine(count, seed.Item, seed.Location);
			var alert = M5DatabaseAlert(seed.Item, seed.Location); var delivery = M5DatabaseDelivery(alert);
			await WriteAsync(async store => { await store.InsertAsync(count); await store.InsertAsync(line); await store.InsertAsync(alert); await store.InsertAsync(delivery); });
			var receipt = M5DatabaseCountPosting(count, line, seed.Item, seed.Location);
			await WriteAsync(async store => { await store.InsertAsync(receipt); line.TransactionId = receipt.Id; line.Revision++; await store.UpdateAsync(line, 1); });
			await using var db = Connect(_connection); var before = await CleanupSnapshotAsync(db); var runner = _runner.GetRequiredService<IMigrationRunner>();
			try { FluentActions.Invoking(() => runner.MigrateDown(201)).Should().Throw<Exception>(); }
			finally { _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp(); }
			(await CleanupSnapshotAsync(db)).Should().BeEquivalentTo(before);
		}

		[Test]
		public async Task Latest_inventory_entry_query_returns_zero_for_empty_tenants_and_only_their_committed_maximum()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88); using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.LastEntryAsync(77)).Should().Be(0); (await repository.LastEntryAsync(88)).Should().Be(0);
			var first = Posting(seed.Item, null, seed.Location, 1); var latest = Posting(seed.Item, null, seed.Location, 2);
			await WriteAsync(async store => { await store.InsertAsync(first); await store.InsertAsync(latest); });
			var other = Posting(foreign.Item, null, foreign.Location, 3); await WriteAsync(store => store.InsertAsync(other), 88);
			other.EntryId.Should().BeGreaterThan(latest.EntryId);
			(await repository.LastEntryAsync(77)).Should().Be(latest.EntryId); (await repository.LastEntryAsync(88)).Should().Be(other.EntryId);
			await uow.CreateOrGetConnectionAsync(); await repository.LockDepartmentAsync(77);
			var abandoned = Posting(seed.Item, null, seed.Location, 4); await repository.InsertAsync(abandoned); uow.DiscardChanges();
			(await repository.LastEntryAsync(77)).Should().Be(latest.EntryId); (await repository.LastEntryAsync(99)).Should().Be(0);
		}

		[Test]
		public async Task Targeted_alert_queries_isolate_tenants_and_page_open_rows_beyond_lifetime_delivery_history()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88); var stamp = new DateTime(2026, 9, 9, 0, 0, 0);
			var open = M5DatabaseAlert(seed.Item, seed.Location); open.OpenedOn = stamp;
			var history = Enumerable.Range(0, 5001).Select(_ =>
			{
				var row = M5DatabaseAlert(seed.Item, seed.Location); row.DedupKey = open.DedupKey; row.Status = 1; row.OpenedOn = stamp.AddDays(-1); return row;
			}).ToArray();
			await using var db = Connect(_connection); await db.OpenAsync();
			await M5InsertAlertMetadataAsync(db, seed.Item, seed.Location, history, includeCompletedDeliveries: true);
			var delivery = M5DatabaseDelivery(open); var otherUserDelivery = M5DatabaseDelivery(open, "inventory-test-other");
			await WriteAsync(async store => { await store.InsertAsync(open); await store.InsertAsync(delivery); await store.InsertAsync(otherUserDelivery); });
			var otherTenantAlert = M5DatabaseAlert(foreign.Item, foreign.Location); otherTenantAlert.DedupKey = open.DedupKey; otherTenantAlert.OpenedOn = stamp.AddDays(-2);
			var otherTenantDelivery = M5DatabaseDelivery(otherTenantAlert);
			await WriteAsync(async store => { await store.InsertAsync(otherTenantAlert); await store.InsertAsync(otherTenantDelivery); }, 88);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.OpenAlertAsync(77, open.DedupKey)).Id.Should().Be(open.Id);
			(await repository.OpenAlertAsync(88, open.DedupKey)).Id.Should().Be(otherTenantAlert.Id);
			(await repository.OpenAlertAsync(77, new string('F', 64))).Should().BeNull();
			(await repository.AlertDeliveryAsync(77, open.Id, delivery.UserId)).Id.Should().Be(delivery.Id);
			(await repository.AlertDeliveryAsync(77, open.Id, otherUserDelivery.UserId)).Id.Should().Be(otherUserDelivery.Id);
			(await repository.AlertDeliveryAsync(88, open.Id, delivery.UserId)).Should().BeNull();
			(await repository.AlertDeliveryAsync(77, otherTenantAlert.Id, delivery.UserId)).Should().BeNull();
			(await repository.AlertDeliveryAsync(77, open.Id, "missing-user")).Should().BeNull();
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("InventoryAlertDeliveries")} WHERE {Q("DepartmentId")}=77 AND {Q("UserId")}=@UserId", new { delivery.UserId })).Should().Be(5002);
			var pending = Enumerable.Range(0, 502).Select(i =>
			{
				var row = M5DatabaseAlert(seed.Item, seed.Location); row.OpenedOn = stamp.AddHours(1).AddMinutes(i / 2); return row;
			}).ToArray();
			await M5InsertAlertMetadataAsync(db, seed.Item, seed.Location, pending);
			var expected = new[] { open }.Concat(pending).OrderBy(a => a.OpenedOn).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id).ToArray();
			var first = await repository.OpenAlertsAsync(77); var second = await repository.OpenAlertsAsync(77, 500);
			first.Should().HaveCount(501).And.OnlyContain(a => a.DepartmentId == 77 && a.Status == 0);
			first.Select(a => a.Id).Should().Equal(expected.Take(501)); second.Select(a => a.Id).Should().Equal(expected.Skip(500));
			first.Take(500).Concat(second).Select(a => a.Id).Should().Equal(expected);
			(await repository.OpenAlertsAsync(88)).Should().ContainSingle().Which.Id.Should().Be(otherTenantAlert.Id);
			(await repository.OpenAlertsAsync(77, 503)).Should().BeEmpty();
			await FluentActions.Awaiting(() => repository.OpenAlertsAsync(77, -1)).Should().ThrowAsync<ArgumentOutOfRangeException>();
		}

		private async Task M5InsertAlertMetadataAsync(DbConnection db, InventoryItem item, InventoryLocation location, InventoryAlert[] rows, bool includeCompletedDeliveries = false)
		{
			// Bounded parameter batches keep this lifetime-history regression fast on both real dialects.
			await using var transaction = await db.BeginTransactionAsync();
			foreach (var batch in rows.Chunk(300))
			{
				var parameters = new DynamicParameters(new { item.DepartmentId, ItemId = item.Id, LocationId = location.Id, CreatedOn = new DateTime(2026, 9, 9), UserId = "inventory-test-author" });
				var alerts = new System.Collections.Generic.List<string>(); var deliveries = new System.Collections.Generic.List<string>();
				for (var i = 0; i < batch.Length; i++)
				{
					parameters.Add("Id" + i, batch[i].Id); parameters.Add("Key" + i, batch[i].DedupKey); parameters.Add("Status" + i, batch[i].Status);
					parameters.Add("Opened" + i, DateTime.SpecifyKind(batch[i].OpenedOn, DateTimeKind.Unspecified));
					alerts.Add($"(@Id{i},@DepartmentId,@CreatedOn,@UserId,0,@Key{i},@ItemId,@LocationId,@Status{i},@Opened{i})");
					if (includeCompletedDeliveries)
					{
						parameters.Add("Delivery" + i, Guid.NewGuid().ToString("D"));
						deliveries.Add($"(@Delivery{i},@DepartmentId,@CreatedOn,@UserId,@Id{i},@UserId,2,@CreatedOn,1,@CreatedOn)");
					}
				}
				var sql = $"INSERT INTO {Q("InventoryAlerts")} ({Q("Id")},{Q("DepartmentId")},{Q("CreatedOn")},{Q("CreatedBy")},{Q("AlertType")},{Q("DedupKey")},{Q("ItemId")},{Q("LocationId")},{Q("Status")},{Q("OpenedOn")}) VALUES " + string.Join(",", alerts) + ";";
				if (includeCompletedDeliveries) sql += $"INSERT INTO {Q("InventoryAlertDeliveries")} ({Q("Id")},{Q("DepartmentId")},{Q("CreatedOn")},{Q("CreatedBy")},{Q("AlertId")},{Q("UserId")},{Q("State")},{Q("NextAttemptOn")},{Q("AttemptCount")},{Q("HandedOffOn")}) VALUES " + string.Join(",", deliveries) + ";";
				await db.ExecuteAsync(sql, parameters, transaction);
			}
			await transaction.CommitAsync();
		}

		private static InventoryCount M5DatabaseCount(InventoryLocation location)
		{
			var row = NewRow<InventoryCount>(location.DepartmentId); row.LocationId = location.Id; row.SnapshotFingerprint = new string('A', 64); row.SnapshotOn = DateTime.UtcNow; return row;
		}
		private static InventoryCountItem M5DatabaseLine(InventoryCount count, InventoryItem item, InventoryLocation location)
		{
			var row = NewRow<InventoryCountItem>(count.DepartmentId); row.CountId = count.Id; row.ItemId = item.Id; row.LocationId = location.Id; row.ExpectedQuantity = 1; return row;
		}
		private static InventoryLot M5DatabaseLot(InventoryItem item)
		{
			var row = NewRow<InventoryLot>(item.DepartmentId); row.ItemId = item.Id; row.ReceivedOn = DateTime.UtcNow; return row;
		}
		private static InventoryAsset M5DatabaseAsset(InventoryItem item, InventoryLocation location)
		{
			var row = NewRow<InventoryAsset>(item.DepartmentId); row.ItemId = item.Id; row.CurrentLocationId = location.Id; return row;
		}
		private static InventoryTransaction M5DatabaseCountPosting(InventoryCount count, InventoryCountItem line, InventoryItem item, InventoryLocation location, decimal quantity = 1)
		{
			var row = Posting(item, null, location, quantity, InventoryTransactionType.Count); row.CountItemId = line.Id; row.ReferenceType = (int)InventoryReferenceType.Count; row.ReferenceId = count.Id; return row;
		}
		private static InventoryIssuance M5DatabaseIssuance(InventoryItem item, InventoryLocation location)
		{
			var row = NewRow<InventoryIssuance>(item.DepartmentId); row.ItemId = item.Id; row.LocationId = location.Id; row.Quantity = 1; row.IssuedOn = DateTime.UtcNow; row.IssuedToUserId = "inventory-test-author"; return row;
		}
		private static InventoryAlert M5DatabaseAlert(InventoryItem item, InventoryLocation location)
		{
			var row = NewRow<InventoryAlert>(item.DepartmentId); row.Content = null; row.ItemId = item.Id; row.LocationId = location.Id; row.DedupKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); row.OpenedOn = DateTime.UtcNow; return row;
		}
		private static InventoryAlertDelivery M5DatabaseDelivery(InventoryAlert alert, string user = "inventory-test-author")
		{
			var row = NewRow<InventoryAlertDelivery>(alert.DepartmentId); row.Content = null; row.AlertId = alert.Id; row.UserId = user; row.NextAttemptOn = DateTime.UtcNow; return row;
		}
	}
}
