using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Model.Inventories;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	public partial class InventoryDatabaseTests
	{
		// Upgrade reversals run before service cases create evidence guarded by later migrations.
		[Test, Order(1)]
		public async Task Tenant_holder_upgrade_rejects_existing_cross_department_links_without_rewriting_inventory()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			var seed = await SeedAsync();
			runner.MigrateDown(204);
			var invalid = NewRow<InventoryLocation>(); invalid.LocationType = 1; invalid.GroupId = 223;
			try
			{
				// The original, already-applied M0198 permitted this typed but cross-tenant reference.
				await WriteAsync(store => store.InsertAsync(invalid));
				FluentActions.Invoking(() => runner.MigrateUp()).Should().Throw<Exception>();
				await using var db = Connect(_connection);
				(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("InventoryLocations")} WHERE {Q("Id")}=@Id", new { invalid.Id })).Should().Be(1);
			}
			finally
			{
				await using var db = Connect(_connection);
				await db.ExecuteAsync($"DELETE FROM {Q("InventoryLocations")} WHERE {Q("Id")}=@Id", new { invalid.Id });
				_runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp();
			}
			await RejectAsync(store => store.InsertAsync(invalid));
			using var uow = new UnitOfWork(Connections());
			(await Store(uow).GetAsync<InventoryLocation>(77, seed.Location.Id)).Should().NotBeNull();
		}

		[Test]
		public async Task Claim_query_excludes_completed_leased_and_backoff_deliveries_for_only_the_selected_recipient()
		{
			var seed = await SeedAsync(); var now = DateTime.UtcNow;
			var alerts = new List<InventoryAlert>();
			await WriteAsync(async store =>
			{
				for (var index = 0; index < 6; index++)
				{
					var alert = NewRow<InventoryAlert>(); alert.Content = null; alert.ItemId = seed.Item.Id; alert.DedupKey = index.ToString("x64"); alert.OpenedOn = now;
					if (index == 5) alert.Status = 1;
					await store.InsertAsync(alert); alerts.Add(alert);
					if (index is 0 or 5) continue;
					var delivery = NewRow<InventoryAlertDelivery>(); delivery.Content = null; delivery.AlertId = alert.Id; delivery.UserId = "inventory-test-author";
					delivery.NextAttemptOn = index == 3 ? now.AddMinutes(5) : now.AddMinutes(-5);
					delivery.State = index == 1 ? 2 : 0; delivery.LeaseUntil = index == 2 ? now.AddMinutes(30) : null;
					await store.InsertAsync(delivery);
				}
			});
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.ClaimableAlertsAsync(77, "inventory-test-author", now)).Select(a => a.Id).Should().BeEquivalentTo(alerts[0].Id, alerts[4].Id);
			(await repository.ClaimableAlertsAsync(77, "another-recipient", now)).Should().HaveCount(5);
			(await repository.ClaimableAlertsAsync(88, "inventory-test-author", now)).Should().BeEmpty();
		}

		[Test]
		public async Task Batched_relationship_reads_scope_the_tenant_and_reject_unreviewed_columns()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88);
			using var uow = new UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.RelatedManyAsync<InventoryItem>(77, "Id", new[] { seed.Item.Id, foreign.Item.Id })).Should().ContainSingle().Which.Id.Should().Be(seed.Item.Id);
			(await repository.RelatedManyAsync<InventoryItem>(77, "Id", Array.Empty<string>())).Should().BeEmpty();
			await FluentActions.Awaiting(() => repository.RelatedManyAsync<InventoryItem>(77, "Content", new[] { "{}" })).Should().ThrowAsync<ArgumentException>();
		}
	}
}
