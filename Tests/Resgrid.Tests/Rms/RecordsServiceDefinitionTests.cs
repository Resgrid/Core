using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RecordsService on a department definition (RMS-1B): pinned version, typed values, numbering policy, finalize rules, role narrowing and the definition/fields Workflow blocks.</summary>
	[TestFixture]
	public class RecordsServiceDefinitionTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsDefinitionHarness();

		private static RecordDefinitionSchema ShiftLog() => Schema(
			Section("shift", "Shift",
				Field("site", RmsFieldType.ShortText, true, configure: f => { f.Searchable = true; f.WorkflowExposed = true; f.Filterable = true; f.Sortable = true; }),
				Field("hours", RmsFieldType.Quantity, configure: f => { f.UnitFamily = "time"; f.DefaultUnit = "h"; f.Aggregatable = true; f.WorkflowExposed = true; }),
				Select("status", "Open", "Closed"), Field("secret", RmsFieldType.ShortText, classification: RmsFieldClassification.Restricted), Field("notes", RmsFieldType.LongText)),
			Rows("stops", "Stops", null, 10, Field("stop", RmsFieldType.ShortText, true, configure: f => f.WorkflowExposed = true), Field("minutes", RmsFieldType.Integer, configure: f => f.Aggregatable = true)));

		[Test]
		public async Task Records_on_a_definition_pin_the_published_version_store_values_and_reserve_numbers_on_create()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", ShiftLog(), d => { d.Numbering = new RecordDefinitionNumbering { Prefix = "SL", Assignment = RmsNumberAssignment.OnCreate, SequenceWidth = 3, ResetYearly = true }; });
			Func<Task> unpublished = () => _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "nope" });
			await unpublished.Should().ThrowAsync<ArgumentException>().WithMessage("*not a published definition*");

			var draft = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "shift-log",
				Values = new List<RecordValueInput> { Value("shift", "site", "Depot 4"), new RecordValueInput { SectionKey = "shift", FieldKey = "hours", Value = "2", UnitCode = "h" }, Value("shift", "status", "open"), Value("shift", "secret", "SSN") }
			});
			draft.Record.RecordType.Should().BeNull();
			draft.Record.DefinitionVersion.Should().Be(1);
			draft.Record.LifecyclePreset.Should().Be((int)RmsLifecyclePreset.QuickEntry);
			draft.Record.RecordNumber.Should().Be("SL-" + DateTime.UtcNow.Year + "-001", "OnCreate reserves the number at draft creation with the definition's prefix and width");
			draft.Record.DisplaySummary.Should().Be("Depot 4");
			draft.DefinitionVersionRow.Version.Should().Be(1);
			draft.Values.Scalar("hours").CanonicalNumber.Should().Be(120m);
			draft.Values.Scalar("status").Display.Should().Be("Open");
			_h.Store.Projections.Single().SearchText.Should().Contain("Depot 4").And.NotContain("SSN");

			var second = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("shift", "site", "Depot 5") } });
			second.Record.RecordNumber.Should().Be("SL-" + DateTime.UtcNow.Year + "-002");

			// Autosave stores values without enforcing requiredness; finalize enforces them.
			var saved = await _h.Records.SaveDraftAsync(Dept, Author, second.Record.RmsOperationalRecordId, second.Record.RowVersion, new RecordDraftInput
			{
				DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("stops", "stop", "Gate", "r1", 0), Value("stops", "minutes", "15", "r1", 0) }
			});
			saved.Values.Scalar("site").Display.Should().BeNull("a save replaces the draft values");
			saved.Values.Section("stops").Rows.Should().HaveCount(1);
			Func<Task> finalizeInvalid = () => _h.Records.FinalizeAsync(Dept, Author, second.Record.RmsOperationalRecordId, saved.Record.RowVersion, "1", null, null);
			await finalizeInvalid.Should().ThrowAsync<ArgumentException>().WithMessage("*Site*required*");
			_h.Store.Revisions.Should().BeEmpty();
		}

		[Test]
		public async Task Finalize_snapshots_typed_values_and_lifecycle_events_carry_definition_and_fields_blocks()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", ShiftLog(), d => d.Numbering.Prefix = "SL");
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "shift-log",
				Values = new List<RecordValueInput>
				{
					Value("shift", "site", "Depot 4"), new RecordValueInput { SectionKey = "shift", FieldKey = "hours", Value = "90", UnitCode = "min" }, Value("shift", "secret", "SSN"), Value("shift", "notes", "quiet night"),
					Value("stops", "stop", "Gate", "r1", 0), Value("stops", "minutes", "15", "r1", 0), Value("stops", "stop", "Yard", "r2", 1), Value("stops", "minutes", "25", "r2", 1)
				}
			});
			draft.Record.RecordNumber.Should().BeNull("OnFinalize numbering waits");
			var finalized = await _h.Records.FinalizeAsync(Dept, Author, draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);
			finalized.Record.RecordNumber.Should().StartWith("SL-");
			finalized.Record.State.Should().Be((int)RmsRecordState.Finalized);

			var revision = _h.Store.Revisions.Single();
			var snapshot = JObject.Parse(revision.SnapshotJson);
			var values = snapshot["Values"] ?? snapshot["values"];
			values.Should().NotBeNull("typed values ride in the revision snapshot");
			values["Shift"]["Site"].Value<string>().Should().Be("Depot 4");
			values["Shift"]["Secret" + RecordSnapshotSerializer.RestrictedValueSuffix].Value<string>().Should().Be("SSN");
			((JArray)values["Stops"]).Should().HaveCount(2);
			_h.Defs.Values.Count(v => v.RevisionId == revision.RmsRevisionId).Should().Be(8, "every draft row is copied onto the revision");
			_h.Defs.Values.Count(v => v.RevisionId == null).Should().Be(8, "the draft rows stay for the next amendment");

			var finalizedEvent = _h.Store.Outbox.Single(e => e.TriggerEventType == (int)WorkflowTriggerEventType.RecordFinalized);
			var payload = JObject.Parse(finalizedEvent.PayloadJson);
			payload["definition"]["key"].Value<string>().Should().Be("shift-log");
			payload["definition"]["version"].Value<int>().Should().Be(1);
			((JArray)payload["definition"]["exposed_field_keys"]).Select(t => t.Value<string>()).Should().Contain("site").And.NotContain("secret");
			payload["fields"]["site"].Value<string>().Should().Be("Depot 4");
			payload["fields"]["hours"].Value<decimal>().Should().Be(90m);
			payload["fields"]["stops_count"].Value<int>().Should().Be(2);
			payload["fields"]["secret"].Should().BeNull("restricted values never reach Workflow");
			payload["fields"]["notes"].Should().BeNull("only WorkflowExposed fields are published");

			// Reading back as a viewer without RecordRestricted_View withholds the restricted cell but keeps the rest.
			var full = await _h.Records.GetAsync(Dept, draft.Record.RmsOperationalRecordId, true);
			full.Values.Scalar("secret").Display.Should().Be("SSN");
			full.DefinitionVersionRow.Version.Should().Be(1);
		}

		[Test]
		public async Task Definition_roles_narrow_who_may_review_and_approve()
		{
			await _h.CreateAndPublishAsync("inspection", "Inspection", ShiftLog(), d =>
			{
				d.LifecyclePreset = RmsLifecyclePreset.ApprovalAcknowledgement; d.ReviewerRoleIds = new List<int> { 5 }; d.ApproverRoleIds = new List<int> { 9 }; d.ReviewDueHours = 12;
			});
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "inspection", Values = new List<RecordValueInput> { Value("shift", "site", "Depot 4") } });
			draft.Record.LifecyclePreset.Should().Be((int)RmsLifecyclePreset.ApprovalAcknowledgement);
			var submitted = await _h.Records.SubmitForReviewAsync(Dept, Author, draft.Record.RmsOperationalRecordId, draft.Record.RowVersion);
			submitted.Record.State.Should().Be((int)RmsRecordState.ReadyForReview);
			submitted.Record.ReviewDueOn.Should().BeCloseTo(DateTime.UtcNow.AddHours(12), TimeSpan.FromMinutes(2), "the version's review window wins over the department setting");

			_h.Roles.Setup(r => r.GetRolesForUserAsync("reviewer", Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 5, DepartmentId = Dept, Name = "Lieutenant" } });
			_h.Roles.Setup(r => r.GetRolesForUserAsync("chief", Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 9, DepartmentId = Dept, Name = "Chief" } });
			Func<Task> notApprover = () => _h.Records.ApproveAsync(Dept, "reviewer", draft.Record.RmsOperationalRecordId);
			await notApprover.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*limits approval*");
			var approved = await _h.Records.ApproveAsync(Dept, "chief", draft.Record.RmsOperationalRecordId);
			approved.Record.State.Should().Be((int)RmsRecordState.Approved);
			var final = await _h.Records.FinalizeAsync(Dept, Author, draft.Record.RmsOperationalRecordId, approved.Record.RowVersion, "1", null, null);
			final.Record.State.Should().Be((int)RmsRecordState.Finalized, "the author acknowledges an approved Record");

			// ReviewRequired: the reviewer roles gate finalization out of ReadyForReview.
			await _h.CreateAndPublishAsync("review-only", "Review only", ShiftLog(), d => { d.LifecyclePreset = RmsLifecyclePreset.ReviewRequired; d.ReviewerRoleIds = new List<int> { 5 }; });
			var second = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "review-only", Values = new List<RecordValueInput> { Value("shift", "site", "Depot 5") } });
			var pending = await _h.Records.SubmitForReviewAsync(Dept, Author, second.Record.RmsOperationalRecordId, second.Record.RowVersion);
			Func<Task> notReviewer = () => _h.Records.FinalizeAsync(Dept, "chief", second.Record.RmsOperationalRecordId, pending.Record.RowVersion, "1", null, null);
			await notReviewer.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*limits review*");
			(await _h.Records.FinalizeAsync(Dept, "reviewer", second.Record.RmsOperationalRecordId, pending.Record.RowVersion, "1", null, null)).Record.State.Should().Be((int)RmsRecordState.Finalized);
		}

		[Test]
		public async Task Records_written_on_v1_keep_rendering_on_v1_after_v2_publishes()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", ShiftLog());
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("shift", "site", "Depot 4"), Value("shift", "notes", "keep") } });
			await _h.Records.FinalizeAsync(Dept, Author, draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);

			var v2 = await _h.Definitions.OpenDraftAsync(Dept, Admin, "shift-log");
			var input = RecordDefinitionsService.ToDraftInput(v2);
			input.Schema.FindSection("shift").Fields.RemoveAll(f => f.Key == "notes");
			await _h.Definitions.SaveDraftAsync(Dept, Admin, "shift-log", 2, v2.RowVersion, input);
			await _h.PublishAsync("shift-log");

			var old = await _h.Records.GetAsync(Dept, draft.Record.RmsOperationalRecordId, true);
			old.Record.DefinitionVersion.Should().Be(1);
			old.DefinitionVersionRow.Version.Should().Be(1);
			old.Values.Scalar("notes").Display.Should().Be("keep", "the pinned version still labels and shapes the stored values");
			var fresh = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("shift", "site", "Depot 9") } });
			fresh.Record.DefinitionVersion.Should().Be(2);
			Func<Task> stale = () => _h.Records.SaveDraftAsync(Dept, Author, fresh.Record.RmsOperationalRecordId, fresh.Record.RowVersion, new RecordDraftInput { DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("shift", "notes", "gone") } });
			await stale.Should().ThrowAsync<ArgumentException>("v2 no longer has a notes field");
		}
	}
}
