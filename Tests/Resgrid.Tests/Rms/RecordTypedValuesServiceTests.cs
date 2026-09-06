using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-1B/1C typed values: every field type parses into exactly one column group, rules, finalize validation, projections and units.</summary>
	[TestFixture]
	public class RecordTypedValuesServiceTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp()
		{
			_h = new RmsDefinitionHarness();
			_h.Store.Attachments.Add(new RmsRecordAttachment { RmsRecordAttachmentId = "att-1", DepartmentId = Dept, RecordId = "rec-1", FileName = "photo.jpg" });
		}

		private static RecordDefinitionSchema AllTypes() => Schema(
			Section("main", "Main",
				Field("short", RmsFieldType.ShortText, configure: f => f.Searchable = true), Field("long", RmsFieldType.LongText, configure: f => f.Searchable = true),
				Field("whole", RmsFieldType.Integer, configure: f => { f.Min = 0; f.Max = 10; f.WorkflowExposed = true; }), Field("dec", RmsFieldType.Decimal, configure: f => f.FixedUnitLabel = "km"),
				Field("flag", RmsFieldType.Boolean, configure: f => f.WorkflowExposed = true), Field("day", RmsFieldType.Date), Field("when", RmsFieldType.DateTime), Field("dur", RmsFieldType.Duration),
				Select("pick", "Alpha", "Beta"), Multi("many", "One", "Two", "Three"), Field("addr", RmsFieldType.Address), Field("person", RmsFieldType.Person),
				Field("unit", RmsFieldType.Unit), Field("group", RmsFieldType.Group), Field("contact", RmsFieldType.Contact), Field("file", RmsFieldType.Attachment),
				Field("sig", RmsFieldType.Signature), Field("ext", RmsFieldType.ExternalReference, configure: f => f.ReferenceType = "ticket"),
				Field("money", RmsFieldType.Currency, configure: f => f.DefaultCurrency = "CAD"), Field("length", RmsFieldType.Quantity, configure: f => { f.UnitFamily = "length"; f.DefaultUnit = "m"; f.Aggregatable = true; }),
				Field("where", RmsFieldType.CountrySubdivision), Field("call", RmsFieldType.CallReference), Field("item", RmsFieldType.InventoryReference), Field("task", RmsFieldType.ChecklistWorkOrderReference),
				Field("secret", RmsFieldType.ShortText, classification: RmsFieldClassification.Restricted)));

		[Test]
		public async Task Every_field_type_parses_to_one_column_group_and_shapes_back_with_a_display()
		{
			var version = DetachedVersion("all", AllTypes());
			var inputs = new List<RecordValueInput>
			{
				Value("main", "short", "Night shift"), Value("main", "long", "Long narrative"), Value("main", "whole", "7"), Value("main", "dec", "12.5"), Value("main", "flag", "true"),
				Value("main", "day", "2026-09-06"), Value("main", "when", "2026-09-06T10:00:00-05:00"), Value("main", "dur", "01:30"), Value("main", "pick", "beta"),
				new RecordValueInput { SectionKey = "main", FieldKey = "many", Values = new List<string> { "one", "three" } },
				new RecordValueInput { SectionKey = "main", FieldKey = "addr", Value = "1 Main St", ReferenceId = "45.5, -122.6" },
				Reference("main", "person", "author"), Reference("main", "unit", "5"), Reference("main", "group", "11"), Reference("main", "contact", "c1"), Reference("main", "file", "att-1"),
				Value("main", "sig", "I confirm this report"), Reference("main", "ext", "TCK-9", "jira"),
				new RecordValueInput { SectionKey = "main", FieldKey = "money", Value = "12.5", CurrencyCode = "usd" },
				new RecordValueInput { SectionKey = "main", FieldKey = "length", Value = "10", UnitCode = "ft" },
				Value("main", "where", "us-ca"), Reference("main", "call", "77"), Reference("main", "item", "9"), Reference("main", "task", "wo-1", "workorder"), Value("main", "secret", "hidden text")
			};
			(await _h.TypedValues.ValidateAsync(Dept, version, inputs, false)).IsValid.Should().BeTrue();

			var saved = await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, inputs);
			_h.Defs.Values.Should().HaveCount(inputs.Count + 1, "multi-select stores one row per option");
			_h.Defs.Values.Should().OnlyContain(v => v.PopulatedColumnGroups() == 1 && v.RmsRecordDefinitionVersionId == version.RmsRecordDefinitionVersionId && v.RevisionId == null);
			_h.Defs.Values.Single(v => v.FieldKey == "secret").TextValue.Should().Be("hidden text", "restricted values are stored in clear until ADP catalog v11 seals them");

			var hydrated = await _h.TypedValues.HydrateAsync(Dept, "rec-1", null, version, true);
			hydrated.DefinitionKey.Should().Be("all");
			hydrated.Scalar("short").Display.Should().Be("Night shift");
			hydrated.Scalar("whole").Number.Should().Be(7);
			hydrated.Scalar("dec").Display.Should().Be("12.5 km");
			hydrated.Scalar("flag").Value.Should().Be("true");
			hydrated.Scalar("day").Display.Should().Be("2026-09-06");
			hydrated.Scalar("when").Value.Should().Be("2026-09-06T15:00:00.0000000Z");
			hydrated.Scalar("when").OffsetMinutes.Should().Be(-300);
			hydrated.Scalar("when").Display.Should().Be("2026-09-06 10:00 -05:00");
			hydrated.Scalar("dur").Number.Should().Be(5400); hydrated.Scalar("dur").Display.Should().Be("01:30");
			hydrated.Scalar("pick").Display.Should().Be("Beta");
			hydrated.Scalar("many").Values.Should().Equal("one", "three"); hydrated.Scalar("many").Display.Should().Be("One, Three");
			hydrated.Scalar("addr").ReferenceId.Should().Be("45.5,-122.6"); hydrated.Scalar("addr").Display.Should().Be("1 Main St (45.5,-122.6)");
			hydrated.Scalar("person").Display.Should().Be("Pat Author"); hydrated.Scalar("person").ReferenceType.Should().Be("user");
			hydrated.Scalar("unit").Display.Should().Be("Engine 5");
			hydrated.Scalar("group").Display.Should().Be("Station 1");
			hydrated.Scalar("contact").Display.Should().Be("Acme Logistics");
			hydrated.Scalar("file").ReferenceId.Should().Be("att-1");
			hydrated.Scalar("sig").ReferenceType.Should().Be("signature"); hydrated.Scalar("sig").ReferenceId.Should().Be(Author.ToUpperInvariant());
			hydrated.Scalar("sig").Display.Should().Contain("Pat Author").And.Contain("signed");
			hydrated.Scalar("ext").ReferenceType.Should().Be("external:jira"); hydrated.Scalar("ext").Display.Should().Be("TCK-9 (jira)");
			hydrated.Scalar("money").CurrencyCode.Should().Be("USD"); hydrated.Scalar("money").Display.Should().Be("12.50 USD");
			hydrated.Scalar("length").UnitCode.Should().Be("ft"); hydrated.Scalar("length").CanonicalNumber.Should().Be(3.048m); hydrated.Scalar("length").CanonicalUnitCode.Should().Be("m");
			hydrated.Scalar("where").Value.Should().Be("US-CA"); hydrated.Scalar("where").Display.Should().Contain("California");
			hydrated.Scalar("call").Display.Should().Be("C-77 Structure fire");
			hydrated.Scalar("item").Display.Should().Be("Hose 50ft");
			hydrated.Scalar("task").ReferenceType.Should().Be("workorder"); hydrated.Scalar("task").Display.Should().Be("wo-1");
			hydrated.Scalar("secret").Display.Should().Be("hidden text");
			hydrated.WithheldFieldKeys.Should().BeEmpty();

			// Round trip: inputs regenerated from the set re-validate and re-save identically.
			var again = await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, hydrated.ToInputs());
			again.Scalar("length").CanonicalNumber.Should().Be(3.048m);
			again.Scalar("many").Values.Should().Equal("one", "three");
			saved.AllCells().Count(c => c.Display != null).Should().Be(again.AllCells().Count(c => c.Display != null));
		}

		[Test]
		public async Task Invalid_inputs_produce_coded_issues_instead_of_rows()
		{
			var version = DetachedVersion("all", AllTypes());
			var validation = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput>
			{
				Value("main", "whole", "11"), Value("main", "whole", "2"), Value("main", "dec", "abc"), Value("main", "flag", "maybe"), Value("main", "day", "06/09/2026"),
				Value("main", "pick", "gamma"), Reference("main", "unit", "99"), Reference("main", "person", "nobody"), Reference("main", "file", "att-9"),
				new RecordValueInput { SectionKey = "main", FieldKey = "money", Value = "1", CurrencyCode = "XXX" }, new RecordValueInput { SectionKey = "main", FieldKey = "length", Value = "1", UnitCode = "kg" },
				Value("main", "where", "US-ZZ"), Reference("main", "call", "1"), Reference("main", "task", "x", "recipe"), Value("main", "nope", "x"), Value("other", "short", "wrong section")
			}, false);
			validation.IsValid.Should().BeFalse();
			validation.Issues.Select(i => i.Code).Should().Contain(new[] { "out_of_range", "duplicate_value", "not_number", "not_boolean", "not_date", "unknown_option", "unknown_unit", "unknown_person", "unknown_currency", "unknown_subdivision", "unknown_call", "bad_reference", "unknown_field", "wrong_section" });
			validation.Issues.Should().Contain(i => i.FieldKey == "unit" && i.Code == "unknown_unit", "a unit from another department is rejected");
			Func<Task> save = () => _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput> { Value("main", "whole", "99") });
			await save.Should().ThrowAsync<ArgumentException>();
			_h.Defs.Values.Should().BeEmpty();
		}

		[Test]
		public async Task Rules_show_and_require_fields_only_at_finalize_and_hidden_sections_never_block()
		{
			var escalated = Field("escalated", RmsFieldType.Boolean);
			var escalatedTo = Field("escalated_to", RmsFieldType.ShortText); escalatedTo.Rules.Add(ShowWhen("escalated", "true")); escalatedTo.Rules.Add(RequireWhen("escalated", "true"));
			var hiddenSection = Section("police", "Police", Field("police_reference", RmsFieldType.ShortText, true)); hiddenSection.Rules.Add(ShowWhen("escalated", "true"));
			var schema = Schema(Section("main", "Main", Field("summary", RmsFieldType.ShortText, true), escalated, escalatedTo), hiddenSection);
			var version = DetachedVersion("rules", schema);

			var draft = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput>(), false);
			draft.IsValid.Should().BeTrue("autosave never enforces requiredness");

			var finalizeEmpty = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput>(), true);
			finalizeEmpty.IsValid.Should().BeFalse();
			finalizeEmpty.Issues.Select(i => i.FieldKey).Should().Contain("summary").And.NotContain("escalated_to").And.NotContain("police_reference", "hidden fields and sections are never required");

			var escalatedNoTarget = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput> { Value("main", "summary", "x"), Value("main", "escalated", "true") }, true);
			escalatedNoTarget.IsValid.Should().BeFalse();
			escalatedNoTarget.Issues.Select(i => i.FieldKey).Should().Contain("escalated_to").And.Contain("police_reference");

			var complete = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput> { Value("main", "summary", "x"), Value("main", "escalated", "true"), Value("main", "escalated_to", "Duty officer"), Value("police", "police_reference", "P-1") }, true);
			complete.IsValid.Should().BeTrue();

			var evaluation = _h.TypedValues.EvaluateRules(schema, RecordTypedValuesService.Shape(schema, null, new[] { new RmsRecordValue { FieldKey = "escalated", BoolValue = false, ValueType = (int)RmsFieldType.Boolean } }, true));
			evaluation.HiddenFieldKeys.Should().Contain("escalated_to").And.Contain("police_reference");
			evaluation.HiddenSectionKeys.Should().Contain("police");
			evaluation.RequiredFieldKeys.Should().Contain("summary").And.NotContain("escalated_to");
		}

		[Test]
		public async Task Repeating_sections_keep_dense_ordinals_and_enforce_row_limits()
		{
			var schema = Schema(Rows("stops", "Stops", 1, 2, Field("stop", RmsFieldType.ShortText, true), Field("minutes", RmsFieldType.Integer)));
			var version = DetachedVersion("rows", schema);
			var inputs = new List<RecordValueInput>
			{
				Value("stops", "stop", "Second", "b", 5), Value("stops", "minutes", "20", "b", 5),
				Value("stops", "stop", "First", "a", 1), Value("stops", "minutes", "10", "a", 1)
			};
			var set = await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, inputs);
			var rows = set.Section("stops").Rows;
			rows.Select(r => r.RowKey).Should().Equal("a", "b");
			rows.Select(r => r.Ordinal).Should().Equal(0, 1);
			rows[0].Cell("minutes").Number.Should().Be(10);
			_h.Defs.Groups.Should().HaveCount(2);
			_h.Defs.Values.Should().OnlyContain(v => v.RmsRecordValueGroupId != null);

			inputs.Add(Value("stops", "stop", "Third", "c", 9));
			var tooMany = await _h.TypedValues.ValidateAsync(Dept, version, inputs, false);
			tooMany.Issues.Should().Contain(i => i.Code == "too_many_rows");
			var tooFew = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput>(), true);
			tooFew.Issues.Should().Contain(i => i.SectionKey == "stops" && i.Code == "too_few_rows");
			var missingCell = await _h.TypedValues.ValidateAsync(Dept, version, new List<RecordValueInput> { Value("stops", "minutes", "3", "a", 0) }, true);
			missingCell.Issues.Should().Contain(i => i.FieldKey == "stop" && i.RowKey == "a");
		}

		[Test]
		public async Task Restricted_values_are_withheld_from_readers_and_kept_out_of_search_workflow_and_snapshots()
		{
			var schema = Schema(Section("main", "Main",
				Field("site", RmsFieldType.ShortText, configure: f => { f.Searchable = true; f.WorkflowExposed = true; }), Field("notes", RmsFieldType.LongText, configure: f => f.Searchable = true),
				Field("secret", RmsFieldType.ShortText, classification: RmsFieldClassification.Restricted), Field("count", RmsFieldType.Integer, configure: f => f.WorkflowExposed = true), Select("status", "Open", "Closed")),
				Rows("crew", "Crew", null, null, Field("name", RmsFieldType.ShortText, configure: f => f.WorkflowExposed = true), Field("phone", RmsFieldType.ShortText, classification: RmsFieldClassification.Restricted)));
			var version = DetachedVersion("r", schema);
			await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput>
			{
				Value("main", "site", "Depot 4"), Value("main", "notes", "very long narrative"), Value("main", "secret", "SSN"), Value("main", "count", "3"), Value("main", "status", "open"),
				Value("crew", "name", "Pat", "r1", 0), Value("crew", "phone", "555", "r1", 0)
			});

			var reader = await _h.TypedValues.HydrateAsync(Dept, "rec-1", null, version, false);
			reader.Scalar("secret").Withheld.Should().BeTrue(); reader.Scalar("secret").Display.Should().Be(RecordTypedValuesService.Redacted); reader.Scalar("secret").Value.Should().BeNull();
			reader.Section("crew").Rows[0].Cell("phone").Withheld.Should().BeTrue();
			reader.WithheldFieldKeys.Should().BeEquivalentTo(new[] { "secret", "phone" });
			var full = await _h.TypedValues.HydrateAsync(Dept, "rec-1", null, version, true);
			full.Scalar("secret").Display.Should().Be("SSN");

			_h.TypedValues.ToSearchText(schema, full).Should().Be("Depot 4", "long text and restricted values never enter the projection");
			var workflow = _h.TypedValues.ToWorkflowBlock(schema, full);
			workflow.Keys.Should().BeEquivalentTo(new[] { "site", "count", "status", "crew", "crew_count" });
			workflow["count"].Should().Be(3L);
			((List<Dictionary<string, object>>)workflow["crew"])[0].Keys.Should().BeEquivalentTo(new[] { "name" });
			var snapshot = _h.TypedValues.ToSnapshot(schema, full);
			var main = (Dictionary<string, object>)snapshot["Main"];
			main.Keys.Should().Contain("Secret" + RecordSnapshotSerializer.RestrictedValueSuffix).And.Contain("Site");
			main["Secret" + RecordSnapshotSerializer.RestrictedValueSuffix].Should().Be("SSN");
			((List<Dictionary<string, object>>)snapshot["Crew"])[0].Keys.Should().Contain("Phone" + RecordSnapshotSerializer.RestrictedValueSuffix);
			_h.TypedValues.ToDisplaySummary(schema, full).Should().Be("Depot 4");
		}

		[Test]
		public async Task Draft_values_copy_to_a_revision_and_restore_from_it()
		{
			var schema = Schema(Section("main", "Main", Field("site", RmsFieldType.ShortText)), Rows("crew", "Crew", null, null, Field("name", RmsFieldType.ShortText)));
			var version = DetachedVersion("copy", schema);
			await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput> { Value("main", "site", "A"), Value("crew", "name", "Pat", "r1", 0) });
			await _h.TypedValues.CopyDraftToRevisionAsync(Dept, "rec-1", "rev-1");
			_h.Defs.Values.Count(v => v.RevisionId == "rev-1").Should().Be(2);
			_h.Defs.Groups.Count(g => g.RevisionId == "rev-1").Should().Be(1);
			_h.Defs.Values.Single(v => v.RevisionId == "rev-1" && v.FieldKey == "name").RmsRecordValueGroupId.Should().Be(_h.Defs.Groups.Single(g => g.RevisionId == "rev-1").RmsRecordValueGroupId, "revision rows point at the revision's group copy");

			await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput> { Value("main", "site", "B") });
			(await _h.TypedValues.HydrateAsync(Dept, "rec-1", null, version, true)).Scalar("site").Value.Should().Be("B");
			(await _h.TypedValues.HydrateAsync(Dept, "rec-1", "rev-1", version, true)).Scalar("site").Value.Should().Be("A", "revision rows are immutable");

			await _h.TypedValues.RestoreDraftFromRevisionAsync(Dept, Author, "rec-1", "rev-1", version);
			var restored = await _h.TypedValues.HydrateAsync(Dept, "rec-1", null, version, true);
			restored.Scalar("site").Value.Should().Be("A");
			restored.Section("crew").Rows.Should().ContainSingle(r => r.Cell("name").Value == "Pat");
			(await _h.TypedValues.DeleteDraftAsync(Dept, "rec-1")).Should().Be(2);
			_h.Defs.Values.Should().OnlyContain(v => v.RevisionId == "rev-1");
		}

		[Test]
		public void Units_canonicalize_with_offsets_and_currencies_and_subdivisions_validate()
		{
			RmsUnits.Canonicalize(10m, "ft").Should().Be((3.048m, "m"));
			RmsUnits.Canonicalize(32m, "F").Should().Be((0m, "C"));
			RmsUnits.Canonicalize(212m, "F").Should().Be((100m, "C"));
			RmsUnits.Canonicalize(2m, "h").Should().Be((120m, "min"));
			RmsUnits.Canonicalize(1m, "furlong").Should().BeNull();
			RmsUnits.FromCanonical(3.048m, "ft").Should().Be(10m);
			RmsUnits.PreferredUnit("length", "customary").Should().Be("ft");
			RmsUnits.PreferredUnit("length", "metric").Should().Be("m");
			RmsCurrencies.IsSupported("cad").Should().BeTrue(); RmsCurrencies.IsSupported("XXX").Should().BeFalse();
			RmsCountrySubdivisions.IsValid("CA-BC").Should().BeTrue(); RmsCountrySubdivisions.IsValid("US").Should().BeTrue(); RmsCountrySubdivisions.IsValid("US-ZZ").Should().BeFalse();
			RmsCountrySubdivisions.Label("CA-BC").Should().Contain("British Columbia");
			RecordTypedValuesService.ParseDuration("90").Should().Be(5400, "bare numbers are minutes");
			RecordTypedValuesService.ParseDuration("PT2H").Should().Be(7200);
			RecordTypedValuesService.ParseBool("no").Should().BeFalse();
		}
		[Test]
		public void Protected_rows_pack_and_unpack_their_typed_columns_byte_for_byte()
		{
			var row = new RmsRecordValue { NumberValue = 12.50m, UnitCode = "ft", CanonicalNumberValue = 3.81m, CanonicalUnitCode = "m", DateTimeValue = new DateTime(2026, 9, 6, 15, 0, 0, DateTimeKind.Utc), DateTimeOffsetMinutes = -300, DurationSeconds = 5400, ReferenceType = "geo", ReferenceSnapshotJson = "{\"coordinates\":\"45.5,-122.6\"}", BoolValue = true, OptionKey = "beta" };
			var packed = RmsRecordValuePack.Pack(row);
			packed.Should().Contain("\"NumberValue\":\"12.50\"").And.Contain("\"DateTimeValue\":\"2026-09-06T15:00:00.0000000Z\"").And.NotContain("TextValue");
			RmsRecordValuePack.Pack(new RmsRecordValue()).Should().BeNull("an empty row packs to nothing");

			var copy = new RmsRecordValue();
			RmsRecordValuePack.Unpack(copy, packed);
			copy.NumberValue.Should().Be(12.50m); copy.UnitCode.Should().Be("ft"); copy.CanonicalNumberValue.Should().Be(3.81m); copy.DateTimeValue.Should().Be(row.DateTimeValue);
			copy.DateTimeValue.Value.Kind.Should().Be(DateTimeKind.Utc); copy.DateTimeOffsetMinutes.Should().Be(-300); copy.DurationSeconds.Should().Be(5400); copy.BoolValue.Should().BeTrue(); copy.OptionKey.Should().Be("beta");
			copy.ReferenceSnapshotJson.Should().Be(row.ReferenceSnapshotJson); copy.TextValue.Should().BeNull();
			RmsRecordValuePack.Pack(copy).Should().Be(packed);

			// The engine works on raw column dictionaries and must agree with the entity path.
			var columns = RmsRecordValuePack.UnpackColumns(packed);
			columns["NumberValue"].Should().Be(12.50m); columns["DurationSeconds"].Should().Be(5400L); columns["TextValue"].Should().BeNull();
			RmsRecordValuePack.PackColumns(columns).Should().Be(packed);

			RmsRecordValuePack.Clear(copy);
			copy.PopulatedColumnGroups().Should().Be(0);
			var sealedRow = new RmsRecordValue { ProtectedEnvelope = "rgdp:1:1:abc", IsProtected = true };
			sealedRow.IsSealed.Should().BeTrue(); new RmsRecordValue { ProtectedEnvelope = "plain" }.IsSealed.Should().BeFalse();

			// The seam accessor: packing to seal, unpacking to reveal, and the sentinel leaving a sealed row alone.
			var accessor = RmsProtectedFields.Values[RmsProtectedFields.ValueFieldId];
			var protectedRow = new RmsRecordValue { TextValue = "SSN 123", ProtectionRequired = true };
			accessor.Get(protectedRow).Should().Contain("SSN 123");
			accessor.Get(new RmsRecordValue { TextValue = "public" }).Should().BeNull("only flagged rows are offered to the seam");
			accessor.Set(protectedRow, "rgdp:1:1:sealed");
			protectedRow.TextValue.Should().BeNull(); protectedRow.ProtectedEnvelope.Should().Be("rgdp:1:1:sealed"); protectedRow.IsSealed.Should().BeTrue();
			accessor.Set(protectedRow, Resgrid.Model.ProtectedDataEnvelope.RedactionValue);
			protectedRow.IsSealed.Should().BeTrue("a refused reveal changes nothing");
			accessor.Set(protectedRow, "{\"TextValue\":\"SSN 123\"}");
			protectedRow.TextValue.Should().Be("SSN 123"); protectedRow.ProtectedEnvelope.Should().BeNull();
		}

		[Test]
		public async Task Protected_fields_flag_their_rows_and_sealed_rows_survive_a_save_that_could_not_reveal_them()
		{
			var schema = Schema(Section("main", "Main", Field("site", RmsFieldType.ShortText), Field("ssn", RmsFieldType.ShortText, classification: RmsFieldClassification.Protected)),
				Rows("crew", "Crew", null, null, Field("name", RmsFieldType.ShortText), Field("dob", RmsFieldType.Date, classification: RmsFieldClassification.Protected)));
			var version = DetachedVersion("adp", schema);
			await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput>
			{
				Value("main", "site", "Depot 4"), Value("main", "ssn", "123-45-6789"), Value("crew", "name", "Pat", "r1", 0), Value("crew", "dob", "1990-01-02", "r1", 0), Value("crew", "name", "Sam", "r2", 1)
			});
			_h.Defs.Values.Single(v => v.FieldKey == "ssn").ProtectionRequired.Should().BeTrue();
			_h.Defs.Values.Single(v => v.FieldKey == "dob").ProtectionRequired.Should().BeTrue();
			_h.Defs.Values.Where(v => v.FieldKey == "site" || v.FieldKey == "name").Should().OnlyContain(v => !v.ProtectionRequired);
			_h.Protection.Writes.Should().Contain("values:2", "the seam sees every flagged row");

			// Seal the stored rows as the engine/seam would; the next save (a viewer without a reveal) posts nothing for them.
			foreach (var row in _h.Defs.Values.Where(v => v.ProtectionRequired))
			{
				row.ProtectedEnvelope = "rgdp:1:1:" + RmsRecordValuePack.Pack(row); row.IsProtected = true; row.ProtectedCatalogVersion = 11; RmsRecordValuePack.Clear(row);
			}
			var sealedSsnId = _h.Defs.Values.Single(v => v.FieldKey == "ssn").RmsRecordValueId;
			var sealedDobId = _h.Defs.Values.Single(v => v.FieldKey == "dob").RmsRecordValueId;

			var saved = await _h.TypedValues.SaveDraftValuesAsync(Dept, "viewer", "rec-1", version, new List<RecordValueInput>
			{
				Value("main", "site", "Depot 5"), Value("crew", "name", "Pat", "r1", 0), Value("crew", "name", "Sam", "r2", 1)
			});
			_h.Defs.Values.Single(v => v.FieldKey == "ssn").RmsRecordValueId.Should().Be(sealedSsnId, "the sealed scalar keeps its identity so the envelope's row key still binds");
			_h.Defs.Values.Single(v => v.FieldKey == "dob").RmsRecordValueId.Should().Be(sealedDobId);
			_h.Defs.Values.Single(v => v.FieldKey == "dob").RmsRecordValueGroupId.Should().Be(_h.Defs.Groups.Single(g => g.ClientRowKey == "r1").RmsRecordValueGroupId, "the sealed cell follows its row by client row key");
			saved.Scalar("ssn").Withheld.Should().BeTrue(); saved.Scalar("ssn").Display.Should().Be(RecordTypedValuesService.Redacted);
			saved.Section("crew").Rows.Single(r => r.RowKey == "r1").Cell("dob").Withheld.Should().BeTrue();
			saved.Section("crew").Rows.Single(r => r.RowKey == "r2").Cell("dob").Display.Should().BeNull();
			saved.Scalar("site").Value.Should().Be("Depot 5");

			// A revealed editor posting a new value replaces the sealed row; a removed repeating row drops its sealed cell.
			await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput> { Value("main", "ssn", "987-65-4321"), Value("crew", "name", "Sam", "r2", 0) });
			_h.Defs.Values.Single(v => v.FieldKey == "ssn").RmsRecordValueId.Should().NotBe(sealedSsnId);
			_h.Defs.Values.Single(v => v.FieldKey == "ssn").TextValue.Should().Be("987-65-4321");
			_h.Defs.Values.Should().NotContain(v => v.FieldKey == "dob", "row r1 was removed");
			_h.TypedValues.ToSearchText(schema, await _h.TypedValues.HydrateAsync(Dept, "rec-1", null, version, true)).Should().NotContain("987", "protected values never enter the projection");
		}

		[Test]
		public async Task Rules_inside_a_repeating_section_evaluate_per_row_against_that_rows_cells()
		{
			var reason = Field("failure_reason", RmsFieldType.ShortText); reason.Rules.Add(ShowWhen("delivered", "false")); reason.Rules.Add(RequireWhen("delivered", "false"));
			var photo = Field("photo", RmsFieldType.ShortText); photo.Rules.Add(RequireWhen("high_value", "true"));
			var schema = Schema(Section("run", "Run", Field("high_value", RmsFieldType.Boolean)),
				Rows("stops", "Stops", null, null, Field("stop", RmsFieldType.ShortText, true), Field("delivered", RmsFieldType.Boolean), reason, photo));
			var version = DetachedVersion("rows", schema);
			var set = await _h.TypedValues.SaveDraftValuesAsync(Dept, Author, "rec-1", version, new List<RecordValueInput>
			{
				Value("run", "high_value", "true"),
				Value("stops", "stop", "Gate", "a", 0), Value("stops", "delivered", "true", "a", 0),
				Value("stops", "stop", "Yard", "b", 1), Value("stops", "delivered", "false", "b", 1)
			});

			var evaluation = _h.TypedValues.EvaluateRules(schema, set);
			evaluation.IsHidden("stops", "a", "failure_reason").Should().BeTrue("row a was delivered");
			evaluation.IsHidden("stops", "b", "failure_reason").Should().BeFalse("row b was not");
			evaluation.IsRequired("stops", "b", "failure_reason").Should().BeTrue();
			evaluation.IsRequired("stops", "a", "failure_reason").Should().BeFalse("a hidden field is never required");
			evaluation.IsRequired("stops", "a", "photo").Should().BeTrue("a per-row rule may still look at a scalar");
			evaluation.IsRequired("stops", "b", "photo").Should().BeTrue();
			evaluation.HiddenFieldKeys.Should().NotContain("failure_reason", "per-row outcomes never collapse onto the field");

			var finalize = await _h.TypedValues.ValidateAsync(Dept, version, set.ToInputs(), true);
			finalize.Issues.Should().Contain(i => i.FieldKey == "failure_reason" && i.RowKey == "b");
			finalize.Issues.Should().NotContain(i => i.FieldKey == "failure_reason" && i.RowKey == "a");
			finalize.Issues.Count(i => i.FieldKey == "photo").Should().Be(2);

			var inputs = set.ToInputs();
			inputs.Add(Value("stops", "failure_reason", "Closed", "b", 1)); inputs.Add(Value("stops", "photo", "p1", "a", 0)); inputs.Add(Value("stops", "photo", "p2", "b", 1));
			(await _h.TypedValues.ValidateAsync(Dept, version, inputs, true)).IsValid.Should().BeTrue();
		}

	}
}
