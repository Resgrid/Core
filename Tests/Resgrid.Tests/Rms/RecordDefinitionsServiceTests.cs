using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-1B configurable definitions: designer lifecycle, validation, publish immutability, triggers 113/114, diff and migration.</summary>
	[TestFixture]
	public class RecordDefinitionsServiceTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsDefinitionHarness();

		[Test]
		public async Task Template_clones_may_raise_a_pack_policy_floor_but_never_lower_it()
		{
			var aggregate = await _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = "sar-mission", Name = "SAR mission", TemplateKey = "pack.sar.mission-summary" });
			var draft = aggregate.Draft;
			var input = RecordDefinitionsService.ToDraftInput(draft);
			input.Schema.FindField("subject_name").Classification = RmsFieldClassification.Protected;
			var raised = await _h.Definitions.SaveDraftAsync(Dept, Admin, "sar-mission", draft.Version, draft.RowVersion, input);
			raised.Schema.FindField("subject_name").Classification.Should().Be(RmsFieldClassification.Protected, "a department may raise the floor");

			var lowered = RecordDefinitionsService.ToDraftInput(raised);
			lowered.Schema.FindField("medical_concerns").Classification = RmsFieldClassification.Standard;
			Func<Task> save = () => _h.Definitions.SaveDraftAsync(Dept, Admin, "sar-mission", raised.Version, raised.RowVersion, lowered);
			(await save.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("medical_concerns").And.Contain("policy floor");
		}

		[Test]
		public async Task Create_from_template_renders_the_profile_overlay_and_starts_as_draft_v1()
		{
			var aggregate = await _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput
			{
				DefinitionKey = "Security-Patrol", Name = "Site patrol", TemplateKey = "template.security-patrol", JurisdictionProfileKey = "ca", Locale = "fr-CA"
			});

			aggregate.Definition.DefinitionKey.Should().Be("security-patrol");
			aggregate.Definition.TemplateKey.Should().Be("template.security-patrol");
			aggregate.Definition.JurisdictionProfileKey.Should().Be("ca");
			aggregate.Definition.Owner.Should().Be((int)RmsDefinitionOwner.Department);
			aggregate.Published.Should().BeNull();
			var draft = aggregate.Draft;
			draft.Version.Should().Be(1);
			draft.Schema.FindField("officer").Label.Should().Be("Agent", "the fr-CA overlay relabels the rendered schema");
			draft.Schema.FindSection("exceptions").Rules.Should().ContainSingle(r => r.Effect == RmsRuleEffect.Show);
			draft.Numbering.Prefix.Should().Be("PAT");
			draft.MinimumClientCapability.Should().Be(RecordsClientCapabilities.Configurable);
			_h.Store.Audits.Should().ContainSingle(a => a.Purpose.Contains("Create definition"));

			var summaries = await _h.Definitions.ListAsync(Dept);
			summaries.Should().ContainSingle(s => s.Key == "security-patrol" && s.DraftVersion == 1 && s.PublishedVersion == null && !s.Locked);
			(await _h.Definitions.GetPublishedAsync(Dept)).Should().BeEmpty();
		}

		[Test]
		public async Task Create_rejects_reserved_keys_duplicates_and_unknown_templates()
		{
			Func<Task> reserved = () => _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = "system.run", Name = "x" });
			await reserved.Should().ThrowAsync<ArgumentException>().WithMessage("*reserved*");
			await _h.CreateAsync("shift-log", "Shift log", Schema(Section("s", "S", Field("summary", RmsFieldType.ShortText))));
			Func<Task> duplicate = () => _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = "shift-log", Name = "again" });
			await duplicate.Should().ThrowAsync<ArgumentException>().WithMessage("*already exists*");
			Func<Task> template = () => _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = "other", Name = "x", TemplateKey = "template.nope" });
			await template.Should().ThrowAsync<ArgumentException>().WithMessage("*not a product template*");
			_h.Authorization.Setup(a => a.HasPermissionAsync("viewer", Dept, PermissionTypes.ManageRecordDefinitions)).ReturnsAsync(false);
			Func<Task> denied = () => _h.Definitions.CreateAsync(Dept, "viewer", new RecordDefinitionCreateInput { DefinitionKey = "denied", Name = "x" });
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Blank_definitions_start_with_a_starter_schema_that_validates()
		{
			var aggregate = await _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = "blank", Name = "Blank" });
			aggregate.Draft.Schema.Sections.Should().ContainSingle(s => s.Key == "details");
			aggregate.Draft.Numbering.Prefix.Should().NotBeNullOrEmpty();
			(await _h.Definitions.ValidateAsync(Dept, RecordDefinitionsService.ToDraftInput(aggregate.Draft))).IsValid.Should().BeTrue();
		}

		[Test]
		public async Task Validation_reports_cycles_flag_misuse_and_bad_policies_with_codes()
		{
			var a = Field("a", RmsFieldType.Boolean); a.Rules.Add(ShowWhen("b", "true"));
			var b = Field("b", RmsFieldType.Boolean); b.Rules.Add(ShowWhen("a", "true"));
			var secret = Field("secret", RmsFieldType.ShortText, classification: RmsFieldClassification.Restricted, configure: f => f.Searchable = true);
			var notes = Field("notes", RmsFieldType.LongText, configure: f => { f.Groupable = true; f.Sortable = true; });
			var qty = Field("qty", RmsFieldType.Quantity, configure: f => f.UnitFamily = "bogus");
			var dup = Field("a", RmsFieldType.ShortText);
			var input = new RecordDefinitionDraftInput
			{
				Name = "Bad", Numbering = new RecordDefinitionNumbering { Prefix = "toolongprefix", SequenceWidth = 2 }, ReviewDueHours = 0,
				Schema = Schema(Section("s", "S", a, b, secret, notes, qty, dup), Section("empty", "Empty"))
			};
			var validation = await _h.Definitions.ValidateAsync(Dept, input);
			validation.IsValid.Should().BeFalse();
			var codes = validation.Issues.Select(i => i.Code).ToList();
			codes.Should().Contain(new[] { "cycle", "protected_exposed", "not_groupable", "not_filterable", "bad_unit_family", "duplicate_key", "bad_prefix", "out_of_range", "no_fields" });
			validation.MinimumClientCapability.Should().Be(RecordsClientCapabilities.Packs, "a quantity field lifts the floor to records.v1c");
			validation.Issues.Should().Contain(i => i.Severity == "warning" && i.Code == "capability");
		}

		[Test]
		public async Task Publish_freezes_the_version_materializes_rows_and_enqueues_trigger_113()
		{
			var schema = Schema(Section("shift", "Shift", Field("site", RmsFieldType.ShortText, true, configure: f => { f.Searchable = true; f.WorkflowExposed = true; }), Select("status", "Open", "Closed")),
				Rows("stops", "Stops", 1, 10, Field("stop", RmsFieldType.ShortText, true), Field("minutes", RmsFieldType.Integer, configure: f => f.Aggregatable = true)));
			await _h.CreateAsync("shift-log", "Shift log", schema, d => { d.Numbering.Prefix = "SL"; d.ReviewerRoleIds = new List<int> { 5 }; d.LifecyclePreset = RmsLifecyclePreset.ReviewRequired; });

			var published = await _h.PublishAsync("shift-log");
			published.IsPublished.Should().BeTrue();
			published.SchemaChecksum.Should().NotBeNullOrEmpty();
			published.PublishedByUserId.Should().Be(Admin);
			published.MinimumClientCapability.Should().Be(RecordsClientCapabilities.Configurable);
			_h.Defs.Sections.Where(s => s.RmsRecordDefinitionVersionId == published.RmsRecordDefinitionVersionId).Select(s => s.SectionKey).Should().Equal("shift", "stops");
			_h.Defs.Fields.Where(f => f.RmsRecordDefinitionVersionId == published.RmsRecordDefinitionVersionId).Select(f => f.FieldKey).Should().Equal("site", "status", "stop", "minutes");
			_h.Defs.Fields.Single(f => f.FieldKey == "minutes").Aggregatable.Should().BeTrue();
			_h.Defs.Definitions.Single().CurrentPublishedVersion.Should().Be(1);

			var events = _h.Events(WorkflowTriggerEventType.RecordDefinitionPublished).ToList();
			events.Should().HaveCount(1);
			events[0].AggregateType.Should().Be(RecordDefinitionsService.DefinitionAggregate);
			events[0].PayloadJson.Should().Contain("\"key\":\"shift-log\"").And.Contain("\"version\":1").And.Contain("\"site\"");
			_h.Published.Should().ContainSingle(e => e.EventName == WorkflowTriggerEventType.RecordDefinitionPublished.ToString(), "dispatch happens after commit");

			// Published versions are immutable; the next draft is v2 and publishes over the pointer.
			Func<Task> mutate = () => _h.Definitions.SaveDraftAsync(Dept, Admin, "shift-log", 1, published.RowVersion, RecordDefinitionsService.ToDraftInput(published));
			await mutate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
			(await _h.Definitions.GetPublishedAsync(Dept)).Should().ContainSingle(v => v.Version == 1);
			(await _h.Definitions.GetCurrentPublishedAsync(Dept, "shift-log")).Version.Should().Be(1);

			var draft = await _h.Definitions.OpenDraftAsync(Dept, Admin, "shift-log");
			draft.Version.Should().Be(2); draft.IsDraft.Should().BeTrue();
			draft.Schema.FindField("site").Should().NotBeNull("the draft copies the published schema");
			Func<Task> second = () => _h.Definitions.OpenDraftAsync(Dept, Admin, "shift-log");
			await second.Should().ThrowAsync<InvalidOperationException>("one open draft at a time");

			var impact = await _h.Definitions.ImpactPreviewAsync(Dept, "shift-log", 2);
			impact.Clients.Should().HaveCount(4);
			impact.CurrentPublishedVersion.Should().Be(1);
			impact.UsesRepeatingGroups.Should().BeTrue();

			_h.Authorization.Setup(a => a.HasPermissionAsync("editor", Dept, PermissionTypes.PublishRecordDefinitions)).ReturnsAsync(false);
			Func<Task> denied = () => _h.Definitions.PublishAsync(Dept, "editor", "shift-log", 2, draft.RowVersion);
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> stale = () => _h.Definitions.PublishAsync(Dept, Admin, "shift-log", 2, draft.RowVersion + 5);
			await stale.Should().ThrowAsync<RecordConcurrencyException>();
		}

		/// <summary>
		/// Regression: publishing a department definition created from a template failed with "Content cannot be written
		/// to a missing or purged RMS record" because the Records outbox repository applied the live-content guard to every
		/// Records event, including definition events whose aggregate id is the definition, not a Record. The harness
		/// runs the same guard, so this test fails without the aggregate-type discrimination in DomainEventOutboxRepository.
		/// </summary>
		[Test]
		public async Task Publish_and_retire_of_a_template_created_definition_pass_the_outbox_live_content_guard()
		{
			var aggregate = await _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput
			{
				DefinitionKey = "ops.security-patrol", Name = "Security Patrol Log", Category = "Security", TemplateKey = "template.security-patrol", JurisdictionProfileKey = "us"
			});
			aggregate.Draft.Should().NotBeNull();
			(await _h.Definitions.ValidateAsync(Dept, RecordDefinitionsService.ToDraftInput(aggregate.Draft, aggregate.Definition))).IsValid.Should().BeTrue();

			var published = await _h.PublishAsync("ops.security-patrol");
			published.IsPublished.Should().BeTrue();
			published.DefinitionKey.Should().Be("ops.security-patrol");
			_h.Defs.Definitions.Single().CurrentPublishedVersion.Should().Be(1);
			var publishedEvent = _h.Events(WorkflowTriggerEventType.RecordDefinitionPublished).Should().ContainSingle().Which;
			publishedEvent.AggregateType.Should().Be(RecordDefinitionsService.DefinitionAggregate);
			publishedEvent.AggregateId.Should().Be(aggregate.Definition.RmsRecordDefinitionId, "the definition, not a Record, is the aggregate");
			DomainEventProducers.IsRecordContentAggregate(publishedEvent.AggregateType).Should().BeFalse();
			_h.Published.Should().ContainSingle(e => e.EventName == WorkflowTriggerEventType.RecordDefinitionPublished.ToString());

			var definition = _h.Defs.Definitions.Single();
			var retired = await _h.Definitions.RetireAsync(Dept, Admin, "ops.security-patrol", definition.RowVersion, "Superseded by the v2 patrol log");
			retired.IsRetired.Should().BeTrue();
			_h.Events(WorkflowTriggerEventType.RecordDefinitionRetired).Should().ContainSingle().Which.AggregateType.Should().Be(RecordDefinitionsService.DefinitionAggregate);
		}

		[Test]
		public async Task Outbox_guard_still_refuses_record_events_for_a_missing_record()
		{
			// The guard the definition test relies on is live in this harness: a Records event that names a Record which
			// does not exist is refused, exactly as the repository does for a missing or purged row.
			Func<Task> missing = () => _h.Outbox.EnqueueAsync(Dept, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = WorkflowTriggerEventType.RecordFinalized.ToString(), SchemaVersion = 1, AggregateType = DomainEventProducers.RecordsAggregate, AggregateId = "no-such-record", AggregateVersion = 1
			});
			(await missing.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("missing or purged");

			Func<Task> untyped = () => _h.Outbox.EnqueueAsync(Dept, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = WorkflowTriggerEventType.RecordDefinitionPublished.ToString(), SchemaVersion = 1, AggregateId = "def-1", AggregateVersion = 1
			});
			(await untyped.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("AggregateType");
		}

		[Test]
		public async Task Retire_marks_definition_and_versions_enqueues_114_and_blocks_new_drafts()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", Schema(Section("s", "S", Field("summary", RmsFieldType.ShortText))));
			var definition = _h.Defs.Definitions.Single();
			Func<Task> noReason = () => _h.Definitions.RetireAsync(Dept, Admin, "shift-log", definition.RowVersion, " ");
			await noReason.Should().ThrowAsync<ArgumentException>();

			var retired = await _h.Definitions.RetireAsync(Dept, Admin, "shift-log", definition.RowVersion, "Replaced by shift-log-v2");
			retired.IsRetired.Should().BeTrue();
			retired.RetiredReason.Should().Be("Replaced by shift-log-v2");
			_h.Version("shift-log", 1).State.Should().Be((int)RmsDefinitionVersionState.Retired);
			_h.Events(WorkflowTriggerEventType.RecordDefinitionRetired).Should().ContainSingle().Which.PayloadJson.Should().Contain("Replaced by shift-log-v2");
			(await _h.Definitions.GetPublishedAsync(Dept)).Should().BeEmpty("retired definitions are not offered on New Record");
			(await _h.Definitions.ListAsync(Dept)).Should().NotContain(s => s.Key == "shift-log");
			(await _h.Definitions.ListAsync(Dept, includeRetired: true)).Should().ContainSingle(s => s.Key == "shift-log" && s.Retired);
			Func<Task> reopen = () => _h.Definitions.OpenDraftAsync(Dept, Admin, "shift-log");
			await reopen.Should().ThrowAsync<InvalidOperationException>();
			(await _h.Definitions.RetireAsync(Dept, Admin, "shift-log", 999, "again")).IsRetired.Should().BeTrue("retire is idempotent");
		}

		[Test]
		public async Task Diff_reports_breaking_changes_and_migration_moves_open_drafts_forward()
		{
			var v1 = Schema(Section("s", "S", Field("summary", RmsFieldType.ShortText), Field("count", RmsFieldType.Integer), Field("old_note", RmsFieldType.LongText)));
			await _h.CreateAndPublishAsync("shift-log", "Shift log", v1);
			var published1 = _h.Version("shift-log", 1);

			// A draft Record on v1 with values, plus a finalized one that must never move.
			var record = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("s", "summary", "Night shift"), Value("s", "count", "4"), Value("s", "old_note", "keep me") }
			});
			var finalized = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("s", "summary", "Done") } });
			await _h.Records.FinalizeAsync(Dept, Author, finalized.Record.RmsOperationalRecordId, finalized.Record.RowVersion, "1", null, null);

			var draft2 = await _h.Definitions.OpenDraftAsync(Dept, Admin, "shift-log");
			var input = RecordDefinitionsService.ToDraftInput(draft2);
			input.Schema = Schema(Section("s", "S", Field("summary", RmsFieldType.ShortText, true), Field("headcount", RmsFieldType.Integer), Field("new_flag", RmsFieldType.Boolean)));
			input.ChangeNotes = "rename count, drop old_note";
			await _h.Definitions.SaveDraftAsync(Dept, Admin, "shift-log", 2, draft2.RowVersion, input);
			var diff = await _h.Definitions.DiffAsync(Dept, "shift-log", 1, 2);
			diff.Entries.Should().Contain(e => e.Kind == "field" && e.Change == "removed" && e.Key == "count");
			diff.Entries.Should().Contain(e => e.Kind == "field" && e.Change == "added" && e.Key == "headcount");
			diff.Breaking.Should().BeTrue("removing a field breaks older values");

			Func<Task> early = () => _h.Definitions.MigrateDraftsAsync(Dept, Admin, "shift-log", 1, 2, null, true);
			await early.Should().ThrowAsync<InvalidOperationException>("drafts only migrate to a published version");
			await _h.PublishAsync("shift-log");

			var mapping = new List<RecordDefinitionFieldMapping> { new RecordDefinitionFieldMapping { FromFieldKey = "count", ToFieldKey = "headcount" } };
			var preview = await _h.Definitions.MigrateDraftsAsync(Dept, Admin, "shift-log", 1, 2, mapping, true);
			preview.Migrated.Should().Be(1, "only the open draft on v1 counts; finalized Records never move");
			preview.UnmappedFieldKeys.Should().Contain("old_note").And.NotContain("count");
			_h.Store.Records.Single(r => r.RmsOperationalRecordId == record.Record.RmsOperationalRecordId).DefinitionVersion.Should().Be(1, "preview changes nothing");

			var result = await _h.Definitions.MigrateDraftsAsync(Dept, Admin, "shift-log", 1, 2, mapping, false);
			result.Migrated.Should().Be(1);
			var moved = _h.Store.Records.Single(r => r.RmsOperationalRecordId == record.Record.RmsOperationalRecordId);
			moved.DefinitionVersion.Should().Be(2);
			var values = await _h.TypedValues.HydrateAsync(Dept, moved.RmsOperationalRecordId, null, _h.Version("shift-log", 2), true);
			values.Scalar("headcount").Value.Should().Be("4");
			values.Scalar("summary").Value.Should().Be("Night shift");
			values.Scalar("old_note").Should().BeNull("dropped fields do not survive migration");
			_h.Store.Records.Single(r => r.RmsOperationalRecordId == finalized.Record.RmsOperationalRecordId).DefinitionVersion.Should().Be(1);
			published1.IsPublished.Should().BeTrue("earlier published versions stay readable for their Records");
		}

		[Test]
		public async Task Draft_versions_delete_only_while_unreferenced()
		{
			await _h.CreateAsync("shift-log", "Shift log", Schema(Section("s", "S", Field("summary", RmsFieldType.ShortText))));
			var draft = _h.Version("shift-log", 1);
			_h.Defs.Values.Add(new RmsRecordValue { RmsRecordValueId = "v", DepartmentId = Dept, RecordId = "r1", RmsRecordDefinitionVersionId = draft.RmsRecordDefinitionVersionId, FieldKey = "summary", TextValue = "x" });
			Func<Task> referenced = () => _h.Definitions.DeleteDraftAsync(Dept, Admin, "shift-log", 1);
			await referenced.Should().ThrowAsync<InvalidOperationException>();
			_h.Defs.Values.Clear();
			(await _h.Definitions.DeleteDraftAsync(Dept, Admin, "shift-log", 1)).Should().BeTrue();
			_h.Defs.Versions.Should().BeEmpty();
			(await _h.Definitions.GetAsync(Dept, "shift-log")).Should().BeNull("the last version deleted removes the definition");
			(await _h.Definitions.DeleteDraftAsync(Dept, Admin, "shift-log", 1)).Should().BeFalse();
		}

		[Test]
		public async Task Clone_copies_the_published_version_under_a_new_key()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", Schema(Section("s", "S", Field("summary", RmsFieldType.ShortText))), d => d.Numbering.Prefix = "SL");
			var clone = await _h.Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = "shift-log-b", Name = "Shift log B", CloneFromDefinitionKey = "shift-log" });
			clone.Draft.Schema.FindField("summary").Should().NotBeNull();
			clone.Draft.Numbering.Prefix.Should().Be("SL");
			clone.Definition.Name.Should().Be("Shift log B");
			clone.Published.Should().BeNull();
		}

		[Test]
		public void Cycle_detection_finds_a_loop_and_ignores_trees()
		{
			var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase) { ["a"] = new HashSet<string> { "b" }, ["b"] = new HashSet<string> { "c" }, ["c"] = new HashSet<string>() };
			RecordDefinitionsService.FindCycle(graph).Should().BeNull();
			graph["c"].Add("a");
			RecordDefinitionsService.FindCycle(graph).Should().NotBeNull().And.Contain("a");
		}
		[Test]
		public async Task Rules_may_reference_a_repeating_row_only_from_a_field_of_the_same_section()
		{
			var sameRow = Field("failure_reason", RmsFieldType.ShortText); sameRow.Rules.Add(ShowWhen("delivered", "false"));
			var ok = new RecordDefinitionDraftInput { Name = "Ok", Numbering = new RecordDefinitionNumbering { Prefix = "OK" }, Schema = Schema(Section("run", "Run", Field("summary", RmsFieldType.ShortText)), Rows("stops", "Stops", null, null, Field("delivered", RmsFieldType.Boolean), sameRow)) };
			(await _h.Definitions.ValidateAsync(Dept, ok)).Issues.Should().NotContain(i => i.Code == "repeating_reference");

			var crossSection = Field("note", RmsFieldType.ShortText); crossSection.Rules.Add(ShowWhen("delivered", "false"));
			var sectionRule = Rows("extras", "Extras", null, null, Field("x", RmsFieldType.ShortText)); sectionRule.Rules.Add(ShowWhen("delivered", "true"));
			var bad = new RecordDefinitionDraftInput { Name = "Bad", Numbering = new RecordDefinitionNumbering { Prefix = "BAD" }, Schema = Schema(Section("run", "Run", crossSection), Rows("stops", "Stops", null, null, Field("delivered", RmsFieldType.Boolean)), sectionRule) };
			var issues = (await _h.Definitions.ValidateAsync(Dept, bad)).Issues.Where(i => i.Code == "repeating_reference").ToList();
			issues.Should().HaveCount(2, "a scalar field and a section rule both reach into the repeating section");
		}

	}
}
