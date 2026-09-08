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
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-4 optional post-finalization quality review (RMS plan section 4.7): rubrics, deterministic sampling, weighted scoring, trends, and the non-mutating rule.</summary>
	[TestFixture]
	public class RecordsQualityReviewServiceTests
	{
		private RmsPreventionHarness _h;
		private List<RmsOperationalRecord> _finalized;

		[SetUp]
		public void SetUp()
		{
			_h = new RmsPreventionHarness();
			_finalized = Enumerable.Range(1, 12).Select(i => new RmsOperationalRecord { RmsOperationalRecordId = $"rec-{i}", DepartmentId = Dept, DefinitionKey = i % 3 == 0 ? "training" : "run", State = (int)RmsRecordState.Finalized, RecordNumber = $"RUN-2026-{i:0000}", AuthorUserId = i % 2 == 0 ? "author-a" : "author-b", CurrentRevisionId = $"rev-{i}", FinalizedOn = DateTime.UtcNow.AddDays(-i) }).ToList();
			_h.Records.Setup(r => r.GetFinalizedSinceAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(_finalized);
			_h.Units.Setup(u => u.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => ids.Select(id => new RmsRecordUnitResponse { RecordId = id, UnitId = 2 }).ToList());
		}

		private Task<RmsQualityRubric> Rubric(string key = null, int size = 5) => _h.QualityService.SaveRubricAsync(Dept, Admin, new RmsQualityRubric { Name = "Run report QA", DefinitionKey = key, SampleSize = size, IsActive = true }, new List<RmsQualityCriterion>
		{
			new RmsQualityCriterion { Key = "times", Text = "Unit times complete", Weight = 3 },
			new RmsQualityCriterion { Key = "narrative", Text = "Narrative answers who/what/where", Weight = 2 },
			new RmsQualityCriterion { Key = "spelling", Text = "Spelling", Weight = 1 }
		});

		[Test]
		public async Task Sampling_is_deterministic_bounded_and_skips_reviewed_records()
		{
			var rubric = await Rubric("run", 4);
			var first = await _h.QualityService.SampleAsync(Dept, Admin, rubric.RmsQualityRubricId, DateTime.UtcNow.AddDays(-30));
			first.Should().HaveCount(4);
			first.Should().OnlyContain(r => r.DefinitionKey == "run" && r.ScoredOn == null && r.RevisionId != null);
			first.Should().OnlyContain(r => r.UnitId == 2, "the first responding unit is snapshotted for the unit trend");
			var second = await _h.QualityService.SampleAsync(Dept, Admin, rubric.RmsQualityRubricId, DateTime.UtcNow.AddDays(-30));
			second.Select(r => r.RecordId).Should().NotIntersectWith(first.Select(r => r.RecordId), "a record is sampled once per rubric");
			second.Should().HaveCount(4);
			(await _h.QualityService.GetPendingAsync(Dept, Admin, 50)).Should().HaveCount(8);
			Func<Task> member = () => _h.QualityService.SampleAsync(Dept, Member, rubric.RmsQualityRubricId, DateTime.UtcNow.AddDays(-30));
			await member.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> memberRubric = () => _h.QualityService.SaveRubricAsync(Dept, Member, new RmsQualityRubric { Name = "x", IsActive = true }, new List<RmsQualityCriterion> { new RmsQualityCriterion { Text = "y" } });
			await memberRubric.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Scoring_is_weighted_complete_and_never_by_the_author_and_trends_aggregate()
		{
			var rubric = await Rubric();
			var sample = await _h.QualityService.SampleAsync(Dept, Admin, rubric.RmsQualityRubricId, DateTime.UtcNow.AddDays(-30));
			var review = sample.First();
			Func<Task> partial = () => _h.QualityService.ScoreAsync(Dept, Admin, review.RmsQualityReviewId, new List<RmsQualityFinding> { new RmsQualityFinding { Key = "times", Score = 100 } }, null, false);
			await partial.Should().ThrowAsync<ArgumentException>();
			Func<Task> self = () => _h.QualityService.ScoreAsync(Dept, review.AuthorUserId, review.RmsQualityReviewId, Findings(100, 100, 100), null, false);
			_h.Reviewers.Add(review.AuthorUserId);
			await self.Should().ThrowAsync<InvalidOperationException>();

			var scored = await _h.QualityService.ScoreAsync(Dept, Admin, review.RmsQualityReviewId, Findings(100, 50, 0), "Narrative thin.", true);
			scored.Score.Should().Be(67, "(3*100 + 2*50 + 1*0) / 6 = 66.7");
			scored.Note.Should().Be("Narrative thin.");
			scored.AmendmentRecommended.Should().BeTrue();
			_h.Protection.Writes.Should().Contain("quality review");
			_h.Audits.Rows.Should().Contain(a => a.RecordId == review.RecordId && a.Purpose == "Quality review scored");
			Func<Task> twice = () => _h.QualityService.ScoreAsync(Dept, Admin, review.RmsQualityReviewId, Findings(1, 1, 1), null, false);
			await twice.Should().ThrowAsync<InvalidOperationException>();

			var other = sample.First(r => r.AuthorUserId != review.AuthorUserId);
			await _h.QualityService.ScoreAsync(Dept, Admin2, other.RmsQualityReviewId, Findings(100, 100, 100), null, false);
			var trends = await _h.QualityService.GetTrendsAsync(Dept, Admin, DateTime.UtcNow.AddDays(-1));
			trends.Scored.Should().Be(2);
			trends.ByAuthor.Should().HaveCount(2);
			trends.ByAuthor.First().AverageScore.Should().BeLessThan(trends.ByAuthor.Last().AverageScore);
			trends.ByCriterion.Single(c => c.Key == "spelling").AverageScore.Should().Be(50);
			trends.ByCriterion.Single(c => c.Key == "times").Label.Should().Be("Unit times complete");
			(await _h.QualityService.GetForRecordAsync(Dept, Member, review.RecordId)).Should().ContainSingle(r => r.Score == 67);
		}

		private static List<RmsQualityFinding> Findings(int times, int narrative, int spelling) => new List<RmsQualityFinding>
		{
			new RmsQualityFinding { Key = "times", Score = times }, new RmsQualityFinding { Key = "narrative", Score = narrative }, new RmsQualityFinding { Key = "spelling", Score = spelling }
		};
	}

	/// <summary>RMS-4 release telemetry: every counter comes from an existing table, and the alert rules are pinned.</summary>
	[TestFixture]
	public class RecordsReleaseTelemetryServiceTests
	{
		private Mock<IDomainEventOutboxRepository> _outbox;
		private Mock<IRmsRecordAttachmentsRepository> _attachments;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<IRmsRecordDueStatesRepository> _dueStates;
		private Mock<IRmsSubmissionsRepository> _submissions;
		private Mock<IWorkflowRunRepository> _runs;
		private Mock<IDepartmentDataProtectionService> _protection;
		private RmsPreventionHarness _h;
		private RecordsReleaseTelemetryService _service;

		[SetUp]
		public void SetUp()
		{
			_h = new RmsPreventionHarness();
			_outbox = new Mock<IDomainEventOutboxRepository>();
			_attachments = new Mock<IRmsRecordAttachmentsRepository>();
			_records = new Mock<IRmsOperationalRecordsRepository>();
			_dueStates = new Mock<IRmsRecordDueStatesRepository>();
			_submissions = new Mock<IRmsSubmissionsRepository>();
			_runs = new Mock<IWorkflowRunRepository>();
			_protection = new Mock<IDepartmentDataProtectionService>();
			_runs.Setup(r => r.GetByDepartmentIdPagedAsync(Dept, 1, 500)).ReturnsAsync(new List<WorkflowRun>
			{
				new WorkflowRun { Status = (int)WorkflowRunStatus.Completed, StartedOn = DateTime.UtcNow.AddHours(-1) },
				new WorkflowRun { Status = (int)WorkflowRunStatus.Skipped, StartedOn = DateTime.UtcNow.AddHours(-2) },
				new WorkflowRun { Status = (int)WorkflowRunStatus.Failed, StartedOn = DateTime.UtcNow.AddHours(-3) },
				new WorkflowRun { Status = (int)WorkflowRunStatus.Completed, StartedOn = DateTime.UtcNow.AddDays(-9) }
			});
			_protection.Setup(p => p.IsProtectionEnforcedAsync(Dept)).ReturnsAsync(true);
			_protection.Setup(p => p.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(13);
			_service = new RecordsReleaseTelemetryService(_h.Cutover.Object, _h.Authorization.Object, _h.Audits, _outbox.Object, _runs.Object, _attachments.Object, _records.Object, _dueStates.Object, _submissions.Object,
				_h.Inspections, _h.Violations, _h.Permits, _h.Hydrants, _protection.Object, _h.Gate);
		}

		[Test]
		public async Task Counters_and_alerts_come_from_the_tables_the_platform_already_writes()
		{
			_h.Audits.Rows.Add(new RmsAccessAudit { DepartmentId = Dept, Action = (int)RmsAccessAuditAction.LegacyWriteDenied, OccurredOn = DateTime.UtcNow.AddHours(-2) });
			_h.Audits.Rows.Add(new RmsAccessAudit { DepartmentId = Dept, Action = (int)RmsAccessAuditAction.LegacyWriteDenied, OccurredOn = DateTime.UtcNow.AddDays(-3) });
			_h.Audits.Rows.Add(new RmsAccessAudit { DepartmentId = Dept, Action = (int)RmsAccessAuditAction.Denied, OccurredOn = DateTime.UtcNow.AddMinutes(-5) });
			_outbox.Setup(o => o.CountByStateForDepartmentAsync(Dept, (int)DomainEventOutboxState.Pending)).ReturnsAsync(4);
			_outbox.Setup(o => o.CountByStateForDepartmentAsync(Dept, (int)DomainEventOutboxState.Failed)).ReturnsAsync(0);
			_outbox.Setup(o => o.GetOldestPendingCreatedOnForDepartmentAsync(Dept)).ReturnsAsync(DateTime.UtcNow.AddSeconds(-90));
			_attachments.Setup(a => a.CountByScanStateAsync(Dept, (int)RmsAttachmentScanState.Rejected)).ReturnsAsync(1);
			_records.Setup(r => r.CountCreatedSinceAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(12);
			_records.Setup(r => r.CountFinalizedSinceAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(9);
			_dueStates.Setup(d => d.CountOverdueAsync(Dept)).ReturnsAsync(2);
			_submissions.Setup(s => s.CountByStateAsync(Dept, (int)RmsSubmissionState.Failed)).ReturnsAsync(1);
			_h.Violations.Rows.Add(new RmsViolation { DepartmentId = Dept, RmsViolationId = "v", State = (int)RmsViolationState.Open, DueOn = DateTime.UtcNow.AddDays(-2) });

			var t = await _service.GetAsync(Dept, Admin, 24);
			t.LegacyWriteAttempts.Should().Be(1, "one denial inside the 24 h window");
			t.AuthorizationDenials.Should().Be(1);
			t.OutboxPending.Should().Be(4);
			t.OutboxLagAlert.Should().BeTrue();
			t.WorkflowRunsCompleted.Should().Be(1); t.WorkflowRunsSkipped.Should().Be(1); t.WorkflowRunsFailed.Should().Be(1);
			t.RecordsCreated.Should().Be(12); t.RecordsFinalized.Should().Be(9); t.RecordsOverdue.Should().Be(2);
			t.SubmissionsFailed.Should().Be(1);
			t.ViolationsOverdue.Should().Be(1);
			t.ProtectionEnforced.Should().BeTrue(); t.ProtectedCatalogVersion.Should().Be(13);
			t.RecordsActive.Should().BeTrue();
			t.Alerts.Should().Contain(a => a.StartsWith("Outbox backlog")).And.Contain(a => a.Contains("legacy Log/UnitLog write attempt")).And.Contain(a => a.Contains("NERIS submission"));
			t.Warnings.Should().Contain(w => w.Contains("rejected by the scanner")).And.Contain(w => w.Contains("overdue")).And.Contain(w => w.Contains("code violation"));

			Func<Task> member = () => _service.GetAsync(Dept, Member, 24);
			await member.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task A_failing_counter_source_degrades_to_a_warning_and_the_snapshot_still_logs()
		{
			_outbox.Setup(o => o.CountByStateForDepartmentAsync(Dept, It.IsAny<int>())).ThrowsAsync(new InvalidOperationException("db down"));
			var t = await _service.LogSnapshotAsync(Dept);
			t.Warnings.Should().Contain("outbox counters unavailable.");
			t.OutboxLagAlert.Should().BeFalse();
			t.WindowHours.Should().Be(24);
		}
	}
}
