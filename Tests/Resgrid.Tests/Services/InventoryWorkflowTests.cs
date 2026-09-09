using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Inventories;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;
using Resgrid.Services.Records;
using Resgrid.Tests.Rms;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public sealed class InventoryWorkflowTests
	{
		private const string Canary = "SYNTHETIC-INVENTORY-PHI-CANARY";
		private const string TransactionId = "11111111-1111-1111-1111-111111111111";
		private const string ItemId = "22222222-2222-2222-2222-222222222222";
		private const string AssetId = "33333333-3333-3333-3333-333333333333";
		private const string SourceId = "44444444-4444-4444-4444-444444444444";
		private const string DestinationId = "55555555-5555-5555-5555-555555555555";
		private const string CorrelationId = "66666666-6666-6666-6666-666666666666";
		private const string PurchaseOrderId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
		private const string VendorId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
		private const string ReceiptId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
		private FakeRmsStore _store;
		private EventAggregator _bus;
		private Mock<IDepartmentDataProtectionService> _policy;
		private ProtectedProjectionService _projection;
		private DomainEventOutboxService _outbox;
		private ReadinessHistoryTestProtection _history;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore(); _bus = new EventAggregator(); _policy = new Mock<IDepartmentDataProtectionService>();
			_projection = new ProtectedProjectionService(_policy.Object, new ProtectedFieldCatalog()); _history = new ReadinessHistoryTestProtection(_policy);
			_outbox = new DomainEventOutboxService(_store.OutboxRepo.Object, _bus, new Lazy<IProtectedProjectionService>(() => _projection), _history.Lazy);
			_store.OutboxRepo.Setup(s => s.InitializeChecklistPayloadAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_store.OutboxRepo.Setup(s => s.ReplaceChecklistPayloadAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DomainEventOutboxEntry e, string p, CancellationToken c) => { e.PayloadJson = p; return true; });
		}
		[TearDown] public void TearDown() => _history.Dispose();

		[TestCase(22), TestCase(58), TestCase(59), TestCase(60), TestCase(64), TestCase(66)]
		public async Task All_inventory_events_render_safe_quantities_ids_and_metadata_after_wrapped_projection(int trigger)
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var wrapped = Wrapped(trigger); var projected = await ChecklistWorkflowPayload.ProjectAsync(42, wrapped, _projection, true);
			projected.Should().NotContain(Canary).And.NotContain("rgdp:").And.NotContain("\"UserId\":").And.NotContain("NestedSecret");
			var json = JObject.Parse(projected); json.Value<string>("AggregateId").Should().Be(AssetId); json.Value<string>("CorrelationId").Should().Be(CorrelationId);
			var context = (ScriptObject)await Builder().BuildContextAsync(42, (WorkflowTriggerEventType)trigger, projected, CancellationToken.None);
			var template = Template.Parse("{{ inventory.transaction_id }}|{{ inventory.item_id }}|{{ inventory.quantity + 1 }}|{{ inventory.from_quantity_after }}|{{ inventory.item_name }}|{{ event.name }}|{{ event.sequence }}|{{ event.is_replay }}|{{ protection.is_redacted }}");
			template.HasErrors.Should().BeFalse();
			template.Render(context).Should().Be(TransactionId + "|" + ItemId + "|3.125001|5.874999|REDACTED|" + (WorkflowTriggerEventType)trigger + "|7|true|true");
			var inventory = (ScriptObject)context["inventory"]; inventory["quantity"].Should().BeOfType<decimal>();
			inventory["from_location_id"].Should().Be(SourceId); inventory["to_location_id"].Should().Be(DestinationId);
			((ScriptObject)context["event"])["id"].Should().Be(wrapped.Value<string>("EventId"));
			Convert.ToInt32(((ScriptObject)context["protection"])["catalog_version"]).Should().BeGreaterThanOrEqualTo(19);
			var sample = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData((WorkflowTriggerEventType)trigger);
			foreach (var descriptor in WorkflowTemplateVariableCatalog.GetVariableCatalog((WorkflowTriggerEventType)trigger)
				.Where(d => d.Name.StartsWith("inventory.") || d.Name.StartsWith("event.") || d.Name.StartsWith("protection.")))
			{
				var path = descriptor.Name.Split('.'); ((ScriptObject)context[path[0]]).ContainsKey(path[1]).Should().BeTrue("runtime advertises " + descriptor.Name);
				((ScriptObject)sample[path[0]]).ContainsKey(path[1]).Should().BeTrue("preview advertises " + descriptor.Name);
			}
			Template.Parse("{{ inventory.quantity + 1 }}|{{ event.name }}|{{ protection.is_redacted }}").Render(sample).Should().EndWith("|" + (WorkflowTriggerEventType)trigger + "|true");
		}

		[Test]
		public async Task Record_usage_routing_survives_projection_and_has_a_renderable_preview()
		{
			const string usageId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
			var wrapped = Wrapped(22); wrapped["Payload"]["UsageId"] = usageId;
			wrapped["Payload"]["UsageType"] = (int)InventoryUsageType.Used;
			var safe = await InventoryWorkflowPayload.ProjectAsync(42, wrapped, _projection, true);
			var context = await Builder().BuildContextAsync(42, WorkflowTriggerEventType.InventoryAdjusted, safe, CancellationToken.None);
			var template = Template.Parse("{{ inventory.usage_id }}|{{ inventory.usage_type }}");
			template.Render(context).Should().Be(usageId + "|0");
			template.Render(WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.InventoryAdjusted)).Should().Be(usageId + "|0");
		}

		[Test]
		public async Task Purchase_receipt_summary_projects_routing_and_preview_without_costs_or_contact_details()
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var projected = await ChecklistWorkflowPayload.ProjectAsync(42, Wrapped(65), _projection, true);
			projected.Should().NotContain(Canary).And.NotContain("12345.678901").And.NotContain("98765.432109").And.NotContain("rgdp:");
			var envelope = JObject.Parse(projected); envelope.Value<string>("AggregateId").Should().Be(PurchaseOrderId);
			envelope.Value<string>("AggregateType").Should().Be("InventoryPurchaseOrder"); envelope.Value<string>("CorrelationId").Should().Be(CorrelationId);
			var payload = (JObject)envelope["Payload"];
			foreach (var property in new[] { "UnitCost", "TotalCost", "ContactId", "CompanyContactId", "SupplierName", "AccountNumber", "Email", "PurchaseOrderNumber" })
				payload.Property(property).Should().BeNull(property + " is outside the Workflow routing contract");
			payload.Property("Quantity").Should().BeNull("a purchase receipt summary does not add quantities with different units");
			var context = (ScriptObject)await Builder().BuildContextAsync(42, WorkflowTriggerEventType.InventoryPurchaseOrderReceived, projected, CancellationToken.None);
			var sample = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.InventoryPurchaseOrderReceived);
			var template = Template.Parse("{{ inventory.purchase_order_id }}|{{ inventory.vendor_id }}|{{ inventory.receipt_id }}|{{ inventory.purchase_order_status }}|{{ inventory.line_count + 1 }}|{{ inventory.currency_code }}|{{ event.name }}|{{ protection.is_redacted }}");
			template.HasErrors.Should().BeFalse();
			var expected = PurchaseOrderId + "|" + VendorId + "|" + ReceiptId + "|2|3|USD|InventoryPurchaseOrderReceived|true";
			template.Render(context).Should().Be(expected); template.Render(sample).Should().Be(expected);
			foreach (var root in new[] { context, sample })
			{
				var inventory = (ScriptObject)root["inventory"]; inventory["quantity"].Should().BeNull(); inventory["transaction_id"].Should().BeNull();
				foreach (var property in new[] { "unit_cost", "total_cost", "contact_id", "company_contact_id", "supplier_name", "account_number", "email", "purchase_order_number" })
					inventory.ContainsKey(property).Should().BeFalse();
				foreach (var descriptor in WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.InventoryPurchaseOrderReceived)
					.Where(d => d.Name.StartsWith("inventory.") || d.Name.StartsWith("event.") || d.Name.StartsWith("protection.")))
				{
					var path = descriptor.Name.Split('.'); ((ScriptObject)root[path[0]]).ContainsKey(path[1]).Should().BeTrue("runtime and preview advertise " + descriptor.Name);
				}
			}
			((ScriptObject)context["event"])["sequence"].Should().Be(7L);
			((ScriptObject)context["event"])["is_replay"].Should().Be(true);
		}

		[Test]
		public async Task Large_stock_balances_preserve_all_six_fractional_digits_through_projection_and_template_arithmetic()
		{
			var wrapped = Wrapped(22); const decimal before = 1234567890123456.123456m; const decimal after = 1234567890123454.123456m;
			wrapped["Payload"]["FromQuantityBefore"] = before; wrapped["Payload"]["FromQuantityAfter"] = after;
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var projected = await InventoryWorkflowPayload.ProjectAsync(42, wrapped, _projection, true);
			var context = (ScriptObject)await Builder().BuildContextAsync(42, WorkflowTriggerEventType.InventoryAdjusted, projected, CancellationToken.None);
			var inventory = (ScriptObject)context["inventory"]; inventory["from_quantity_before"].Should().Be(before); inventory["from_quantity_after"].Should().Be(after);
			Template.Parse("{{ (inventory.from_quantity_before - inventory.from_quantity_after) == 2 }}").Render(context).Should().Be("true");
		}

		[Test]
		public async Task Invalid_scalar_types_and_policy_reintroduced_authored_fields_cannot_escape_allowlist()
		{
			var payload = Payload(); payload["Quantity"] = Canary; payload["ItemId"] = Canary; payload["OldStatus"] = Canary;
			payload["ReferenceId"] = "person-" + Canary; payload["OccurredOn"] = Canary; payload["UsageId"] = Canary; payload["UsageType"] = 4;
			foreach (var property in new[] { "PurchaseOrderId", "PurchaseOrderItemId", "VendorId", "ReceiptId" }) payload[property] = Canary;
			payload["PurchaseOrderStatus"] = 5; payload["LineCount"] = 101; payload["CurrencyCode"] = "U$D";
			var protection = new Mock<IProtectedProjectionService>();
			protection.Setup(p => p.BuildSafeWorkflowPayloadAsync(42, It.IsAny<object>())).ReturnsAsync((int d, object value) =>
			{
				var returned = JObject.FromObject(value); returned["Note"] = Canary; returned["SerialNumber"] = Canary; returned["WitnessUserId"] = Canary;
				returned["UnitCost"] = 12345.678901m; returned["TotalCost"] = 98765.432109m; returned["ContactId"] = SourceId; returned["SupplierName"] = Canary;
				returned["UserId"] = Canary; returned["NestedSecret"] = new JObject { ["Value"] = Canary }; return returned.ToString();
			});
			var safe = JObject.Parse(await InventoryWorkflowPayload.ProjectAsync(42, payload, protection.Object));
			safe.ToString().Should().NotContain(Canary).And.NotContain("NestedSecret");
			foreach (var name in new[] { "Quantity", "ItemId", "OldStatus", "ReferenceId", "OccurredOn", "UserId", "UsageId", "UsageType" }) safe.Property(name).Should().BeNull(name + " is not a valid routing value");
			foreach (var name in new[] { "PurchaseOrderId", "PurchaseOrderItemId", "VendorId", "ReceiptId", "PurchaseOrderStatus", "LineCount", "CurrencyCode", "UnitCost", "TotalCost", "ContactId", "SupplierName" })
				safe.Property(name).Should().BeNull(name + " must not escape the allowlist");
			safe.Value<string>("Note").Should().Be("REDACTED"); safe.Value<string>("SerialNumber").Should().Be("REDACTED");
			safe.Value<string>("WitnessUserId").Should().Be("REDACTED");
		}

		[TestCase("PurchaseOrderId", "123"), TestCase("PurchaseOrderItemId", "{}"), TestCase("VendorId", "[]"), TestCase("ReceiptId", "true")]
		[TestCase("PurchaseOrderStatus", "\"2\""), TestCase("PurchaseOrderStatus", "2.5"), TestCase("PurchaseOrderStatus", "-1")]
		[TestCase("LineCount", "\"2\""), TestCase("LineCount", "1.5"), TestCase("LineCount", "-1")]
		[TestCase("CurrencyCode", "123"), TestCase("CurrencyCode", "\"usd\""), TestCase("CurrencyCode", "\"USDD\"")]
		public async Task Purchase_routing_rejects_invalid_scalar_types_and_formats(string property, string invalidJson)
		{
			var payload = PurchasePayload(); payload[property] = JToken.Parse(invalidJson);
			var safe = JObject.Parse(await InventoryWorkflowPayload.ProjectAsync(42, payload, _projection));
			safe.Property(property).Should().BeNull(property + " cannot be coerced into a routing value");
			safe.ToString().Should().NotContain(Canary);
		}

		[TestCase(22), TestCase(65)]
		public async Task Reprojection_after_enrollment_or_deactivation_never_restores_authored_content(int trigger)
		{
			var safe = await InventoryWorkflowPayload.ProjectAsync(42, Wrapped(trigger), _projection, true);
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			safe = await InventoryWorkflowPayload.ProjectAsync(42, JObject.Parse(safe), _projection, true);
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(false);
			safe = await InventoryWorkflowPayload.ProjectAsync(42, JObject.Parse(safe), _projection, true);
			safe.Should().NotContain(Canary); var payload = (JObject)JObject.Parse(safe)["Payload"];
			payload.Value<string>("ItemName").Should().Be("REDACTED");
			var context = await Builder().BuildContextAsync(42, (WorkflowTriggerEventType)trigger, safe, CancellationToken.None);
			if (trigger == 65)
			{
				payload.Property("UnitCost").Should().BeNull(); payload.Property("ContactId").Should().BeNull();
				Template.Parse("{{ inventory.purchase_order_id }}|{{ inventory.receipt_id }}|{{ inventory.purchase_order_status }}|{{ inventory.line_count }}|{{ inventory.currency_code }}").Render(context)
					.Should().Be(PurchaseOrderId + "|" + ReceiptId + "|2|2|USD");
			}
			else
			{
				payload.Value<decimal>("Quantity").Should().Be(2.125001m);
				Template.Parse("{{ inventory.id }}|{{ inventory.amount }}|{{ inventory.previous_amount }}|{{ inventory.type_name }}|{{ inventory.note }}").Render(context)
					.Should().Be(TransactionId + "|3.125001|1.0|REDACTED|REDACTED");
			}
		}

		[Test]
		public async Task Legacy_adjustment_still_renders_original_legacy_aliases()
		{
			var legacy = new InventoryAdjustedEvent { PreviousAmount = 12.5, Inventory = new Inventory { InventoryId = 15, Amount = 10.25,
				Batch = "Synthetic lot", Note = "Synthetic note", Location = "Synthetic store", Type = new InventoryType { Type = "Synthetic gloves", UnitOfMesasure = "pair" } } };
			var context = await Builder().BuildContextAsync(42, WorkflowTriggerEventType.InventoryAdjusted, JsonConvert.SerializeObject(legacy), CancellationToken.None);
			Template.Parse("{{ inventory.id }}|{{ inventory.amount }}|{{ inventory.previous_amount }}|{{ inventory.type_name }}|{{ inventory.unit_of_measure }}").Render(context)
				.Should().Be("15|10.25|12.5|Synthetic gloves|pair");
		}

		[TestCase(61), TestCase(62), TestCase(63), TestCase(166)]
		[TestCase(22), TestCase(58), TestCase(59), TestCase(60), TestCase(64), TestCase(65), TestCase(66)]
		public async Task Inventory_outbox_protects_history_and_replays_only_structural_payload_without_broker_decryption(int trigger)
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var entry = await _outbox.EnqueueAsync(42, "Inventory", Event(trigger));
			entry.PayloadJson.Should().StartWith("rgdp:").And.NotContain("2.125001");
			var routingId = trigger == 65 ? ReceiptId : TransactionId;
			entry.ReadinessRoutingJson.Should().NotContain(Canary).And.Contain(routingId);
			var history = _history.Decrypt(42, "domaineventoutbox.payloadjson", entry.DomainEventOutboxId.ToString(), entry.PayloadJson);
			history.Should().NotContain(Canary).And.Contain(routingId).And.NotContain("12345.678901").And.NotContain("98765.432109");
			if (trigger != 65) history.Should().Contain("2.125001");
			DomainEventDispatchedEvent delivered = null;
			_bus.AddAsyncListener<DomainEventDispatchedEvent>(e => { delivered = e; return Task.CompletedTask; });
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1);
			delivered.ProducerSubsystem.Should().Be("Inventory"); delivered.AggregateId.Should().Be(trigger == 65 ? PurchaseOrderId : AssetId); delivered.CorrelationId.Should().Be(CorrelationId);
			delivered.PayloadJson.Should().NotContain(Canary).And.NotContain("rgdp:");
			var deliveredPayload = JObject.Parse(delivered.PayloadJson);
			if (trigger == 65)
			{
				deliveredPayload.Value<int>("PurchaseOrderStatus").Should().Be(2); deliveredPayload.Value<int>("LineCount").Should().Be(2);
				deliveredPayload.Value<string>("CurrencyCode").Should().Be("USD"); deliveredPayload.Property("Quantity").Should().BeNull();
			}
			else deliveredPayload.Value<decimal>("Quantity").Should().Be(2.125001m);
			var context = await Builder().BuildContextAsync(42, (WorkflowTriggerEventType)trigger, JsonConvert.SerializeObject(RecordsWorkflowEvent.From(delivered)), CancellationToken.None);
			Template.Parse("{{ event.id }}|{{ inventory." + (trigger == 65 ? "receipt_id" : "transaction_id") + " }}|{{ inventory.item_name }}").Render(context).Should().Be(entry.EventId + "|" + routingId + "|REDACTED");
			_history.Broker.Invocations.Should().OnlyContain(i => i.Method.Name == "EncryptAsync");
		}

		[TestCase(22), TestCase(65)]
		public async Task Unknown_protection_policy_prevents_inventory_outbox_initialization(int trigger)
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ThrowsAsync(new InvalidOperationException("policy unavailable"));
			await ((Func<Task>)(() => _outbox.EnqueueAsync(42, "Inventory", Event(trigger)))).Should().ThrowAsync<InvalidOperationException>();
			_store.OutboxRepo.Verify(s => s.InitializeChecklistPayloadAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(61), TestCase(62), TestCase(63), TestCase(166)]
		[TestCase(22), TestCase(58), TestCase(59), TestCase(60), TestCase(64), TestCase(65), TestCase(66)]
		public async Task Queue_rejection_retries_existing_inventory_workflow_run_with_same_event_identity(int trigger)
		{
			var workflows = new Mock<IWorkflowRepository>(); var runs = new Mock<IWorkflowRunRepository>(); var queue = new Mock<IOutboundQueueProvider>();
			var subscriptions = new Mock<ISubscriptionsService>(); subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(42, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = 999999 });
			var workflow = new Workflow { WorkflowId = Guid.NewGuid().ToString("D"), DepartmentId = 42, TriggerEventType = trigger };
			workflows.Setup(s => s.GetAllActiveByDepartmentAndEventTypeAsync(42, trigger)).ReturnsAsync(new[] { workflow });
			WorkflowRun stored = null;
			runs.Setup(s => s.GetByWorkflowsAndEventAsync(42, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>())).ReturnsAsync(() => stored == null ? new List<WorkflowRun>() : new List<WorkflowRun> { stored });
			runs.Setup(s => s.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((WorkflowRun run, CancellationToken c, bool f) => stored = run);
			var attempts = new List<WorkflowQueueItem>(); queue.Setup(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>())).ReturnsAsync((WorkflowQueueItem q) => { attempts.Add(q); return attempts.Count > 1; });
			_ = new WorkflowEventProvider(_bus, queue.Object, workflows.Object, runs.Object, Mock.Of<IDepartmentsService>(), subscriptions.Object, _projection, _history.Lazy);
			var entry = await _outbox.EnqueueAsync(42, "Inventory", Event(trigger));
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(0); stored.Should().NotBeNull(); entry.DispatchedOn.Should().BeNull();
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1);
			attempts.Should().HaveCount(2); attempts[1].WorkflowRunId.Should().Be(attempts[0].WorkflowRunId);
			var queued = JObject.Parse(attempts[1].EventPayloadJson); queued.Value<string>("EventId").Should().Be(entry.EventId); queued.Value<bool>("IsReplay").Should().BeTrue();
			queued.Value<string>("AggregateId").Should().Be(trigger == 65 ? PurchaseOrderId : AssetId); attempts[1].EventPayloadJson.Should().NotContain(Canary).And.NotContain("rgdp:");
			runs.Verify(s => s.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[TestCase(61), TestCase(62), TestCase(63), TestCase(166)]
		[TestCase(22), TestCase(58), TestCase(59), TestCase(60), TestCase(64), TestCase(65), TestCase(66)]
		public async Task Duplicate_consumer_claim_does_not_execute_completed_inventory_run(int trigger)
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var workflows = new Mock<IWorkflowRepository>(); var runs = new Mock<IWorkflowRunRepository>(); var context = new Mock<IWorkflowTemplateContextBuilder>();
			var workflow = new Workflow { WorkflowId = Guid.NewGuid().ToString("D"), DepartmentId = 42, TriggerEventType = trigger };
			var run = new WorkflowRun { WorkflowRunId = Guid.NewGuid().ToString("D"), WorkflowId = workflow.WorkflowId, DepartmentId = 42, TriggerEventType = trigger, Status = (int)WorkflowRunStatus.Completed };
			workflows.Setup(s => s.GetByIdAsync(workflow.WorkflowId)).ReturnsAsync(workflow); runs.Setup(s => s.GetByIdAsync(run.WorkflowRunId)).ReturnsAsync(run);
			string claimed = null;
			runs.Setup(s => s.TryStartChecklistRunAsync(run.WorkflowRunId, workflow.WorkflowId, 42, 1, It.IsAny<string>()))
				.ReturnsAsync((string r, string w, int d, int a, string p) => { claimed = p; return false; });
			var service = new WorkflowService(workflows.Object, Mock.Of<IWorkflowStepRepository>(), Mock.Of<IWorkflowCredentialRepository>(), runs.Object,
				Mock.Of<IWorkflowRunLogRepository>(), Mock.Of<IWorkflowDailyUsageRepository>(), Mock.Of<IEncryptionService>(), Mock.Of<IWorkflowActionExecutorFactory>(),
				context.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IRecordsExportService>(), new Lazy<IProtectedProjectionService>(() => _projection), _history.Lazy);
			(await service.ExecuteWorkflowAsync(workflow.WorkflowId, Wrapped(trigger).ToString(), 42, string.Empty, existingRunId: run.WorkflowRunId)).Should().BeSameAs(run);
			claimed.Should().StartWith("rgdp:"); _history.Decrypt(42, "workflowruns.inputpayload", run.WorkflowRunId, claimed).Should().NotContain(Canary).And.Contain(trigger == 65 ? ReceiptId : TransactionId);
			context.Verify(s => s.BuildContextAsync(It.IsAny<int>(), It.IsAny<WorkflowTriggerEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(61), TestCase(62), TestCase(63), TestCase(166)]
		public async Task Count_and_alert_summaries_have_safe_routing_and_template_previews(int trigger)
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var wrapped = Wrapped(trigger); wrapped["AggregateType"] = trigger == 63 ? "InventoryCount" : "InventoryAlert";
			wrapped["Payload"] = JObject.FromObject(new { InventoryEvent = true, CountId = PurchaseOrderId, CountItemId = ReceiptId, AlertId = VendorId, LocationId = SourceId,
				AlertType = trigger == 61 ? 0 : trigger == 62 ? 1 : 3, LineCount = 12, VarianceLineCount = 2, VarianceValue = 12345.678901m,
				ReorderPoint = 98765.432109m, DueOn = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc), ItemName = Canary });
			var safe = await ChecklistWorkflowPayload.ProjectAsync(42, wrapped, _projection, true);
			safe.Should().NotContain(Canary).And.NotContain("12345.678901").And.NotContain("98765.432109");
			var context = (ScriptObject)await Builder().BuildContextAsync(42, (WorkflowTriggerEventType)trigger, safe, CancellationToken.None);
			Template.Parse("{{ inventory.count_id }}|{{ inventory.count_item_id }}|{{ inventory.alert_id }}|{{ inventory.variance_line_count + 1 }}|{{ inventory.variance_value }}")
				.Render(context).Should().Be(PurchaseOrderId + "|" + ReceiptId + "|" + VendorId + "|3|REDACTED");
			var sample = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData((WorkflowTriggerEventType)trigger);
			var inventory = (ScriptObject)sample["inventory"];
			foreach (var pair in InventoryWorkflowPayload.Variables) inventory.ContainsKey(pair.Variable).Should().BeTrue();
			inventory[trigger == 63 ? "count_id" : "alert_id"].Should().NotBeNull();
			if (trigger == 63) Template.Parse("{{ inventory.variance_line_count + 1 }}|{{ inventory.variance_value }}").Render(sample).Should().Be("3|REDACTED");
		}

		private static WorkflowTemplateContextBuilder Builder() => new(Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUserProfileService>(),
			Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentProfileMediaService>());
		private static JObject Payload() => JObject.FromObject(new { InventoryEvent = true, TransactionId, ItemId, AssetId, FromLocationId = SourceId, ToLocationId = DestinationId,
			Quantity = 2.125001m, FromQuantityBefore = 8m, FromQuantityAfter = 5.874999m, ToQuantityBefore = 1m, ToQuantityAfter = 3.125001m,
			TransactionType = 3, OldStatus = 0, NewStatus = 1, ReferenceType = 9, ReferenceId = "123", OccurredOn = new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc),
			ItemName = Canary, Note = Canary, SerialNumber = Canary, WitnessUserId = Canary, UserId = Canary, NestedSecret = new { Value = Canary } });
		private static JObject PurchasePayload() => JObject.FromObject(new { InventoryEvent = true, PurchaseOrderId, VendorId, ReceiptId,
			PurchaseOrderStatus = (int)InventoryPurchaseOrderStatus.PartiallyReceived, LineCount = 2, CurrencyCode = "USD",
			OccurredOn = new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc), UnitCost = 12345.678901m, TotalCost = 98765.432109m,
			ContactId = SourceId, CompanyContactId = DestinationId, SupplierName = Canary, AccountNumber = Canary, Email = Canary,
			PurchaseOrderNumber = Canary, Note = Canary, UserId = Canary, NestedSecret = new { Value = Canary } });
		private static JObject Wrapped(int trigger) => new() { ["DepartmentId"] = 42, ["EventId"] = "77777777-7777-7777-7777-777777777777",
			["EventName"] = ((WorkflowTriggerEventType)trigger).ToString(), ["TriggerEventType"] = trigger, ["SchemaVersion"] = 1, ["AggregateType"] = trigger == 65 ? "InventoryPurchaseOrder" : "InventoryAsset", ["AggregateId"] = trigger == 65 ? PurchaseOrderId : AssetId,
			["CorrelationId"] = CorrelationId, ["Sequence"] = 7L, ["IsReplay"] = true, ["OriginClient"] = "Api", ["OccurredOn"] = new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc),
			["Payload"] = trigger == 65 ? PurchasePayload() : Payload(), ["UnreviewedEnvelopeText"] = Canary };
		private static DomainEventEnvelope Event(int trigger) => new() { EventName = ((WorkflowTriggerEventType)trigger).ToString(), SchemaVersion = 1,
			AggregateType = trigger == 65 ? "InventoryPurchaseOrder" : "InventoryAsset", AggregateId = trigger == 65 ? PurchaseOrderId : AssetId,
			Trigger = (WorkflowTriggerEventType)trigger, CorrelationId = CorrelationId, Payload = trigger == 65 ? PurchasePayload() : Payload() };
	}
}
