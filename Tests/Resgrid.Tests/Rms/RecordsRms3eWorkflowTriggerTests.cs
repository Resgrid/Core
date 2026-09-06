using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Services;
using Scriban.Runtime;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Workflow integration for the RMS-3e triggers (2026-09-05 pass): the block-1 gaps (103 approved, 115
	/// attachment added) and block 2 (152-160: disclosure, legal hold, evidence, purge, scheduled export). Every
	/// trigger has a variable catalog, every catalog variable renders from the sample data, and the context
	/// builder maps each block from the dispatched outbox payload, never from current record state.
	/// </summary>
	[TestFixture]
	public class RecordsRms3eWorkflowTriggerTests
	{
		private static readonly WorkflowTriggerEventType[] Triggers =
		{
			WorkflowTriggerEventType.RecordApproved,
			WorkflowTriggerEventType.RecordAttachmentAdded,
			WorkflowTriggerEventType.RecordDisclosureRequested,
			WorkflowTriggerEventType.RecordDisclosureProduced,
			WorkflowTriggerEventType.RecordDisclosureReleased,
			WorkflowTriggerEventType.RecordDisclosureClosed,
			WorkflowTriggerEventType.RecordLegalHoldPlaced,
			WorkflowTriggerEventType.RecordLegalHoldReleased,
			WorkflowTriggerEventType.RecordEvidenceCaptured,
			WorkflowTriggerEventType.RecordPurged,
			WorkflowTriggerEventType.RecordExportScheduled
		};

		[Test]
		public void Every_rms3e_trigger_is_a_records_trigger_with_a_catalog_and_a_protection_block()
		{
			foreach (var trigger in Triggers)
			{
				WorkflowTriggerEventTypes.IsRecordsTrigger(trigger).Should().BeTrue($"{trigger} lives in an RMS block");
				var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger);
				catalog.Should().Contain(v => v.Name == "event.name", $"{trigger} carries the event namespace");
				catalog.Should().Contain(v => v.Name == "protection.is_protected", $"{trigger} carries the ADP posture");
				catalog.Should().NotContain(v => v.Name.Contains("narrative") || v.Name.Contains("requester") || v.Name.Contains("notes") || v.Name.Contains("file_name") && trigger == WorkflowTriggerEventType.RecordAttachmentAdded,
					$"{trigger} must never expose record content");
			}
		}

		[Test]
		public void Every_catalog_variable_has_a_sample_value()
		{
			foreach (var trigger in Triggers)
			{
				var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
				foreach (var descriptor in WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger))
					Has(data, descriptor.Name).Should().BeTrue($"{trigger} sample data must carry {descriptor.Name}");
			}
		}

		[Test]
		public void Block_specific_namespaces_ride_only_their_own_triggers()
		{
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordAttachmentAdded).Should().Contain(v => v.Name == "attachment.checksum");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordFinalized).Should().NotContain(v => v.Name.StartsWith("attachment."));
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordDisclosureReleased).Should().Contain(v => v.Name == "disclosure.delivery_method");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordLegalHoldPlaced).Should().Contain(v => v.Name == "legal_hold.reason").And.NotContain(v => v.Name == "legal_hold.reference_number");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordEvidenceCaptured).Should().Contain(v => v.Name == "evidence.checksum").And.NotContain(v => v.Name.Contains("manifest"));
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordPurged).Should().Contain(v => v.Name == "purge.attachments_purged");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordExportScheduled).Should().Contain(v => v.Name == "export.run_id");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.RecordApproved).Should().Contain(v => v.Name == "review.approver_user_id");
		}

		[Test]
		public void Workflow_step_export_hooks_read_the_designer_config_and_the_dispatched_subject()
		{
			WorkflowService.ReadExportTemplateId("{\"to\":\"a@b\",\"recordsExportTemplateId\":\"tpl-1\"}").Should().Be("tpl-1");
			WorkflowService.ReadExportTemplateId("{\"RecordsExportTemplateId\":\" tpl-2 \"}").Should().Be("tpl-2");
			WorkflowService.ReadExportTemplateId("{\"to\":\"a@b\"}").Should().BeNull();
			WorkflowService.ReadExportTemplateId("not json").Should().BeNull();
			WorkflowService.ReadExportTemplateId(null).Should().BeNull();

			var dispatched = new DomainEventDispatchedEvent
			{
				DepartmentId = 1, EventId = "evt", EventName = "RecordExportScheduled", TriggerEventType = (int)WorkflowTriggerEventType.RecordExportScheduled,
				PayloadJson = JsonConvert.SerializeObject(new { record = new { id = (string)null, kind = "Export" }, export = new { run_id = "run-9" } })
			};
			var subject = WorkflowService.ReadExportSubject(JsonConvert.SerializeObject(RecordsWorkflowEvent.From(dispatched)));
			subject.recordId.Should().BeNull();
			subject.recordKind.Should().BeNull();
			subject.scheduledRunId.Should().Be("run-9");

			var finalized = new DomainEventDispatchedEvent
			{
				DepartmentId = 1, EventId = "evt2", EventName = "RecordFinalized", TriggerEventType = (int)WorkflowTriggerEventType.RecordFinalized,
				PayloadJson = JsonConvert.SerializeObject(new { record = new { id = "rec-1", kind = "IncidentReport" } })
			};
			var single = WorkflowService.ReadExportSubject(JsonConvert.SerializeObject(RecordsWorkflowEvent.From(finalized)));
			single.recordId.Should().Be("rec-1");
			single.recordKind.Should().Be(RmsRecordKind.IncidentReport);
			WorkflowService.ReadExportSubject("garbage").Should().Be(((string)null, (RmsRecordKind?)null, (string)null));
		}

		private static bool Has(ScriptObject root, string path)
		{
			object current = root;
			foreach (var segment in path.Split('.'))
			{
				if (!(current is ScriptObject obj) || !obj.ContainsKey(segment))
					return false;
				current = obj[segment];
			}
			return true;
		}
	}

	/// <summary>The template context for a dispatched RMS-3e event, built with the shared context-builder fixture.</summary>
	[TestFixture]
	public class RecordsRms3eContextBuilderTests : Resgrid.Tests.Services.WorkflowTemplateContextBuilderTests.with_the_context_builder
	{
		[Test]
		public async Task Maps_the_block_namespaces_and_defaults_protection_for_older_payloads()
		{
			var dispatched = new DomainEventDispatchedEvent
			{
				DepartmentId = 1, EventId = "evt-3", EventName = "RecordLegalHoldPlaced", SchemaVersion = 1, AggregateType = DomainEventProducers.RecordsAggregate, AggregateId = "rec-1",
				TriggerEventType = (int)WorkflowTriggerEventType.RecordLegalHoldPlaced, CorrelationId = "hold-1", OriginClient = (int)RmsOriginClient.Web, OccurredOn = DateTime.UtcNow,
				PayloadJson = JsonConvert.SerializeObject(new
				{
					record = new { id = "rec-1", kind = "Operational", record_number = "RUN-2026-0007", author_user_id = "author-1" },
					legal_hold = new { id = "hold-1", record_id = "rec-1", reason = "Litigation", placed_by_user_id = "counsel", is_released = false },
					protection = new { is_protected = true, is_redacted = false, protected_catalog_version = 10 }
				})
			};

			var ctx = await BuildContext(WorkflowTriggerEventType.RecordLegalHoldPlaced, RecordsWorkflowEvent.From(dispatched));

			var hold = (ScriptObject)ctx["legal_hold"];
			hold["reason"].Should().Be("Litigation");
			hold["is_released"].Should().Be(false);
			((ScriptObject)ctx["record"])["record_number"].Should().Be("RUN-2026-0007");
			var protection = (ScriptObject)ctx["protection"];
			protection["is_protected"].Should().Be(true);
			protection["protected_catalog_version"].Should().Be(10L);

			var legacy = new DomainEventDispatchedEvent
			{
				DepartmentId = 1, EventId = "evt-4", EventName = "RecordFinalized", TriggerEventType = (int)WorkflowTriggerEventType.RecordFinalized,
				PayloadJson = JsonConvert.SerializeObject(new { record = new { id = "rec-2", author_user_id = "author-1" }, record_change = new { previous_state = "Draft", current_state = "Finalized" } })
			};
			var older = await BuildContext(WorkflowTriggerEventType.RecordFinalized, RecordsWorkflowEvent.From(legacy));
			var defaulted = (ScriptObject)older["protection"];
			defaulted["is_protected"].Should().Be(false, "an outbox row written before catalog v10 still renders the unprotected shape");
			older.ContainsKey("legal_hold").Should().BeFalse("block namespaces appear only when the payload carries them");
		}

		[Test]
		public async Task Export_scheduled_maps_the_export_block_for_the_carrying_step()
		{
			var dispatched = new DomainEventDispatchedEvent
			{
				DepartmentId = 1, EventId = "evt-5", EventName = "RecordExportScheduled", TriggerEventType = (int)WorkflowTriggerEventType.RecordExportScheduled, AggregateType = "RmsExportTemplate", AggregateId = "tpl-1",
				PayloadJson = JsonConvert.SerializeObject(new
				{
					record = new { id = (string)null, kind = "Export", department_id = 1 },
					export = new { run_id = "run-1", template_key = "state-runs", format = "Csv", record_count = 12, file_name = "state-runs.csv", redacted = false }
				})
			};

			var ctx = await BuildContext(WorkflowTriggerEventType.RecordExportScheduled, RecordsWorkflowEvent.From(dispatched));

			var export = (ScriptObject)ctx["export"];
			export["run_id"].Should().Be("run-1");
			export["record_count"].Should().Be(12L);
			((ScriptObject)ctx["record"])["kind"].Should().Be("Export");
		}
	}
}
