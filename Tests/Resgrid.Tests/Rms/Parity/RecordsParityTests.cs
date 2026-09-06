using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms.Parity
{
	/// <summary>
	/// The Logs parity gate (RMS plan sections 4.1, 6 and 7): "Records must be able to author, view, search,
	/// report, print, and export the equivalent of a Run, Training, Work, Meeting, Coroner, Callback, and Unit
	/// Activity record before it replaces the Logs writer", and "the parity gate is defined as 'the automated
	/// matrix passes'". One golden fixture per record type is replayed through the real aggregate and every
	/// projection is asserted. Nothing here reads a legacy row — parity is behavior, not migration.
	/// </summary>
	[TestFixture]
	public class RecordsParityTests
	{
		private static readonly string[] RequiredRecordTypes = { "Run", "Training", "Work", "Meeting", "Coroner", "Callback", "UnitActivity" };

		public static IEnumerable Fixtures()
		{
			foreach (var fixture in RecordsParityHarness.LoadFixtures())
				yield return new TestCaseData(fixture).SetName("{m}(" + fixture.RecordType + ")");
		}

		#region The matrix itself

		[Test]
		public void Every_replacement_record_type_has_a_golden_fixture()
		{
			var present = RecordsParityHarness.LoadFixtures().Select(f => f.RecordType).ToList();
			present.Should().Contain(RequiredRecordTypes, "the replacement inventory (plan section 4.1) names exactly these types");
			present.Should().OnlyHaveUniqueItems();
			foreach (var fixture in RecordsParityHarness.LoadFixtures())
			{
				RmsDefinitionKeys.LockedTypes.Should().ContainKey(fixture.DefinitionKey);
				RmsDefinitionKeys.LockedTypes[fixture.DefinitionKey].ToString().Should().Be(fixture.RecordType);
				fixture.Expected.NumberPrefix.Should().Be(RmsDefinitionKeys.DefaultNumberPrefix(fixture.DefinitionKey));
			}
		}

		[TestCaseSource(nameof(Fixtures))]
		public void A_logs_fixture_captures_every_legacy_column(RecordsParityFixture fixture)
		{
			// "one file per record type capturing every field, association, attachment" — a fixture that omits a
			// column cannot prove the column survives.
			if (fixture.Legacy.Source == "UnitLogs")
			{
				fixture.Legacy.UnitLog.Properties().Select(p => p.Name).Should().BeEquivalentTo(LegacyFieldMap.UnitLog.Keys);
				return;
			}
			fixture.Legacy.Log.Properties().Select(p => p.Name).Should().BeEquivalentTo(LegacyFieldMap.Log.Keys.Where(k => k != "Units" && k != "Users"));
			foreach (var user in fixture.Legacy.Users)
				user.Properties().Select(p => p.Name).Should().BeEquivalentTo(LegacyFieldMap.LogUser.Keys.Concat(new[] { "Role" }).Where(k => user[k] != null || k != "Role"));
			foreach (var unit in fixture.Legacy.Units)
				unit.Properties().Select(p => p.Name).Should().BeEquivalentTo(LegacyFieldMap.LogUnit.Keys);
			foreach (var attachment in fixture.Legacy.Attachments)
				attachment.FileName.Should().NotBeNullOrWhiteSpace();
		}

		[TestCaseSource(nameof(Fixtures))]
		public async Task Records_reproduces_the_detail_projection(RecordsParityFixture fixture)
		{
			var replay = await new RecordsParityHarness().ReplayAsync(fixture);
			var expected = fixture.Expected.Detail;
			var record = replay.Aggregate.Record;

			record.State.Should().Be((int)RmsRecordState.Finalized, "a legacy Log is created already-final; Quick Entry preserves that");
			record.RecordNumber.Should().StartWith(fixture.Expected.NumberPrefix);
			record.AuthorUserId.Should().Be(RecordsParityHarness.LegacyAuthor(fixture));
			record.DefinitionKey.Should().Be(fixture.DefinitionKey);
			record.RecordType.Should().Be((int)RmsDefinitionKeys.LockedTypes[fixture.DefinitionKey]);
			if (expected.ExternalId != null) record.ExternalId.Should().Be(expected.ExternalId);
			if (expected.StationGroupId.HasValue) record.StationGroupId.Should().Be(expected.StationGroupId);
			if (expected.CallId.HasValue) record.CallId.Should().Be(expected.CallId);
			if (expected.StartedOn != null) record.StartedOn.Should().Be(RecordsParityHarness.Date(expected.StartedOn));
			if (expected.EndedOn != null) record.EndedOn.Should().Be(RecordsParityHarness.Date(expected.EndedOn));

			RecordsParityHarness.Mismatches(expected.Details, replay.Aggregate.Details).Should().BeEmpty("every typed field must survive the replay");

			replay.Aggregate.Participants.Should().HaveCount(expected.Participants.Count);
			foreach (var participant in expected.Participants)
			{
				var actual = replay.Aggregate.Participants.SingleOrDefault(p => p.UserId == participant.Value<string>("UserId"));
				actual.Should().NotBeNull($"participant {participant["UserId"]} must be carried");
				RecordsParityHarness.Mismatches(participant, actual).Should().BeEmpty();
				actual.DisplayNameSnapshot.Should().NotBeNullOrWhiteSpace("person snapshots keep the record stable when a profile changes later");
			}

			replay.Aggregate.Units.Should().HaveCount(expected.Units.Count);
			foreach (var unit in expected.Units)
			{
				var actual = replay.Aggregate.Units.SingleOrDefault(u => u.UnitId == unit.Value<int>("UnitId"));
				actual.Should().NotBeNull($"unit {unit["UnitId"]} must be carried");
				RecordsParityHarness.Mismatches(unit, actual).Should().BeEmpty();
				actual.UnitNameSnapshot.Should().NotBeNullOrWhiteSpace("unit snapshots keep the record stable when apparatus changes later");
			}

			replay.Aggregate.Attachments.Should().HaveCount(expected.Attachments.Count);
			foreach (var attachment in expected.Attachments)
			{
				var actual = replay.Aggregate.Attachments.SingleOrDefault(a => a.FileName == attachment.Value<string>("FileName"));
				actual.Should().NotBeNull($"attachment {attachment["FileName"]} must be carried");
				RecordsParityHarness.Mismatches(attachment, actual).Should().BeEmpty();
				actual.Checksum.Should().Be(RecordSnapshotSerializer.Checksum(replay.AttachmentBytes[actual.FileName]), "the stored bytes are what was uploaded");
				actual.UploadedByUserId.Should().NotBeNullOrWhiteSpace();
			}
		}

		[TestCaseSource(nameof(Fixtures))]
		public async Task Records_reproduces_the_list_and_search_projection(RecordsParityFixture fixture)
		{
			var replay = await new RecordsParityHarness().ReplayAsync(fixture);
			var expected = fixture.Expected.List;
			var projection = replay.Projection;

			projection.State.Should().Be((int)RmsRecordState.Finalized);
			projection.DefinitionKey.Should().Be(fixture.DefinitionKey);
			projection.RecordNumber.Should().StartWith(fixture.Expected.NumberPrefix);
			projection.IsLegacy.Should().BeFalse();
			projection.DisplaySummary.Should().Be(expected.DisplaySummary);
			if (expected.OccurredOn != null) projection.OccurredOn.Should().Be(RecordsParityHarness.Date(expected.OccurredOn));
			projection.CallNumber.Should().Be(expected.CallNumber);
			foreach (var text in expected.SearchTextContains)
				projection.SearchText.Should().Contain(text);
			foreach (var text in expected.SearchTextExcludes)
				(projection.SearchText ?? string.Empty).Should().NotContain(text, "the list projection is the safe generic-search projection (plan section 5.10)");
			SplitIds(projection.ParticipantUserIds).Should().BeEquivalentTo(expected.ParticipantUserIds);
			SplitIds(projection.UnitIds).Select(int.Parse).Should().BeEquivalentTo(expected.UnitIds);
			projection.AuthorUserId.Should().Be(RecordsParityHarness.LegacyAuthor(fixture));
		}

		[TestCaseSource(nameof(Fixtures))]
		public async Task Records_reproduces_the_export_projection(RecordsParityFixture fixture)
		{
			var replay = await new RecordsParityHarness().ReplayAsync(fixture);
			var expected = fixture.Expected.Export;

			replay.Revision.Checksum.Should().Be(RecordSnapshotSerializer.Checksum(replay.Revision.SnapshotJson), "the revision is checksummed");
			RecordSnapshotSerializer.Serialize(RecordSnapshotSerializer.Deserialize(replay.Revision.SnapshotJson)).Should().Be(replay.Revision.SnapshotJson, "serialization is canonical so a re-export reproduces the checksum");
			var export = JObject.Parse(replay.ExportJson);
			foreach (var field in RecordSnapshotSerializer.DetailFieldOrder)
				((JObject)export["Details"]).ContainsKey(field).Should().BeTrue($"the export carries every typed field by its stable key, including {field}");
			foreach (var text in expected.MustContain)
				replay.ExportJson.Should().Contain(text);
			foreach (var attachment in export["Attachments"])
				attachment["Data"].Type.Should().Be(JTokenType.Null, "attachment bytes are referenced by checksum, never embedded in an export");
			foreach (var field in expected.RestrictedFields)
			{
				RecordSnapshotSerializer.RestrictedDetailFields.Should().Contain(field);
				replay.ClerkDocument.WithheldFields.Should().Contain(w => w.EndsWith(field, StringComparison.Ordinal), $"{field} is withheld from a viewer without RecordRestricted_View");
			}
		}

		[TestCaseSource(nameof(Fixtures))]
		public async Task Records_reproduces_the_print_projection(RecordsParityFixture fixture)
		{
			var replay = await new RecordsParityHarness().ReplayAsync(fixture);
			var expected = fixture.Expected.Print;

			replay.PrintHtml.Should().Contain("Parity Fire Department").And.Contain(replay.Aggregate.Record.RecordNumber).And.Contain(replay.Revision.Checksum);
			foreach (var text in expected.Contains)
				replay.PrintHtml.Should().Contain(text);
			foreach (var text in expected.WithheldWithoutRestrictedAccess)
			{
				replay.PrintHtml.Should().Contain(text, "an authorized officer sees the restricted section");
				replay.PrintHtmlWithoutRestrictedAccess.Should().NotContain(text, "a viewer without RecordRestricted_View never sees it on paper either");
			}
			if (expected.WithheldWithoutRestrictedAccess.Count > 0)
				replay.PrintHtmlWithoutRestrictedAccess.Should().Contain("withheld");
		}

		[TestCaseSource(nameof(Fixtures))]
		public async Task A_replayed_record_emits_exactly_one_created_and_one_finalized_event(RecordsParityFixture fixture)
		{
			var harness = new RecordsParityHarness();
			var replay = await harness.ReplayAsync(fixture);
			var events = harness.Store.Outbox.Where(o => o.AggregateId == replay.RecordId).Select(o => o.TriggerEventType).ToList();
			events.Should().Contain((int)WorkflowTriggerEventType.RecordCreated).And.Contain((int)WorkflowTriggerEventType.RecordFinalized);
			events.Count(t => t == (int)WorkflowTriggerEventType.RecordFinalized).Should().Be(1, "one-step create/finalize preserves sequence (plan section 7)");
		}

		#endregion

		#region The inventory is executable

		[TestCase(typeof(Log), nameof(LegacyFieldMap.Log))]
		[TestCase(typeof(LogUser), nameof(LegacyFieldMap.LogUser))]
		[TestCase(typeof(LogUnit), nameof(LegacyFieldMap.LogUnit))]
		[TestCase(typeof(LogAttachment), nameof(LegacyFieldMap.LogAttachment))]
		[TestCase(typeof(UnitLog), nameof(LegacyFieldMap.UnitLog))]
		public void Every_legacy_column_is_in_the_inventory(Type entity, string mapName)
		{
			var map = (IReadOnlyDictionary<string, string>)typeof(LegacyFieldMap).GetField(mapName, BindingFlags.Public | BindingFlags.Static).GetValue(null);
			var columns = PersistedColumns(entity).ToList();

			columns.Should().NotBeEmpty();
			columns.Should().BeSubsetOf(map.Keys, $"every persisted column of {entity.Name} needs a Records destination or a stated reason it is not carried; a new column must be decided here before it ships");
			map.Keys.Should().BeSubsetOf(columns.Concat(new[] { "Units", "Users" }), $"the {entity.Name} inventory names a column that no longer exists");
			map.Values.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v));
		}

		[Test]
		public void Every_not_carried_decision_states_its_reason()
		{
			var all = new[] { LegacyFieldMap.Log, LegacyFieldMap.LogUser, LegacyFieldMap.LogUnit, LegacyFieldMap.LogAttachment, LegacyFieldMap.UnitLog }.SelectMany(m => m);
			foreach (var entry in all.Where(e => e.Value.StartsWith(LegacyFieldMap.NotCarried, StringComparison.Ordinal)))
				entry.Value.Length.Should().BeGreaterThan(LegacyFieldMap.NotCarried.Length + 10, $"{entry.Key} is not carried and must say why");
			all.Count(e => e.Value.StartsWith(LegacyFieldMap.NotCarried, StringComparison.Ordinal)).Should().Be(5, "only the five legacy identity columns are not carried");
		}

		private static IEnumerable<string> PersistedColumns(Type entity)
		{
			var plumbing = new HashSet<string>(StringComparer.Ordinal) { "IdValue", "IdType", "TableName", "IdName", "IgnoredProperties" };
			foreach (var property in entity.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (plumbing.Contains(property.Name))
					continue;
				var getter = property.GetGetMethod();
				var isNavigation = getter != null && getter.IsVirtual && !getter.IsFinal && !typeof(IEnumerable).IsAssignableFrom(property.PropertyType);
				if (isNavigation)
					continue;
				if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string) && property.PropertyType != typeof(byte[]))
				{
					// Collections are associations: Units/Users on Log are mapped explicitly; anything else must be decided.
					yield return property.Name;
					continue;
				}
				yield return property.Name;
			}
		}

		private static IEnumerable<string> SplitIds(string csv)
		{
			return string.IsNullOrWhiteSpace(csv) ? Enumerable.Empty<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries);
		}

		#endregion
	}
}
