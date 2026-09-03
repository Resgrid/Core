using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
	/// <summary>
	/// Accountability view (RMS plan section 4.7): open, overdue and returned Records per owner, group or unit,
	/// time-to-finalize over the window, the queue's visibility rule applied, and a reminder that is bounded per
	/// Record per day and per action.
	/// </summary>
	[TestFixture]
	public class RecordsAccountabilityServiceTests
	{
		private const int Dept = 4;
		private static readonly DateTime Now = DateTime.UtcNow;

		private List<RmsOperationalRecord> _records;
		private List<RmsAccessAudit> _audits;
		private List<RmsRecordUnitResponse> _units;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<ICommunicationService> _communication;
		private Mock<IRecordsService> _recordsService;
		private RecordsAccountabilityService _service;

		private static RmsOperationalRecord Record(string id, RmsRecordState state, string owner, int? group = null, DateTime? created = null, DateTime? finalizedOn = null, DateTime? reviewDue = null)
		{
			return new RmsOperationalRecord
			{
				RmsOperationalRecordId = id, DepartmentId = Dept, State = (int)state, AuthorUserId = owner, OwnerUserId = owner, StationGroupId = group,
				RecordType = (int)RmsOperationalRecordType.Training, DraftReference = "D-" + id, RecordNumber = finalizedOn.HasValue ? "TRN-" + id : null,
				CreatedOn = created ?? Now.AddDays(-3), FinalizedOn = finalizedOn, ReviewDueOn = reviewDue, DisplaySummary = "Summary " + id
			};
		}

		[SetUp]
		public void SetUp()
		{
			_records = new List<RmsOperationalRecord>
			{
				Record("a1", RmsRecordState.Draft, "alice", 1, Now.AddDays(-10)),
				Record("a2", RmsRecordState.ReadyForReview, "alice", 1, Now.AddDays(-4), reviewDue: Now.AddHours(-5)),
				Record("b1", RmsRecordState.Returned, "bob", 2, Now.AddDays(-2)),
				Record("b2", RmsRecordState.Finalized, "bob", 2, Now.AddDays(-5), finalizedOn: Now.AddDays(-3)),
				Record("c1", RmsRecordState.Finalized, "carol", null, Now.AddDays(-2), finalizedOn: Now.AddDays(-1)),
				Record("old", RmsRecordState.Finalized, "carol", null, Now.AddDays(-90), finalizedOn: Now.AddDays(-80))
			};
			_audits = new List<RmsAccessAudit>();
			_units = new List<RmsRecordUnitResponse> { new RmsRecordUnitResponse { RecordId = "a1", UnitId = 7 }, new RmsRecordUnitResponse { RecordId = "a1", UnitId = 8 } };

			var records = new Mock<IRmsOperationalRecordsRepository>();
			records.Setup(r => r.GetOpenAsync(Dept)).ReturnsAsync(() => _records.Where(RecordsAccountabilityService.IsOpen).ToList());
			records.Setup(r => r.GetFinalizedSinceAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync((int d, DateTime since) => _records.Where(r => r.FinalizedOn.HasValue && r.FinalizedOn >= since).ToList());
			records.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _records.FirstOrDefault(r => r.RmsOperationalRecordId == id));

			var participants = new Mock<IRmsRecordParticipantsRepository>();
			participants.Setup(p => p.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsRecordParticipant>());
			var units = new Mock<IRmsRecordUnitResponsesRepository>();
			units.Setup(u => u.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(() => _units);
			var scopes = new Mock<IRmsRecordGroupScopesRepository>();
			scopes.Setup(s => s.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(() => _records.Where(r => r.StationGroupId.HasValue)
				.Select(r => new RmsRecordGroupScope { RecordId = r.RmsOperationalRecordId, DepartmentGroupId = r.StationGroupId.Value }).ToList());
			var values = new Mock<IRmsRecordValueService>();
			values.Setup(v => v.GetDraftsForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsOperationalRecordDetail> { new RmsOperationalRecordDetail { RecordId = "b1", UnitId = 9 } });
			var audits = new Mock<IRmsAccessAuditsRepository>();
			audits.Setup(a => a.GetForRecordAsync(Dept, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((int d, string id, int take) => _audits.Where(a => a.RecordId == id).ToList());

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Dept)).ReturnsAsync((List<int>)null);

			_recordsService = new Mock<IRecordsService>();
			_recordsService.Setup(s => s.RecordAccessAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsAccessAuditAction>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsOriginClient>()))
				.Returns((int d, string user, string recordId, string rev, RmsAccessAuditAction action, string purpose, string ip, RmsOriginClient origin) =>
				{
					_audits.Add(new RmsAccessAudit { RecordId = recordId, Action = (int)action, Purpose = purpose, ActorUserId = user, OccurredOn = DateTime.UtcNow });
					return Task.CompletedTask;
				});

			_communication = new Mock<ICommunicationService>();
			_communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.ReturnsAsync(true);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Springfield Fire" });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetTextToCallNumberForDepartmentAsync(Dept)).ReturnsAsync("5551234");
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id });

			_service = new RecordsAccountabilityService(records.Object, participants.Object, units.Object, scopes.Object, values.Object, audits.Object,
				_authorization.Object, _recordsService.Object, _communication.Object, departments.Object, settings.Object, profiles.Object);
		}

		[Test]
		public async Task Person_pivot_counts_open_overdue_returned_and_time_to_finalize_inside_the_window()
		{
			var report = await _service.BuildAsync(Dept, "chief", RecordsAccountabilityPivot.Person, 30);

			report.Rows.Select(r => r.Key).Should().BeEquivalentTo(new[] { "alice", "bob", "carol" });

			var alice = report.Rows.Single(r => r.Key == "alice");
			alice.Open.Should().Be(2);
			alice.OverdueReviews.Should().Be(1, "a2 is ReadyForReview past its due time");
			alice.ReturnedNotCorrected.Should().Be(0);
			alice.OldestOpenOn.Should().Be(_records.Single(r => r.RmsOperationalRecordId == "a1").CreatedOn);
			alice.OpenRecords.Select(r => r.RecordId).Should().Equal(new[] { "a1", "a2" });

			var bob = report.Rows.Single(r => r.Key == "bob");
			bob.Open.Should().Be(1);
			bob.ReturnedNotCorrected.Should().Be(1);
			bob.FinalizedInWindow.Should().Be(1);
			bob.AverageHoursToFinalize.Should().Be(48.0);

			var carol = report.Rows.Single(r => r.Key == "carol");
			carol.FinalizedInWindow.Should().Be(1, "the record finalized 80 days ago is outside a 30-day window");
			carol.Open.Should().Be(0);

			report.Totals.Open.Should().Be(3);
			report.Totals.OverdueReviews.Should().Be(1);
			report.Totals.ReturnedNotCorrected.Should().Be(1);
			report.Totals.FinalizedInWindow.Should().Be(2);
			report.Rows.First().Key.Should().BeOneOf("alice", "bob", "the rows with problems sort first");
		}

		[Test]
		public async Task Group_and_unit_pivots_key_by_station_and_by_every_responding_unit()
		{
			var byGroup = await _service.BuildAsync(Dept, "chief", RecordsAccountabilityPivot.Group, 30);
			byGroup.Rows.Select(r => r.Key).Should().BeEquivalentTo(new[] { "1", "2", RecordsAccountabilityService.UnassignedKey });
			byGroup.Rows.Single(r => r.Key == "1").Open.Should().Be(2);

			var byUnit = await _service.BuildAsync(Dept, "chief", RecordsAccountabilityPivot.Unit, 30);
			byUnit.Rows.Single(r => r.Key == "7").OpenRecords.Select(r => r.RecordId).Should().Equal(new[] { "a1" });
			byUnit.Rows.Single(r => r.Key == "8").Open.Should().Be(1, "a1 had two responding units and counts under both");
			byUnit.Rows.Single(r => r.Key == "9").OpenRecords.Select(r => r.RecordId).Should().Equal(new[] { "b1" }, "the unit on the detail row counts too");
			byUnit.Rows.Single(r => r.Key == RecordsAccountabilityService.UnassignedKey).Open.Should().Be(1, "a2 has no unit");
		}

		[Test]
		public async Task Rows_follow_the_queue_visibility_rule_for_a_group_scoped_viewer()
		{
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("station1", Dept)).ReturnsAsync(new List<int> { 1 });

			var report = await _service.BuildAsync(Dept, "station1", RecordsAccountabilityPivot.Person, 30);

			report.Rows.Select(r => r.Key).Should().Equal(new[] { "alice" }, "only station 1 records are anchored to a visible group");
			report.Totals.Open.Should().Be(2);
		}

		[Test]
		public async Task A_reminder_goes_to_the_owner_is_audited_and_is_not_repeated_within_a_day()
		{
			var first = await _service.SendReminderAsync(Dept, "chief", "b1");

			first.Sent.Should().BeTrue();
			first.RecipientUserId.Should().Be("bob");
			_communication.Verify(c => c.SendNotificationAsync("bob", Dept, It.Is<string>(m => m.Contains("D-b1") && m.Contains("returned for correction") && m.Contains("/User/Records/Details/b1")),
				"5551234", It.IsAny<Department>(), RecordsAccountabilityService.ReminderTitle, It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			_audits.Should().ContainSingle(a => a.RecordId == "b1" && a.Purpose == RecordsAccountabilityService.ReminderPurposePrefix + "bob");

			var second = await _service.SendReminderAsync(Dept, "chief", "b1");

			second.Sent.Should().BeFalse();
			second.Reason.Should().Be(RecordsReminderResult.ReasonRecentlyReminded);
			_communication.Verify(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Reminders_refuse_closed_records_and_cap_the_batch()
		{
			(await _service.SendReminderAsync(Dept, "chief", "b2")).Reason.Should().Be(RecordsReminderResult.ReasonNotOpen);

			for (var i = 0; i < 30; i++)
				_records.Add(Record("bulk" + i, RmsRecordState.Draft, "dave", 3, Now.AddDays(-1)));

			var results = await _service.SendRemindersAsync(Dept, "chief", _records.Where(r => r.RmsOperationalRecordId.StartsWith("bulk")).Select(r => r.RmsOperationalRecordId));

			results.Count(r => r.Sent).Should().Be(_service.MaxRemindersPerAction);
			results.Count(r => r.Reason == RecordsReminderResult.ReasonLimit).Should().Be(30 - _service.MaxRemindersPerAction);
		}

		[Test]
		public void The_reminder_message_carries_the_header_and_the_link_only()
		{
			var record = Record("x", RmsRecordState.ReadyForReview, "alice", created: Now.AddDays(-6), reviewDue: Now.AddHours(-1));
			record.DisplaySummary = "Secret narrative details";

			var message = RecordsAccountabilityService.BuildReminderMessage(record, Now);

			message.Should().StartWith("Training record D-x is still ReadyForReview after 6 days.");
			message.Should().Contain("review is overdue");
			message.Should().Contain("/User/Records/Details/x");
			message.Should().NotContain("Secret");
		}
	}
}
