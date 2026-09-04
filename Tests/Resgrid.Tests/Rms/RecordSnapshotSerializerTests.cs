using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>Revision snapshot, checksum and on-demand diff tests (RMS plan section 7, revision history and diff tests).</summary>
	[TestFixture]
	public class RecordSnapshotSerializerTests
	{
		private static RecordAggregate SampleAggregate()
		{
			var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
			return new RecordAggregate
			{
				Record = new RmsOperationalRecord { RmsOperationalRecordId = "r1", DepartmentId = 7, DefinitionKey = RmsDefinitionKeys.Training, DefinitionVersion = 1, RecordType = (int)RmsOperationalRecordType.Training, DraftReference = "D-AAAAA", AuthorUserId = "u1", StartedOn = now },
				Details = new RmsOperationalRecordDetail { Narrative = "Hose evolutions", Course = "Engine Ops", CourseCode = "ENG-101", CaseNumber = "C-1" },
				Participants = new List<RmsRecordParticipant> { new RmsRecordParticipant { UserId = "u2", Ordinal = 1 }, new RmsRecordParticipant { UserId = "u1", Ordinal = 0 } },
				Units = new List<RmsRecordUnitResponse> { new RmsRecordUnitResponse { UnitId = 3, OnScene = now } },
				Attachments = new List<RmsRecordAttachment> { new RmsRecordAttachment { RmsRecordAttachmentId = "a1", Checksum = "abc", Data = new byte[] { 1, 2, 3 }, UploadedOn = now } }
			};
		}

		[Test]
		public void Serialization_is_deterministic_and_checksums_match_across_instances()
		{
			var first = RecordSnapshotSerializer.Serialize(RecordSnapshotSerializer.Build(SampleAggregate()));
			var second = RecordSnapshotSerializer.Serialize(RecordSnapshotSerializer.Build(SampleAggregate()));

			first.Should().Be(second);
			RecordSnapshotSerializer.Checksum(first).Should().Be(RecordSnapshotSerializer.Checksum(second));
			RecordSnapshotSerializer.Checksum(first).Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
		}

		[Test]
		public void Snapshot_never_carries_attachment_bytes_and_orders_children_canonically()
		{
			var snapshot = RecordSnapshotSerializer.Build(SampleAggregate());

			snapshot.Attachments.Single().Data.Should().BeNull();
			snapshot.Participants.Select(p => p.UserId).Should().Equal("u1", "u2");
			RecordSnapshotSerializer.Serialize(snapshot).Should().NotContain("\"Data\":\"AQID\"");
		}

		[Test]
		public void Round_trip_preserves_every_field()
		{
			var snapshot = RecordSnapshotSerializer.Build(SampleAggregate());
			var restored = RecordSnapshotSerializer.Deserialize(RecordSnapshotSerializer.Serialize(snapshot));

			restored.Should().BeEquivalentTo(snapshot);
		}

		[Test]
		public void Diff_reports_changed_added_and_removed_items_and_nothing_else()
		{
			var from = RecordSnapshotSerializer.Build(SampleAggregate());
			var toAggregate = SampleAggregate();
			toAggregate.Details.Narrative = "Hose evolutions and ladders";
			toAggregate.Participants.Add(new RmsRecordParticipant { UserId = "u3", Ordinal = 2 });
			toAggregate.Units.Clear();
			var to = RecordSnapshotSerializer.Build(toAggregate);

			var diffs = RecordSnapshotSerializer.Diff(from, to, canViewRestricted: true);

			diffs.Should().HaveCount(3);
			diffs.Should().ContainSingle(d => d.Section == "Details" && d.FieldKey == "Narrative" && d.OldValue == "Hose evolutions" && d.NewValue == "Hose evolutions and ladders");
			diffs.Should().ContainSingle(d => d.Section == "Participants" && d.FieldKey == "added" && d.NewValue == "u3");
			diffs.Should().ContainSingle(d => d.Section == "Units" && d.FieldKey == "removed");
		}

		[Test]
		public void Diff_withholds_restricted_values_from_a_viewer_without_the_permission()
		{
			var from = RecordSnapshotSerializer.Build(SampleAggregate());
			var toAggregate = SampleAggregate();
			toAggregate.Details.CaseNumber = "C-2";
			var to = RecordSnapshotSerializer.Build(toAggregate);

			var withheld = RecordSnapshotSerializer.Diff(from, to, canViewRestricted: false).Single();
			withheld.FieldKey.Should().Be("CaseNumber");
			withheld.Withheld.Should().BeTrue();
			withheld.OldValue.Should().BeNull();
			withheld.NewValue.Should().BeNull();

			var revealed = RecordSnapshotSerializer.Diff(from, to, canViewRestricted: true).Single();
			revealed.Withheld.Should().BeFalse();
			revealed.OldValue.Should().Be("C-1");
			revealed.NewValue.Should().Be("C-2");
		}

		[Test]
		public void Identical_snapshots_produce_an_empty_diff()
		{
			var a = RecordSnapshotSerializer.Build(SampleAggregate());
			var b = RecordSnapshotSerializer.Build(SampleAggregate());
			RecordSnapshotSerializer.Diff(a, b, true).Should().BeEmpty();
		}
	}
}
