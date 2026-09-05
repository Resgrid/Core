using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>Report activity feed: legacy Logs and finalized Records in one shape, records under group scope.</summary>
	[TestFixture]
	public class RecordsReportingServiceTests
	{
		private const int Dept = 5;
		private static readonly DateTime Start = new DateTime(2026, 1, 1);
		private static readonly DateTime End = new DateTime(2026, 12, 31);

		private Mock<IWorkLogsService> _legacy;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<IRmsOperationalRecordDetailsRepository> _details;
		private Mock<IRmsRecordParticipantsRepository> _participants;
		private Mock<IRmsRecordUnitResponsesRepository> _units;
		private Mock<IRmsRecordGroupScopesRepository> _scopes;
		private Mock<IRecordsAuthorizationService> _authorization;
		private RecordsReportingService _service;
		private Mock<IRmsRevisionsRepository> _revisions;
		private readonly Dictionary<string, RmsRevision> _finalized = new Dictionary<string, RmsRevision>();

		[SetUp]
		public void SetUp()
		{
			_legacy = new Mock<IWorkLogsService>();
			_legacy.Setup(l => l.GetAllLogsByDepartmentDateRangeAsync(Dept, LogTypes.Training, Start, End)).ReturnsAsync(new List<Log>
			{
				new Log
				{
					LogId = 77, DepartmentId = Dept, LogType = (int)LogTypes.Training, Course = "Ropes", LoggedByUserId = "chief", LoggedOn = new DateTime(2026, 2, 1),
					StartedOn = new DateTime(2026, 2, 1, 8, 0, 0), EndedOn = new DateTime(2026, 2, 1, 10, 0, 0),
					Users = new List<LogUser> { new LogUser { UserId = "u1", UnitId = 3 } }, Units = new List<LogUnit> { new LogUnit { UnitId = 3, Dispatched = new DateTime(2026, 2, 1, 8, 0, 0), InQuarters = new DateTime(2026, 2, 1, 10, 0, 0) } }
				}
			});

			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });

			_records = new Mock<IRmsOperationalRecordsRepository>();
			_records.Setup(r => r.GetByDefinitionAndStartedRangeAsync(Dept, RmsDefinitionKeys.Training, It.IsAny<IEnumerable<int>>(), Start, End)).ReturnsAsync(new[]
			{
				Record("rec-1", "author", new DateTime(2026, 3, 1, 9, 0, 0)),
				Record("rec-2", "other", new DateTime(2026, 4, 1, 9, 0, 0))
			});

			_details = new Mock<IRmsOperationalRecordDetailsRepository>();
			_details.Setup(d => d.GetDraftsForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new[]
			{
				new RmsOperationalRecordDetail { RecordId = "rec-1", Course = "Pump Ops", CallNumber = "C-9", CallName = "Drill" },
				new RmsOperationalRecordDetail { RecordId = "rec-2", Course = "Ladders" }
			});
			_participants = new Mock<IRmsRecordParticipantsRepository>();
			_participants.Setup(p => p.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new[]
			{
				new RmsRecordParticipant { RecordId = "rec-1", UserId = "u1" },
				new RmsRecordParticipant { RecordId = "rec-1", UserId = "u2" },
				new RmsRecordParticipant { RecordId = "rec-2", UserId = "u3" }
			});
			_units = new Mock<IRmsRecordUnitResponsesRepository>();
			_units.Setup(u => u.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new[]
			{
				new RmsRecordUnitResponse { RecordId = "rec-1", UnitId = 3, Dispatched = new DateTime(2026, 3, 1, 9, 0, 0), InQuarters = new DateTime(2026, 3, 1, 11, 0, 0) }
			});
			_scopes = new Mock<IRmsRecordGroupScopesRepository>();
			_scopes.Setup(s => s.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new[]
			{
				new RmsRecordGroupScope { RecordId = "rec-1", DepartmentGroupId = 11 },
				new RmsRecordGroupScope { RecordId = "rec-2", DepartmentGroupId = 12 }
			});

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Dept)).ReturnsAsync((List<int>)null);

			_finalized.Clear();
			foreach (var record in _records.Object.GetByDefinitionAndStartedRangeAsync(Dept, RmsDefinitionKeys.Training, new[] { (int)RmsRecordState.Finalized }, Start, End).GetAwaiter().GetResult())
			{
				var detail = _details.Object.GetDraftsForRecordsAsync(Dept, new[] { record.RmsOperationalRecordId }).GetAwaiter().GetResult().First(d => d.RecordId == record.RmsOperationalRecordId);
				var participants = _participants.Object.GetForRecordsAsync(Dept, new[] { record.RmsOperationalRecordId }).GetAwaiter().GetResult().Where(p => p.RecordId == record.RmsOperationalRecordId).ToList();
				var units = _units.Object.GetForRecordsAsync(Dept, new[] { record.RmsOperationalRecordId }).GetAwaiter().GetResult().Where(u => u.RecordId == record.RmsOperationalRecordId).ToList();
				AddRevision(record, detail, participants, units);
			}
			_records.Invocations.Clear();
			_revisions = new Mock<IRmsRevisionsRepository>();
			_revisions.Setup(r => r.GetByIdsForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => _finalized.Values.Where(r => ids.Contains(r.RmsRevisionId)).ToList());
			_service = new RecordsReportingService(_legacy.Object, _cutover.Object, _records.Object, _revisions.Object, _scopes.Object, _authorization.Object);
		}

		[Test]
		public async Task Merges_legacy_logs_and_finalized_records_into_one_ordered_shape()
		{
			var entries = await _service.GetActivityAsync(Dept, "viewer", RmsOperationalRecordType.Training, Start, End);

			entries.Select(e => e.SourceId).Should().Equal("77", "rec-1", "rec-2");
			entries.Select(e => e.Source).Should().Equal(ReportActivitySources.LegacyLog, ReportActivitySources.Record, ReportActivitySources.Record);

			var legacy = entries[0];
			legacy.Course.Should().Be("Ropes");
			legacy.Participants.Single().Should().BeEquivalentTo(new ReportActivityParticipant { UserId = "u1", UnitId = 3 });
			legacy.Units.Single().InQuarters.Should().Be(new DateTime(2026, 2, 1, 10, 0, 0));

			var record = entries[1];
			record.Type.Should().Be(RmsOperationalRecordType.Training);
			record.Course.Should().Be("Pump Ops");
			record.CallNumber.Should().Be("C-9");
			record.CallName.Should().Be("Drill");
			record.LoggedByUserId.Should().Be("author");
			record.LoggedOn.Should().Be(new DateTime(2026, 3, 2));
			record.EndedOn.Should().Be(new DateTime(2026, 3, 1, 11, 0, 0));
			record.Participants.Select(p => p.UserId).Should().Equal("u1", "u2");
			record.Units.Single().UnitId.Should().Be(3);
		}

		[Test]
		public async Task Records_outside_the_viewers_group_scope_are_withheld_unless_always_visible()
		{
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("viewer", Dept)).ReturnsAsync(new List<int> { 11 });
			(await _service.GetActivityAsync(Dept, "viewer", RmsOperationalRecordType.Training, Start, End)).Select(e => e.SourceId).Should().Equal("77", "rec-1");

			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("u3", Dept)).ReturnsAsync(new List<int>());
			(await _service.GetActivityAsync(Dept, "u3", RmsOperationalRecordType.Training, Start, End)).Select(e => e.SourceId).Should().Equal(new[] { "77", "rec-2" }, "a participant always sees the record");

			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("other", Dept)).ReturnsAsync(new List<int>());
			(await _service.GetActivityAsync(Dept, "other", RmsOperationalRecordType.Training, Start, End)).Select(e => e.SourceId).Should().Equal(new[] { "77", "rec-2" }, "the author always sees the record");
		}

		[Test]
		public async Task Records_are_left_out_while_the_flag_is_off_and_legacy_is_skipped_for_unit_activity()
		{
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = false });
			(await _service.GetActivityAsync(Dept, "viewer", RmsOperationalRecordType.Training, Start, End)).Select(e => e.SourceId).Should().Equal("77");
			_records.Verify(r => r.GetByDefinitionAndStartedRangeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);

			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true });
			_records.Setup(r => r.GetByDefinitionAndStartedRangeAsync(Dept, RmsDefinitionKeys.UnitActivity, It.IsAny<IEnumerable<int>>(), Start, End)).ReturnsAsync(new RmsOperationalRecord[0]);
			await _service.GetActivityAsync(Dept, "viewer", RmsOperationalRecordType.UnitActivity, Start, End);
			_legacy.Verify(l => l.GetAllLogsByDepartmentDateRangeAsync(It.IsAny<int>(), It.IsAny<LogTypes>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Once, "unit activity never lived in the Logs table");
		}

		[Test]
		public async Task Call_activity_merges_legacy_call_logs_with_finalized_records_on_the_call()
		{
			_legacy.Setup(l => l.GetLogsForCallAsync(9)).ReturnsAsync(new List<Log> { new Log { LogId = 5, DepartmentId = Dept, LogType = (int)LogTypes.Run, CallId = 9, Users = new List<LogUser> { new LogUser { UserId = "u1" } } } });
			var finalized = Record("rec-9", "author", new DateTime(2026, 6, 1));
			finalized.CallId = 9;
			AddRevision(finalized, null, new List<RmsRecordParticipant>(), new List<RmsRecordUnitResponse>());
			var draft = Record("rec-10", "author", new DateTime(2026, 6, 2));
			draft.State = (int)RmsRecordState.Draft;
			_records.Setup(r => r.GetByCallAsync(Dept, 9)).ReturnsAsync(new[] { finalized, draft });

			var entries = await _service.GetCallActivityAsync(Dept, "viewer", 9);

			entries.Select(e => e.SourceId).Should().BeEquivalentTo(new[] { "5", "rec-9" }, "drafts never count");
			entries.Single(e => e.SourceId == "rec-9").Participants.Select(p => p.UserId).Should().BeEmpty("no participant rows were returned for rec-9");
		}

		[Test]
		public void Only_the_finalized_family_is_requested()
		{
			IEnumerable<int> requested = null;
			_records.Setup(r => r.GetByDefinitionAndStartedRangeAsync(Dept, RmsDefinitionKeys.Training, It.IsAny<IEnumerable<int>>(), Start, End))
				.Callback<int, string, IEnumerable<int>, DateTime, DateTime>((d, k, states, s, e) => requested = states).ReturnsAsync(new RmsOperationalRecord[0]);

			_service.GetActivityAsync(Dept, "viewer", RmsOperationalRecordType.Training, Start, End).GetAwaiter().GetResult();

			requested.Should().BeEquivalentTo(new[] { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended });
		}

		[Test]
		public async Task An_open_amendment_cannot_move_official_dates_hours_call_or_participant_assignments()
		{
			var record = Record("rec-1", "author", new DateTime(2026, 3, 1, 9, 0, 0));
			record.CallId = 9;
			AddRevision(record, new RmsOperationalRecordDetail { Course = "Approved course" },
				new List<RmsRecordParticipant> { new RmsRecordParticipant { UserId = "u1", UnitId = 3 } }, new List<RmsRecordUnitResponse>());
			_records.Setup(r => r.GetByDefinitionAndStartedRangeAsync(Dept, RmsDefinitionKeys.Training, It.IsAny<IEnumerable<int>>(), Start, End)).ReturnsAsync(new[] { record });
			_records.Setup(r => r.GetByCallAsync(Dept, 9)).ReturnsAsync(new[] { record });
			var before = (await _service.GetActivityAsync(Dept, "author", RmsOperationalRecordType.Training, Start, End)).Single(e => e.SourceId == "rec-1");
			record.AmendsRevisionId = record.CurrentRevisionId;
			record.StartedOn = Start.AddYears(3);
			record.EndedOn = record.StartedOn.Value.AddHours(18);
			record.CallId = 10;
			var during = (await _service.GetActivityAsync(Dept, "author", RmsOperationalRecordType.Training, Start, End)).Single(e => e.SourceId == "rec-1");
			during.Should().BeEquivalentTo(before);
			during.Participants.Single().UnitId.Should().Be(3);
			(await _service.GetCallActivityAsync(Dept, "author", 9)).Single(e => e.SourceId == "rec-1").CallId.Should().Be(9);
			record.AmendsRevisionId = null;
			(await _service.GetActivityAsync(Dept, "author", RmsOperationalRecordType.Training, Start, End)).Single(e => e.SourceId == "rec-1").Should().BeEquivalentTo(before);
		}

		private void AddRevision(RmsOperationalRecord record, RmsOperationalRecordDetail details, List<RmsRecordParticipant> participants, List<RmsRecordUnitResponse> units)
		{
			_finalized[record.CurrentRevisionId] = new RmsRevision
			{
				RmsRevisionId = record.CurrentRevisionId, RecordId = record.RmsOperationalRecordId, DepartmentId = Dept,
				CreatedOn = record.FinalizedOn.Value,
				SnapshotJson = RecordSnapshotSerializer.Serialize(RecordSnapshotSerializer.Build(new RecordAggregate { Record = record, Details = details, Participants = participants, Units = units }))
			};
			_finalized[record.CurrentRevisionId].Checksum = RecordSnapshotSerializer.Checksum(_finalized[record.CurrentRevisionId].SnapshotJson);
		}

		[Test]
		public async Task Changed_official_snapshot_cannot_silently_change_activity_totals()
		{
			var revision = _finalized["rec-1-final"];
			revision.SnapshotJson = revision.SnapshotJson.Replace("2026-03-01T11:00:00", "2026-03-01T23:00:00");
			revision.Checksum.Should().NotBe(RecordSnapshotSerializer.Checksum(revision.SnapshotJson));
			Func<Task> report = () => _service.GetActivityAsync(Dept, "author", RmsOperationalRecordType.Training, Start, End);
			await report.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integrity*");
		}

		private static RmsOperationalRecord Record(string id, string author, DateTime startedOn)
		{
			return new RmsOperationalRecord
			{
				RmsOperationalRecordId = id, DepartmentId = Dept, DefinitionKey = RmsDefinitionKeys.Training, RecordType = (int)RmsOperationalRecordType.Training,
				State = (int)RmsRecordState.Finalized, AuthorUserId = author, OwnerUserId = author, StartedOn = startedOn, EndedOn = startedOn.AddHours(2),
				CreatedOn = startedOn, FinalizedOn = startedOn.Date.AddDays(1), StationGroupId = 11, CurrentRevisionId = id + "-final"
			};
		}
	}
}
