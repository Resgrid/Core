using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Equipment still held by a removed, disabled or hidden member raises a DepartedHolder recovery alert (trigger 188).
	/// The existing expiry and overdue-return alerts keep running for the same gear.
	/// </summary>
	public sealed partial class InventoryModernizationTests
	{
		private InventoryLocation PersonnelLocation(string userId) => _store.Seed(new InventoryLocation {
			DepartmentId = Department, LocationType = (int)InventoryLocationType.Personnel, UserId = userId, CreatedBy = _actor.UserId, CreatedOn = _clock.Utc,
			Content = JsonConvert.SerializeObject(new InventoryLabel { Name = "Synthetic locker" }) });

		private List<InventoryAlert> DepartedAlerts() => _store.All<InventoryAlert>().Where(a => a.AlertType == (int)InventoryAlertType.DepartedHolder).ToList();

		[Test]
		public async Task Gear_held_by_a_departed_member_raises_one_recovery_alert_per_item_that_follows_recovery_and_reactivation()
		{
			var active = new HashSet<string> { "current-member" };
			_auth.Setup(x => x.ActiveMemberIdsAsync(Department)).ReturnsAsync(() => new HashSet<string>(active));
			var bulk = Item(); var serialized = Item(InventoryTrackingMode.Serialized);
			var departedLocker = PersonnelLocation("departed-member"); var currentLocker = PersonnelLocation("current-member"); var station = Location();
			SeedStock(bulk, departedLocker, 3); SeedStock(bulk, currentLocker, 5); SeedStock(bulk, station, 9);
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = serialized.Id, CurrentLocationId = departedLocker.Id, Status = (int)InventoryAssetStatus.Issued });
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = serialized.Id, CurrentLocationId = departedLocker.Id, Status = (int)InventoryAssetStatus.InService });
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = serialized.Id, CurrentLocationId = departedLocker.Id, Status = (int)InventoryAssetStatus.Lost });
			var overdue = _store.Seed(new InventoryIssuance { DepartmentId = Department, ItemId = bulk.Id, LocationId = departedLocker.Id, IssuedToUserId = "departed-member", Quantity = 3, Status = 0,
				ExpectedReturnOn = _clock.Utc.AddDays(-1) });

			await _service.SweepAlertsAsync(Department); await _service.SweepAlertsAsync(Department);

			var alerts = DepartedAlerts();
			alerts.Should().HaveCount(2).And.OnlyContain(a => a.LocationId == departedLocker.Id && a.Status == 0 && a.DueOn == null && a.Content == null);
			alerts.Single(a => a.ItemId == bulk.Id).Quantity.Should().Be(3);
			alerts.Single(a => a.ItemId == serialized.Id).Quantity.Should().Be(2, "a lost asset is no longer held");
			_store.All<InventoryAlert>().Should().ContainSingle(a => a.AlertType == (int)InventoryAlertType.OverdueReturn && a.IssuanceId == overdue.Id, "the overdue return keeps running for the same gear");
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryDepartedHolder).Should().Be(2, "the second sweep deduplicates");
			_events.Where(e => e.Trigger == WorkflowTriggerEventType.InventoryDepartedHolder).Should().OnlyContain(e => JObject.FromObject(e.Payload).Value<int>("AlertType") == (int)InventoryAlertType.DepartedHolder);
			_auth.Verify(x => x.ActiveMemberIdsAsync(Department), Times.Exactly(2), "membership is read once per sweep");

			SeedStock(bulk, departedLocker, 1); await _service.SweepAlertsAsync(Department);
			DepartedAlerts().Single(a => a.ItemId == bulk.Id).Quantity.Should().Be(1, "a partial recovery updates the open alert");
			SeedStock(bulk, departedLocker, 0); await _service.SweepAlertsAsync(Department);
			DepartedAlerts().Single(a => a.ItemId == bulk.Id).Status.Should().Be(1, "recovering the last of it resolves the alert");

			active.Add("departed-member"); await _service.SweepAlertsAsync(Department);
			DepartedAlerts().Should().OnlyContain(a => a.Status == 1, "a member who is active again is not a departed holder");
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryDepartedHolder).Should().Be(2);
		}

		[Test]
		public async Task Departed_holder_alerts_are_delivered_only_while_the_gear_is_still_held_and_survive_an_unreadable_roster()
		{
			HashSet<string> active = new();
			_auth.Setup(x => x.ActiveMemberIdsAsync(Department)).ReturnsAsync(() => active == null ? null : new HashSet<string>(active));
			var item = Item(); var locker = PersonnelLocation("departed-member"); SeedStock(item, locker, 2);
			await _service.SweepAlertsAsync(Department);
			var alert = DepartedAlerts().Single();

			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeTrue();
			_deniedLocations.Add(locker.Id);
			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse("delivery still applies the recipient's location scope");
			_deniedLocations.Remove(locker.Id);

			active = null; await _service.SweepAlertsAsync(Department);
			DepartedAlerts().Single().Status.Should().Be(0, "an unreadable roster neither resolves nor reopens the alert");
			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse("delivery waits until the holder is confirmed as departed");

			active = new HashSet<string> { "departed-member" };
			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse("the member is back");
		}

		[Test]
		public void The_departed_holder_trigger_is_an_inventory_trigger_and_its_alert_type_survives_workflow_routing()
		{
			InventoryWorkflowPayload.IsInventory((int)WorkflowTriggerEventType.InventoryDepartedHolder).Should().BeTrue();
			var routed = JObject.Parse(InventoryWorkflowPayload.Routing(new JObject { ["InventoryEvent"] = true, ["AlertType"] = (int)InventoryAlertType.DepartedHolder, ["LocationId"] = Guid.NewGuid().ToString("D") }));
			routed.Value<int>("AlertType").Should().Be((int)InventoryAlertType.DepartedHolder);
		}
	}
}
