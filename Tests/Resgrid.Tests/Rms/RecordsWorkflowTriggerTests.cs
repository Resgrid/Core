using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Services;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Workflow integration for the RMS-1 native triggers 100-107 (plan section 5.6): the variable catalog, the
	/// sample payloads, the dispatched-event contract and the legacy LogAdded compatibility projection.
	/// </summary>
	[TestFixture]
	public class RecordsWorkflowTriggerTests
	{
		private static readonly WorkflowTriggerEventType[] Rms1Triggers =
		{
			WorkflowTriggerEventType.RecordCreated,
			WorkflowTriggerEventType.RecordSubmittedForReview,
			WorkflowTriggerEventType.RecordReturnedForCorrection,
			WorkflowTriggerEventType.RecordFinalized,
			WorkflowTriggerEventType.RecordAmended,
			WorkflowTriggerEventType.RecordVoided,
			WorkflowTriggerEventType.RecordCancelled
		};

		[Test]
		public void Catalog_covers_event_record_and_record_change_for_every_rms1_trigger()
		{
			foreach (var trigger in Rms1Triggers)
			{
				var names = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger).Select(v => v.Name).ToList();

				names.Should().OnlyHaveUniqueItems(trigger.ToString());
				names.Should().Contain(new[] { "event.id", "event.name", "event.origin_client", "record.id", "record.record_number", "record.definition_key", "record.url", "record_change.previous_state", "record_change.current_state", "record_change.reason_code" }, trigger.ToString());
				names.Should().Contain("department.name", "common namespaces stay available");
			}
		}

		[Test]
		public void Catalog_lists_number_disposition_only_for_cancelled()
		{
			foreach (var trigger in Rms1Triggers)
			{
				var has = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger).Any(v => v.Name == "record_change.number_disposition");
				has.Should().Be(trigger == WorkflowTriggerEventType.RecordCancelled, trigger.ToString());
			}
		}

		[Test]
		public void Catalog_never_exposes_narrative_or_restricted_fields_on_native_triggers()
		{
			var forbidden = new[] { "narrative", "body_location", "pronounced", "case_number", "initial_report" };
			foreach (var trigger in Rms1Triggers)
			{
				var names = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger).Select(v => v.Name.ToLowerInvariant());
				names.Should().NotContain(n => forbidden.Any(n.Contains), trigger.ToString());
			}
		}

		[Test]
		public void Sample_data_renders_the_documented_variables_for_every_rms1_trigger()
		{
			var template = Template.Parse("{{ event.name }}|{{ record.record_number }}|{{ record_change.previous_state }}>{{ record_change.current_state }}|{{ record.url }}");
			template.HasErrors.Should().BeFalse();

			foreach (var trigger in Rms1Triggers)
			{
				var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
				var rendered = template.Render(data);

				rendered.Should().StartWith(trigger.ToString() + "|", trigger.ToString());
				rendered.Should().Contain("/User/Records/Details/");
			}

			template.Render((ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.RecordFinalized)).Should().Contain("TRN-2026-0042").And.Contain("Draft>Finalized");
			template.Render((ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.RecordAmended)).Should().Contain("Finalized>Amended");
		}

		[Test]
		public void Every_catalog_variable_has_a_sample_value()
		{
			foreach (var trigger in Rms1Triggers)
			{
				var data = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
				foreach (var descriptor in WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger))
					Has(data, descriptor.Name).Should().BeTrue($"{trigger} sample data must carry {descriptor.Name}");
			}
		}

		[Test]
		public void Records_block_helper_identifies_100_to_115()
		{
			WorkflowTriggerEventTypes.IsRecordsTrigger(WorkflowTriggerEventType.RecordCreated).Should().BeTrue();
			WorkflowTriggerEventTypes.IsRecordsTrigger(WorkflowTriggerEventType.RecordCancelled).Should().BeTrue();
			WorkflowTriggerEventTypes.IsRecordsTrigger(WorkflowTriggerEventType.LogAdded).Should().BeFalse();
			WorkflowTriggerEventTypes.IsRecordsTrigger(WorkflowTriggerEventType.StationCoverageGapDetected).Should().BeFalse();
			Rms1Triggers.Should().OnlyContain(t => WorkflowTriggerEventTypes.IsRecordsTrigger(t));
		}

		[Test]
		public void RecordsWorkflowEvent_From_parses_the_payload_and_survives_bad_json()
		{
			var good = RecordsWorkflowEvent.From(new DomainEventDispatchedEvent
			{
				DepartmentId = 4,
				EventId = "evt-1",
				EventName = "RecordFinalized",
				TriggerEventType = (int)WorkflowTriggerEventType.RecordFinalized,
				OriginClient = (int)RmsOriginClient.Responder,
				PayloadJson = "{\"record\":{\"id\":\"rec-1\"},\"record_change\":{\"current_state\":\"Finalized\"}}"
			});
			good.DepartmentId.Should().Be(4);
			good.OriginClient.Should().Be("Responder");
			good.TriggerEventType.Should().Be(104);
			((string)good.Payload["record"]["id"]).Should().Be("rec-1");

			var bad = RecordsWorkflowEvent.From(new DomainEventDispatchedEvent { EventId = "evt-2", PayloadJson = "{not json", OriginClient = 999 });
			bad.Payload.Should().NotBeNull().And.BeEmpty();
			bad.OriginClient.Should().Be("System", "an unknown origin never leaks a raw number");

			RecordsWorkflowEvent.From(null).Should().BeNull();
		}

		[Test]
		public void LogAdded_compatibility_is_eligible_for_logs_parity_types_only()
		{
			LogAddedCompatibility.IsEligible(new RmsOperationalRecord { DefinitionKey = RmsDefinitionKeys.Training }).Should().BeTrue();
			LogAddedCompatibility.IsEligible(new RmsOperationalRecord { DefinitionKey = RmsDefinitionKeys.Coroner }).Should().BeTrue();
			LogAddedCompatibility.IsEligible(new RmsOperationalRecord { DefinitionKey = RmsDefinitionKeys.UnitActivity }).Should().BeFalse("Unit Activity never emitted LogAdded");
			LogAddedCompatibility.IsEligible(new RmsOperationalRecord { DefinitionKey = RmsDefinitionKeys.NerisIncidentReport }).Should().BeFalse();
			LogAddedCompatibility.IsEligible(new RmsOperationalRecord { DefinitionKey = "dept.security-patrol" }).Should().BeFalse("department definitions never use the legacy path");
			LogAddedCompatibility.IsEligible(new RmsOperationalRecord()).Should().BeFalse();
			LogAddedCompatibility.IsEligible(null).Should().BeFalse();
		}

		[Test]
		public void LogAdded_compatibility_payload_matches_the_legacy_contract_and_omits_restricted_fields()
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = "rec-1",
				DepartmentId = 4,
				DefinitionKey = RmsDefinitionKeys.Coroner,
				RecordType = (int)RmsOperationalRecordType.Coroner,
				AuthorUserId = "author-1",
				StationGroupId = 12,
				CallId = 9,
				ExternalId = "EXT-1",
				StartedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
				EndedOn = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
				FinalizedOn = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)
			};
			var details = new RmsOperationalRecordDetail
			{
				Narrative = "Scene narrative",
				Cause = "Natural",
				ContactName = "Jane Doe",
				OtherPersonnel = "Deputy Smith",
				BodyLocation = "restricted",
				PronouncedDeceasedBy = "restricted",
				CaseNumber = "restricted"
			};

			var payload = LogAddedCompatibility.Build(record, details, DateTime.UtcNow);
			var json = JsonConvert.SerializeObject(payload);
			var roundTrip = JsonConvert.DeserializeObject<LogAddedEvent>(json);

			roundTrip.DepartmentId.Should().Be(4);
			roundTrip.Log.LogId.Should().Be(0);
			roundTrip.Log.LogType.Should().Be((int)LogTypes.Coroner);
			roundTrip.Log.Type.Should().Be("Coroner");
			roundTrip.Log.Narrative.Should().Be("Scene narrative");
			roundTrip.Log.Cause.Should().Be("Natural");
			roundTrip.Log.OtherPersonnel.Should().Be("Deputy Smith");
			roundTrip.Log.CallId.Should().Be(9);
			roundTrip.Log.StationGroupId.Should().Be(12);
			roundTrip.Log.LoggedOn.Should().Be(record.FinalizedOn.Value);
			roundTrip.Log.LoggedByUserId.Should().Be("author-1");
			roundTrip.Log.BodyLocation.Should().BeNull();
			roundTrip.Log.PronouncedDeceasedBy.Should().BeNull();
			json.Should().NotContain("restricted");
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

	/// <summary>The template context for a dispatched Records event, built with the shared context-builder fixture.</summary>
	[TestFixture]
	public class RecordsWorkflowContextBuilderTests : Resgrid.Tests.Services.WorkflowTemplateContextBuilderTests.with_the_context_builder
	{
		[Test]
		public async Task Maps_envelope_record_and_record_change_from_the_dispatched_event()
		{
			var dispatched = new DomainEventDispatchedEvent
			{
				DepartmentId = 1,
				EventId = "evt-1",
				EventName = "RecordFinalized",
				SchemaVersion = 1,
				AggregateType = DomainEventProducers.RecordsAggregate,
				AggregateId = "rec-1",
				Sequence = 2,
				TriggerEventType = (int)WorkflowTriggerEventType.RecordFinalized,
				CorrelationId = "rec-1",
				OriginClient = (int)RmsOriginClient.Web,
				OccurredOn = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc),
				PayloadJson = JsonConvert.SerializeObject(new
				{
					record = new { id = "rec-1", record_number = "TRN-2026-0001", author_user_id = "author-1", state = "Finalized", call_id = 7 },
					record_change = new { previous_state = "Draft", current_state = "Finalized", reason_code = (string)null },
					extra = new { number_disposition = "none" }
				})
			};

			var ctx = await BuildContext(WorkflowTriggerEventType.RecordFinalized, RecordsWorkflowEvent.From(dispatched));

			var evt = (ScriptObject)ctx["event"];
			evt["name"].Should().Be("RecordFinalized");
			evt["origin_client"].Should().Be("Web");
			evt["sequence"].Should().Be(2L);
			evt["is_replay"].Should().Be(false);

			var record = (ScriptObject)ctx["record"];
			record["record_number"].Should().Be("TRN-2026-0001");
			record["call_id"].Should().Be(7L);
			((string)record["url"]).Should().EndWith("/User/Records/Details/rec-1");

			var change = (ScriptObject)ctx["record_change"];
			change["previous_state"].Should().Be("Draft");
			change["current_state"].Should().Be("Finalized");
			change["reason_code"].Should().BeNull();
			change["number_disposition"].Should().Be("none", "extra facts surface on record_change");

			UserProfileServiceMock.Verify(s => s.GetProfileByUserIdAsync("author-1", It.IsAny<bool>()), Times.AtLeastOnce, "the author is the triggering user");
		}

		[Test]
		public async Task Malformed_payload_still_yields_empty_namespaces_rather_than_a_failed_run()
		{
			var dispatched = new DomainEventDispatchedEvent { DepartmentId = 1, EventId = "evt-9", EventName = "RecordVoided", TriggerEventType = (int)WorkflowTriggerEventType.RecordVoided, PayloadJson = "not json" };

			var ctx = await BuildContext(WorkflowTriggerEventType.RecordVoided, RecordsWorkflowEvent.From(dispatched));

			((ScriptObject)ctx["event"])["name"].Should().Be("RecordVoided");
			((ScriptObject)ctx["record"])["url"].Should().Be(string.Empty);
			((ScriptObject)ctx["record_change"]).Count.Should().Be(0);
		}

		[Test]
		public async Task LogAdded_compatibility_payload_maps_through_the_existing_log_namespace()
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = "rec-1",
				DepartmentId = 1,
				DefinitionKey = RmsDefinitionKeys.Training,
				RecordType = (int)RmsOperationalRecordType.Training,
				AuthorUserId = "author-1",
				CallId = 9,
				FinalizedOn = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
			};
			var details = new RmsOperationalRecordDetail { Narrative = "Hose evolutions", Course = "Pump Ops", CourseCode = "PO-101", BodyLocation = "never" };

			var ctx = await BuildContext(WorkflowTriggerEventType.LogAdded, LogAddedCompatibility.Build(record, details, DateTime.UtcNow));

			var log = (ScriptObject)ctx["log"];
			log["course"].Should().Be("Pump Ops");
			log["course_code"].Should().Be("PO-101");
			log["narrative"].Should().Be("Hose evolutions");
			log["log_type"].Should().Be((int)LogTypes.Training);
			log["call_id"].Should().Be(9);
			log.ContainsKey("body_location").Should().BeFalse("the legacy contract never carried restricted fields");
			UserProfileServiceMock.Verify(s => s.GetProfileByUserIdAsync("author-1", It.IsAny<bool>()), Times.AtLeastOnce);
		}
	}
}
