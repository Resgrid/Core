using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		[Test]
		public async Task Historical_inventory_reads_continue_past_the_501_row_lookahead()
		{
			var item = Item(InventoryTrackingMode.Serialized); var first = Location(InventoryLocationType.Unit, 101); var second = Location(InventoryLocationType.Unit, 102);
			var asset = await CreateAsset(item, first);
			for (var index = 1; index <= 500; index++)
				_store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = item.Id, AssetId = asset.Id, EntryId = 1000 + index,
					OccurredOn = _clock.Utc.AddSeconds(index), FromLocationId = first.Id, ToLocationId = index == 500 ? second.Id : first.Id,
					TransactionType = (int)InventoryTransactionType.Transfer, NewStatus = (int)InventoryAssetStatus.InService, Quantity = 1, Content = "{\"ItemName\":\"Equipment\"}" });
			_clock.Advance(TimeSpan.FromSeconds(600));
			(await _service.AtCallAsync(ChecklistActor(), 25, _clock.Utc, new[] { 101 }, false)).Should().BeEmpty();
		}

		[Test]
		public async Task Vendor_archive_already_requires_contact_view_through_the_catalog_read()
		{
			var vendor = _store.Seed(new InventoryVendor { DepartmentId = Department, ContactId = "company", Content = "{}" });
			_auth.Setup(a => a.RequireAsync(_actor, false, PermissionTypes.ContactView, null)).ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			await Fails(() => _service.ArchiveAsync<InventoryVendor>(_actor, vendor.Id, vendor.Revision), "PermissionRequired", 403);
			_store.All<InventoryVendor>().Single().IsDeleted.Should().BeFalse(); _audits.Should().BeEmpty();
		}

		[Test]
		public async Task Vendor_choices_resolve_one_protected_batch_without_refetching_contacts()
		{
			_companyContacts["first"] = new Contact { ContactId = "first", DepartmentId = Department, ContactType = 1, CompanyName = "First" };
			_companyContacts["second"] = new Contact { ContactId = "second", DepartmentId = Department, ContactType = 1, CompanyName = "Second" };
			var choices = await _service.GetVendorContactsAsync(_actor);
			choices.Select(c => c.Name).Should().BeEquivalentTo("First", "Second");
			_contacts.Verify(c => c.GetContactByIdAsync(It.IsAny<string>()), Times.Never);
			_read.Verify(r => r.ResolveContactsForReadAsync(Department, It.Is<IReadOnlyList<Contact>>(c => c.Count == 2), _actor.GrantToken, _actor.UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Bulk_refresh_resolves_deleted_and_inactive_item_alerts_and_preserves_live_totals()
		{
			var location = Location(); var items = new[] { Item(), Item(), Item() };
			foreach (var item in items) { item.Content = JsonConvert.SerializeObject(new InventoryItemContent { Name = "Stock", MinLevel = 3 }); _store.Seed(item); SeedStock(item, location, 1); }
			await _service.RefreshAlertsAsync(_actor); _store.All<InventoryAlert>().Count(a => a.Status == 0).Should().Be(3);
			items[0].IsDeleted = true; items[1].IsActive = false; _store.Seed(items[0]); _store.Seed(items[1]);
			await _service.RefreshAlertsAsync(_actor);
			_store.All<InventoryAlert>().Where(a => a.Status == 0).Should().ContainSingle().Which.ItemId.Should().Be(items[2].Id);
			_store.All<InventoryAlert>().Count(a => a.Status == 1).Should().Be(2);
		}

		[Test]
		public async Task Batch_catalog_read_preserves_missing_row_and_holder_denial_failures()
		{
			var item = Item(); var first = Location(); var denied = Location();
			var a = _store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = item.Id, FromLocationId = first.Id, Content = "{}" });
			var b = _store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = item.Id, FromLocationId = denied.Id, Content = "{}" });
			(await _service.GetManyAsync<InventoryTransaction>(_actor, new[] { a.Id, b.Id, a.Id })).Should().HaveCount(2);
			_deniedLocations.Add(denied.Id);
			await Fails(() => _service.GetManyAsync<InventoryTransaction>(_actor, new[] { a.Id, b.Id }), "LocationUnavailable", 404);
			await Fails(() => _service.GetManyAsync<InventoryTransaction>(_actor, new[] { a.Id, Guid.NewGuid().ToString("D") }), "Unavailable", 404);
		}
	}
}
