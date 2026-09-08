using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Per-app Field Records rollout telemetry and its dashboard (RMS plan RMS-1D). What is under test is that a
	/// client cannot report itself into someone else's numbers, that only coded outcomes are stored, and that the
	/// dashboard is department-administration only.
	/// </summary>
	[TestFixture]
	public class RecordsFieldRolloutServiceTests
	{
		private const int Dept = 9;
		private const string Admin = "admin";
		private const string Member = "member";

		private List<RmsFieldRolloutEvent> _stored;
		private Mock<IRmsFieldRolloutEventsRepository> _events;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<IFeatureToggleService> _flags;
		private RecordsFieldRolloutService _service;
		private string _minimumResponder;

		[SetUp]
		public void SetUp()
		{
			_minimumResponder = RecordsFieldConfig.MinimumResponderVersion;
			_stored = new List<RmsFieldRolloutEvent>();
			_events = new Mock<IRmsFieldRolloutEventsRepository>();
			_events.Setup(e => e.InsertBatchAsync(It.IsAny<IEnumerable<RmsFieldRolloutEvent>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((IEnumerable<RmsFieldRolloutEvent> rows, CancellationToken c) => { _stored.AddRange(rows); return _stored.Count; });
			_events.Setup(e => e.GetForWindowAsync(Dept, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, DateTime since, int take, CancellationToken c) => _stored.Where(row => row.OccurredOn >= since).ToList());

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.IsDepartmentAdminAsync(Admin, Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.IsDepartmentAdminAsync(Member, Dept)).ReturnsAsync(false);

			_records = new Mock<IRmsOperationalRecordsRepository>();
			_records.Setup(r => r.GetCreatedSinceAsync(Dept, It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<RmsOperationalRecord>());
			_records.Setup(r => r.GetFinalizedSinceAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(new List<RmsOperationalRecord>());

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_service = new RecordsFieldRolloutService(_events.Object, _authorization.Object, _records.Object, _flags.Object);
		}

		[TearDown]
		public void TearDown() => RecordsFieldConfig.MinimumResponderVersion = _minimumResponder;

		private static RecordFieldRolloutBatch Batch(params RecordFieldRolloutInput[] events) => new RecordFieldRolloutBatch
		{
			OriginClient = RmsOriginClient.Responder,
			AppVersion = "5.4.0",
			ClientCapability = "records.v1c",
			Events = events.ToList()
		};

		[Test]
		public async Task Only_a_field_app_and_an_active_member_may_report_and_unknown_events_are_dropped()
		{
			var accepted = await _service.RecordBatchAsync(Dept, Member, Batch(
				new RecordFieldRolloutInput { EventType = RmsFieldRolloutEventTypes.Catalog, Outcome = "ok" },
				new RecordFieldRolloutInput { EventType = "exfiltrate", Outcome = "here is the narrative" }));

			accepted.Should().Be(1);
			_stored.Should().ContainSingle();
			_stored[0].UserId.Should().Be(Member, "the server stamps the member; a client never names one");
			_stored[0].DepartmentId.Should().Be(Dept);

			_stored.Clear();
			var web = Batch(new RecordFieldRolloutInput { EventType = RmsFieldRolloutEventTypes.Catalog });
			web.OriginClient = RmsOriginClient.Web;
			(await _service.RecordBatchAsync(Dept, Member, web)).Should().Be(0, "a non-field origin has no rollout to report");

			_authorization.Setup(a => a.IsActiveMemberAsync("stranger", Dept)).ReturnsAsync(false);
			(await _service.RecordBatchAsync(Dept, "stranger", Batch(new RecordFieldRolloutInput { EventType = RmsFieldRolloutEventTypes.Sync }))).Should().Be(0);
			_stored.Should().BeEmpty();
		}

		[Test]
		public async Task A_wrong_clock_and_an_oversized_batch_cannot_move_rows_outside_the_window()
		{
			var future = new RecordFieldRolloutInput { EventType = RmsFieldRolloutEventTypes.Sync, OccurredOn = DateTime.UtcNow.AddDays(30) };
			var ancient = new RecordFieldRolloutInput { EventType = RmsFieldRolloutEventTypes.Sync, OccurredOn = DateTime.UtcNow.AddYears(-5) };

			await _service.RecordBatchAsync(Dept, Member, Batch(future, ancient));

			_stored.Should().HaveCount(2);
			_stored.Should().OnlyContain(row => row.OccurredOn <= DateTime.UtcNow.AddMinutes(1) && row.OccurredOn >= DateTime.UtcNow.AddDays(-RecordsFieldRolloutService.MaxWindowDays).AddMinutes(-1));

			_stored.Clear();
			var oversized = Batch(Enumerable.Range(0, RecordFieldRolloutBatch.MaxEvents + 50).Select(_ => new RecordFieldRolloutInput { EventType = RmsFieldRolloutEventTypes.Sync }).ToArray());
			await _service.RecordBatchAsync(Dept, Member, oversized);
			_stored.Should().HaveCount(RecordFieldRolloutBatch.MaxEvents);
		}

		[Test]
		public async Task The_dashboard_is_department_administration_only()
		{
			Func<Task> denied = () => _service.GetAsync(Dept, Member);
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();

			var rollout = await _service.GetAsync(Dept, Admin);
			rollout.Apps.Select(app => app.OriginClient).Should().BeEquivalentTo(new[] { "Responder", "Unit", "IncidentCommand", "Dispatch" });
			rollout.WindowDays.Should().Be(30);
			rollout.AnyAppEnabled.Should().BeTrue();
		}

		[Test]
		public void Summarize_counts_outcomes_versions_and_the_median_time_to_complete()
		{
			var now = DateTime.UtcNow;
			var events = new List<RmsFieldRolloutEvent>
			{
				Event("a", "5.2.0", RmsFieldRolloutEventTypes.Catalog, "ok", now.AddHours(-5)),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.Catalog, "ok", now.AddHours(-1)),
				Event("b", "5.1.0", RmsFieldRolloutEventTypes.Catalog, "app_version_too_old", now.AddHours(-2)),
				Event("b", "5.1.0", RmsFieldRolloutEventTypes.Catalog, "app_version_too_old", now.AddHours(-2)),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.DraftStarted, "ok", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.DraftStarted, "ok", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.DraftSaved, "ok", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.DraftSaved, "etag", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.Conflict, "etag", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.Attachment, "ok", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.Attachment, "too_large", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.Abandoned, "ok", now),
				Event("a", "5.4.0", RmsFieldRolloutEventTypes.WebHandoff, "ok", now),
				Completed("a", "5.4.0", 60_000, now),
				Completed("a", "5.4.0", 180_000, now),
				Completed("a", "5.4.0", 240_000, now),
			};

			var summary = RecordsFieldRolloutService.Summarize(RmsOriginClient.Responder, events, "5.2.0");

			summary.ActiveUsers.Should().Be(2);
			summary.CompatibleUsers.Should().Be(1, "each person counts on the version they are actually running now");
			summary.CatalogRequests.Should().Be(4);
			summary.CatalogFailures.Should().Be(2);
			summary.CatalogFailureReasons["app_version_too_old"].Should().Be(2);
			summary.DraftsStarted.Should().Be(2);
			summary.DraftsSaved.Should().Be(1);
			summary.DraftSaveFailures.Should().Be(1);
			summary.Conflicts.Should().Be(1);
			summary.ConflictKinds["etag"].Should().Be(1);
			summary.AttachmentsUploaded.Should().Be(1);
			summary.AttachmentFailures.Should().Be(1);
			summary.Completed.Should().Be(3);
			summary.MedianTimeToCompleteMs.Should().Be(180_000);
			summary.Abandoned.Should().Be(1);
			summary.AbandonmentRate.Should().Be(0.5);
			summary.WebHandoffs.Should().Be(1);
			summary.Versions.Should().Contain(version => version.AppVersion == "5.4.0" && version.Users == 1);
		}

		[Test]
		public void Versions_are_listed_newest_first_by_version_rather_than_by_text()
		{
			var now = DateTime.UtcNow;
			var events = new List<RmsFieldRolloutEvent>
			{
				Event("a", "5.2.0", RmsFieldRolloutEventTypes.Sync, "ok", now),
				Event("b", "5.10.0", RmsFieldRolloutEventTypes.Sync, "ok", now),
				Event("c", "5.9.1", RmsFieldRolloutEventTypes.Sync, "ok", now),
				Event("d", "6.0.0", RmsFieldRolloutEventTypes.Sync, "ok", now)
			};

			var summary = RecordsFieldRolloutService.Summarize(RmsOriginClient.Responder, events, "5.2.0");

			// 5.10 is a later version than 5.9 even though it sorts earlier as text.
			summary.Versions.Select(v => v.AppVersion).Should().Equal("6.0.0", "5.10.0", "5.9.1", "5.2.0");
		}

		[Test]
		public void An_app_that_reports_nothing_still_shows_no_numbers_rather_than_wrong_ones()
		{
			var summary = RecordsFieldRolloutService.Summarize(RmsOriginClient.Dispatch, new List<RmsFieldRolloutEvent>(), "1.0.0");

			summary.ActiveUsers.Should().Be(0);
			summary.CompatibleUsers.Should().Be(0);
			summary.AbandonmentRate.Should().BeNull("a rate over nothing is not zero");
			summary.MedianTimeToCompleteMs.Should().BeNull();
		}

		private static RmsFieldRolloutEvent Event(string userId, string version, string type, string outcome, DateTime occurredOn) => new RmsFieldRolloutEvent
		{
			RmsFieldRolloutEventId = Guid.NewGuid().ToString(),
			DepartmentId = Dept,
			OriginClient = (int)RmsOriginClient.Responder,
			UserId = userId,
			AppVersion = version,
			EventType = type,
			Outcome = outcome,
			OccurredOn = occurredOn,
			RecordedOn = occurredOn
		};

		private static RmsFieldRolloutEvent Completed(string userId, string version, long durationMs, DateTime occurredOn)
		{
			var entry = Event(userId, version, RmsFieldRolloutEventTypes.Completed, "ok", occurredOn);
			entry.DurationMs = durationMs;
			return entry;
		}
	}
}
