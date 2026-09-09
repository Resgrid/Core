using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class GdprExportProtectedDataTests
	{
		private static IInventoryStore EmptyInventory()
		{
			var store = new Mock<IInventoryStore>();
			store.SetReturnsDefault(Task.FromResult(new List<InventoryLocation>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryIssuance>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryAsset>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryTransaction>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryOperation>()));
			store.SetReturnsDefault(Task.FromResult(new List<RecordInventoryUsage>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryVendor>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryPurchaseOrder>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryPurchaseOrderItem>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryCount>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryCountItem>()));
			store.SetReturnsDefault(Task.FromResult(new List<InventoryAlertDelivery>()));
			return store.Object;
		}

		private void UseInventoryExport(IInventoryStore store, bool enforced = false, bool policyFailure = false)
		{
			var policy = new Mock<IDepartmentDataProtectionService>();
			if (policyFailure) policy.Setup(p => p.IsProtectionEnforcedAsync(DeptId)).ThrowsAsync(new InvalidOperationException("Synthetic policy failure"));
			else policy.Setup(p => p.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(enforced);
			var reminders = new Mock<IChecklistReminderRepository>();
			reminders.SetReturnsDefault(Task.FromResult(new List<Resgrid.Model.Checklists.ChecklistReminder>()));
			_service = new GdprDataExportService(_repository.Object, _userProfileService.Object, _memberSensitiveDataService.Object,
				_emergencyContactService.Object, _usersService.Object, _departmentsService.Object, _departmentGroupsService.Object,
				_personnelRolesService.Object, _actionLogsService.Object, _messageService.Object, _certificationService.Object,
				_trainingService.Object, _shiftsService.Object, _emailService.Object, new ChecklistWorkflowTests.MemoryStore(),
				new Lazy<IReadinessHistoryProtectionService>(() => new ReadinessHistoryProtectionService(Mock.Of<IProtectedWriteService>(), policy.Object)),
				reminders.Object, EmptyWorkOrders(), store);
		}

		private static T InventoryExportRow<T>(string creator = UserId, int department = DeptId, string content = "SUBJECT-EVIDENCE") where T : InventoryRow, new()
			=> new() { DepartmentId = department, CreatedBy = creator, Content = content };

		[Test]
		public void Inventory_export_requires_storage_at_construction()
		{
			FluentActions.Invoking(() => UseInventoryExport(null)).Should().Throw<ArgumentNullException>().WithParameterName("inventoryStore");
		}

		[Test]
		public async Task Authorship_of_shared_locations_does_not_export_other_holders_assets_or_movements()
		{
			var store = EmptyInventory();
			var shared = InventoryExportRow<InventoryLocation>(); shared.LocationType = (int)InventoryLocationType.Station; shared.GroupId = 5;
			var personal = InventoryExportRow<InventoryLocation>("other"); personal.UserId = UserId;
			var foreignAsset = InventoryExportRow<InventoryAsset>("other", content: "UNRELATED-CANARY"); foreignAsset.CurrentLocationId = shared.Id;
			var foreignMovement = InventoryExportRow<InventoryTransaction>("other", content: "UNRELATED-CANARY"); foreignMovement.ToLocationId = shared.Id;
			var heldAsset = InventoryExportRow<InventoryAsset>("other"); heldAsset.CurrentLocationId = personal.Id;
			InventoryExportPages(store, shared, personal); InventoryExportPages(store, foreignAsset, heldAsset); InventoryExportPages(store, foreignMovement);
			UseInventoryExport(store);
			var json = (await RunExportAsync())["inventory.json"]; var data = JObject.Parse(json);
			json.Should().NotContain("UNRELATED-CANARY"); data["Locations"].Should().HaveCount(2);
			data["Assets"].Should().ContainSingle(); data["Assets"][0]["Id"].Value<string>().Should().Be(heldAsset.Id); data["Transactions"].Should().BeEmpty();
		}

		private static void InventoryExportPages<T>(IInventoryStore store, params T[] rows) where T : InventoryRow
			=> Mock.Get(store).Setup(s => s.ListAsync<T>(DeptId, It.IsAny<int>()))
				.ReturnsAsync((int department, int skip) => rows.Skip(skip).Take(501).ToList());

		[Test]
		public async Task Inventory_export_includes_subject_relationships_and_excludes_unrelated_or_foreign_candidates()
		{
			var store = EmptyInventory();
			InventoryExportPages(store, InventoryExportRow<RecordInventoryUsage>(), InventoryExportRow<RecordInventoryUsage>("another-user", content: "UNRELATED-CANARY"), InventoryExportRow<RecordInventoryUsage>(department: 999, content: "FOREIGN-CANARY"));
			var location = InventoryExportRow<InventoryLocation>("another-user"); location.UserId = UserId;
			var authoredLocation = InventoryExportRow<InventoryLocation>();
			InventoryExportPages(store, location, authoredLocation,
				InventoryExportRow<InventoryLocation>("another-user", content: "UNRELATED-CANARY"), InventoryExportRow<InventoryLocation>(department: 999, content: "FOREIGN-CANARY"));
			var issuance = InventoryExportRow<InventoryIssuance>("another-user"); issuance.IssuedToUserId = UserId;
			InventoryExportPages(store, issuance, InventoryExportRow<InventoryIssuance>(),
				InventoryExportRow<InventoryIssuance>("another-user", content: "UNRELATED-CANARY"), InventoryExportRow<InventoryIssuance>(department: 999, content: "FOREIGN-CANARY"));
			var heldAsset = InventoryExportRow<InventoryAsset>("another-user"); heldAsset.CurrentLocationId = location.Id;
			InventoryExportPages(store, heldAsset, InventoryExportRow<InventoryAsset>(),
				InventoryExportRow<InventoryAsset>("another-user", content: "UNRELATED-CANARY"), InventoryExportRow<InventoryAsset>(department: 999, content: "FOREIGN-CANARY"));
			var issuedTransaction = InventoryExportRow<InventoryTransaction>("another-user"); issuedTransaction.IssuanceId = issuance.Id;
			var holderTransaction = InventoryExportRow<InventoryTransaction>("another-user"); holderTransaction.ToLocationId = location.Id;
			InventoryExportPages(store, issuedTransaction, holderTransaction, InventoryExportRow<InventoryTransaction>(),
				InventoryExportRow<InventoryTransaction>("another-user", content: "UNRELATED-CANARY"), InventoryExportRow<InventoryTransaction>(department: 999, content: "FOREIGN-CANARY"));
			InventoryExportPages(store, InventoryExportRow<InventoryOperation>(), InventoryExportRow<InventoryOperation>("another-user", content: "UNRELATED-CANARY"),
				InventoryExportRow<InventoryOperation>(department: 999, content: "FOREIGN-CANARY"));
			var vendor = InventoryExportRow<InventoryVendor>(); vendor.ContactId = Guid.NewGuid().ToString("D");
			var order = InventoryExportRow<InventoryPurchaseOrder>(); order.VendorId = vendor.Id;
			var line = InventoryExportRow<InventoryPurchaseOrderItem>(); line.PurchaseOrderId = order.Id;
			var unrelatedOrder = InventoryExportRow<InventoryPurchaseOrder>("another-user", content: "UNRELATED-CANARY"); unrelatedOrder.VendorId = vendor.Id;
			var unrelatedLine = InventoryExportRow<InventoryPurchaseOrderItem>("another-user", content: "UNRELATED-CANARY"); unrelatedLine.PurchaseOrderId = order.Id;
			InventoryExportPages(store, vendor, InventoryExportRow<InventoryVendor>("another-user", content: "UNRELATED-CANARY"), InventoryExportRow<InventoryVendor>(department: 999, content: "FOREIGN-CANARY"));
			InventoryExportPages(store, order, unrelatedOrder, InventoryExportRow<InventoryPurchaseOrder>(department: 999, content: "FOREIGN-CANARY"));
			InventoryExportPages(store, line, unrelatedLine, InventoryExportRow<InventoryPurchaseOrderItem>(department: 999, content: "FOREIGN-CANARY"));
			UseInventoryExport(store);
			var files = await RunExportAsync(); var json = files["inventory.json"]; var data = JObject.Parse(json);
			json.Should().Contain("SUBJECT-EVIDENCE").And.NotContain("UNRELATED-CANARY").And.NotContain("FOREIGN-CANARY");
			data["Locations"].Should().HaveCount(2); data["Issuances"].Should().HaveCount(2); data["Assets"].Should().HaveCount(2);
			data["Transactions"].Should().HaveCount(3); data["Operations"].Should().HaveCount(1); data["WitnessedOperations"].Should().BeEmpty();
			data["Vendors"].Should().ContainSingle(); data["Vendors"][0]["Id"].Value<string>().Should().Be(vendor.Id);
			data["PurchaseOrders"].Should().ContainSingle(); data["PurchaseOrders"][0]["Id"].Value<string>().Should().Be(order.Id);
			data["PurchaseOrderItems"].Should().ContainSingle(); data["PurchaseOrderItems"][0]["Id"].Value<string>().Should().Be(line.Id);
		}

		[TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
		public async Task Inventory_witness_export_contains_only_participation_facts_and_declares_receipt_content_withheld(bool enforced, bool encrypted)
		{
			var store = EmptyInventory();
			var content = encrypted ? "rgdp:1:19:SYNTHETIC-CIPHERTEXT-CANARY" : "{\"PendingCommand\":{\"Note\":\"PERFORMER-CANARY\"},\"Attestation\":\"ATTESTATION-CANARY\"}";
			var witness = InventoryExportRow<InventoryOperation>("PERFORMER-IDENTITY-CANARY", content: content);
			witness.WitnessUserId = UserId; witness.State = 2; witness.RequestId = Guid.NewGuid().ToString("D"); witness.ModifiedOn = DateTime.UtcNow;
			var unrelated = InventoryExportRow<InventoryOperation>("another-user", content: "UNRELATED-CANARY"); unrelated.WitnessUserId = "another-witness";
			InventoryExportPages(store, witness, unrelated); UseInventoryExport(store, enforced);
			var files = await RunExportAsync(); var json = files["inventory.json"]; var data = JObject.Parse(json);
			json.Should().NotContain("CANARY").And.NotContain("rgdp:"); data["Operations"].Should().BeEmpty(); data["WitnessedOperations"].Should().HaveCount(1);
			var exported = (JObject)data["WitnessedOperations"][0];
			exported.Properties().Select(p => p.Name).Should().BeEquivalentTo(new[] { "Id", "DepartmentId", "RequestId", "State", "WitnessUserId", "ModifiedOn", "Content" });
			exported["Id"].Value<string>().Should().Be(witness.Id); exported["WitnessUserId"].Value<string>().Should().Be(UserId);
			exported["Content"].Value<string>().Should().Be(ProtectedDataEnvelope.RedactionValue);
			JObject.Parse(files["withheld.json"])["entries"]["inventory.json"]["fields"].Values<string>().Should().Contain("WitnessedOperations[].Content");
			witness.Content.Should().Be(content, "background masking must not mutate a persisted receipt");
		}

		[TestCase(false, false, false), TestCase(false, true, false), TestCase(true, false, false), TestCase(false, false, true)]
		public async Task Inventory_export_masks_envelopes_enrollment_plaintext_and_policy_failures_without_mutating_rows(bool enforced, bool encrypted, bool policyFailure)
		{
			var store = EmptyInventory(); var content = encrypted ? "rgdp:1:19:SYNTHETIC-CIPHERTEXT-CANARY" : "SYNTHETIC-PLAIN-CANARY";
			var location = InventoryExportRow<InventoryLocation>(content: content); var issuance = InventoryExportRow<InventoryIssuance>(content: content);
			var asset = InventoryExportRow<InventoryAsset>(content: content); var transaction = InventoryExportRow<InventoryTransaction>(content: content);
			var operation = InventoryExportRow<InventoryOperation>(content: content);
			var vendorContent = encrypted ? "rgdp:1:21:SYNTHETIC-VENDOR-CIPHERTEXT-CANARY" : "{\"AccountNumber\":\"VENDOR-ACCOUNT-CANARY\",\"Note\":\"VENDOR-NOTE-CANARY\"}";
			var orderContent = encrypted ? "rgdp:1:21:SYNTHETIC-ORDER-CIPHERTEXT-CANARY" : "{\"Number\":\"ORDER-NUMBER-CANARY\",\"SupplierName\":\"SUPPLIER-CANARY\",\"Note\":\"ORDER-NOTE-CANARY\"}";
			var lineContent = encrypted ? "rgdp:1:21:SYNTHETIC-LINE-CIPHERTEXT-CANARY" : "{\"UnitCost\":12345.678901,\"Note\":\"LINE-NOTE-CANARY\"}";
			var vendor = InventoryExportRow<InventoryVendor>(content: vendorContent); vendor.ContactId = Guid.NewGuid().ToString("D");
			var order = InventoryExportRow<InventoryPurchaseOrder>(content: orderContent); order.VendorId = vendor.Id;
			var line = InventoryExportRow<InventoryPurchaseOrderItem>(content: lineContent); line.PurchaseOrderId = order.Id;
			var purchasing = new InventoryRow[] { vendor, order, line };
			var originals = purchasing.ToDictionary(row => row.Id, row => JObject.FromObject(row).ToString());
			InventoryExportPages(store, location); InventoryExportPages(store, issuance); InventoryExportPages(store, asset);
			InventoryExportPages(store, transaction); InventoryExportPages(store, operation);
			InventoryExportPages(store, vendor); InventoryExportPages(store, order); InventoryExportPages(store, line); UseInventoryExport(store, enforced, policyFailure);
			var files = await RunExportAsync(); var json = files["inventory.json"]; var data = JObject.Parse(json);
			var masked = enforced || encrypted || policyFailure;
			foreach (var property in new[] { "Locations", "Issuances", "Assets", "Transactions", "Operations" })
				data[property][0]["Content"].Value<string>().Should().Be(masked ? ProtectedDataEnvelope.RedactionValue : content);
			foreach (var entry in new[] { (Property: "Vendors", Content: vendorContent), (Property: "PurchaseOrders", Content: orderContent), (Property: "PurchaseOrderItems", Content: lineContent) })
				data[entry.Property][0]["Content"].Value<string>().Should().Be(masked ? ProtectedDataEnvelope.RedactionValue : entry.Content);
			if (masked)
			{
				json.Should().NotContain("CANARY").And.NotContain("rgdp:").And.NotContain("12345.678901");
				var withheld = JObject.Parse(files["withheld.json"])["entries"]["inventory.json"];
				withheld["valuesWithheld"].Value<int>().Should().Be(8);
				withheld["fields"].Values<string>().Should().Contain(new[] { "Vendors[].Content", "PurchaseOrders[].Content", "PurchaseOrderItems[].Content" });
			}
			else files.Should().NotContainKey("withheld.json");
			new InventoryRow[] { location, issuance, asset, transaction, operation }.Should().OnlyContain(x => x.Content == content);
			foreach (var row in purchasing) JObject.FromObject(row).ToString().Should().Be(originals[row.Id], "export masking must not rewrite stored supplier details or purchase costs");
		}

		[Test]
		public async Task Inventory_export_pages_every_row_family_and_includes_witnesses_after_unrelated_pages_without_duplicates()
		{
			var store = EmptyInventory();
			T[] Rows<T>() where T : InventoryRow, new() => Enumerable.Range(0, 1002)
				.Select(i => InventoryExportRow<T>(i >= 999 ? UserId : "another-user")).ToArray();
			var locations = Rows<InventoryLocation>(); var issuances = Rows<InventoryIssuance>(); var assets = Rows<InventoryAsset>();
			var transactions = Rows<InventoryTransaction>(); var operations = Rows<InventoryOperation>();
			var vendors = Rows<InventoryVendor>(); var orders = Rows<InventoryPurchaseOrder>(); var lines = Rows<InventoryPurchaseOrderItem>();
			operations[1001].CreatedBy = "another-user"; operations[1001].WitnessUserId = UserId;
			InventoryExportPages(store, locations); InventoryExportPages(store, issuances); InventoryExportPages(store, assets);
			InventoryExportPages(store, transactions); InventoryExportPages(store, operations);
			InventoryExportPages(store, vendors); InventoryExportPages(store, orders); InventoryExportPages(store, lines); UseInventoryExport(store);
			var data = JObject.Parse((await RunExportAsync())["inventory.json"]);
			foreach (var property in new[] { "Locations", "Issuances", "Assets", "Transactions", "Vendors", "PurchaseOrders", "PurchaseOrderItems" })
			{
				data[property].Should().HaveCount(3); data[property].Select(x => x["Id"].Value<string>()).Should().OnlyHaveUniqueItems();
			}
			data["Operations"].Should().HaveCount(2); data["WitnessedOperations"].Should().HaveCount(1);
			data["WitnessedOperations"][0]["Id"].Value<string>().Should().Be(operations[1001].Id);
			void VerifyPages<T>() where T : InventoryRow
			{
				foreach (var skip in new[] { 0, 500, 1000 }) Mock.Get(store).Verify(s => s.ListAsync<T>(DeptId, skip), Times.Once);
			}
			VerifyPages<InventoryLocation>(); VerifyPages<InventoryIssuance>(); VerifyPages<InventoryAsset>(); VerifyPages<InventoryTransaction>(); VerifyPages<InventoryOperation>();
			VerifyPages<InventoryVendor>(); VerifyPages<InventoryPurchaseOrder>(); VerifyPages<InventoryPurchaseOrderItem>();
		}

		[Test]
		public async Task Inventory_export_fails_instead_of_publishing_a_partial_archive_when_the_page_limit_is_exceeded()
		{
			var store = EmptyInventory(); var unrelated = InventoryExportRow<InventoryLocation>("another-user");
			Mock.Get(store).Setup(s => s.ListAsync<InventoryLocation>(DeptId, It.IsAny<int>()))
				.ReturnsAsync(Enumerable.Repeat(unrelated, 501).ToList());
			UseInventoryExport(store); await _service.ProcessPendingRequestsAsync(System.Threading.CancellationToken.None);
			_request.Status.Should().Be((int)GdprExportStatus.Failed); _request.ExportData.Should().BeNull(); _request.DownloadToken.Should().BeNull();
			_request.ErrorMessage.Should().Be("Inventory export exceeds the supported department size.");
			Mock.Get(store).Verify(s => s.ListAsync<InventoryLocation>(DeptId, 100000), Times.Once);
			Mock.Get(store).Verify(s => s.ListAsync<InventoryLocation>(DeptId, 100500), Times.Never);
		}
	}
}
