using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>Officer actions, apply-history, the hours report, backfill and the Records metrics reading the same ledger.</summary>
	[TestFixture]
	public class LearningRecordServiceTests
	{
		private const int DepartmentId = 12;
		private const string Member = "member-a";
		private const string Former = "member-gone";
		private const string Officer = "officer-b";
		private const int TypeId = 70;
		private const int CategoryId = 5;

		private FakeLearningRecordsRepository _records;
		private List<CategoryCreditMapping> _mappingRows;
		private List<PersonnelCertification> _holders;
		private List<PersonnelCertificationCredit> _creditRows;
		private Mock<ICertificationService> _certifications;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentsService> _departments;
		private Mock<ITrainingRepository> _trainings;
		private Mock<ITrainingUserRepository> _trainingUsers;
		private Mock<ITrainingCompletionHistoriesRepository> _histories;
		private Mock<ILogsRepository> _logs;
		private Mock<ILogUsersRepository> _logUsers;
		private Mock<ILearningRecordBackfillsRepository> _backfills;
		private LearningRecordWriter _writer;
		/// <summary>Round 9A: the observers the writer tells about written and voided records.</summary>
		private readonly List<ILearningRecordObserver> _observers = new List<ILearningRecordObserver>();
		private LearningRecordService _service;
		private TrainingRecordSettings _recordSettings;

		[SetUp]
		public void SetUp()
		{
			_records = new FakeLearningRecordsRepository();
			_mappingRows = new List<CategoryCreditMapping>();
			_holders = new List<PersonnelCertification>();
			_creditRows = new List<PersonnelCertificationCredit>();
			_recordSettings = new TrainingRecordSettings();

			var mappings = new Mock<ICategoryCreditMappingsRepository>();
			mappings.Setup(m => m.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _mappingRows);
			var categories = new Mock<ITrainingCategoriesRepository>();
			categories.Setup(c => c.GetForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory>
			{
				new TrainingCategory { TrainingCategoryId = CategoryId, DepartmentId = DepartmentId, Name = "Company training", TargetHours = 192m },
				new TrainingCategory { TrainingCategoryId = 6, DepartmentId = DepartmentId, Name = "Hazmat" }
			});
			var certificationRecords = new Mock<IPersonnelCertificationRepository>();
			certificationRecords.Setup(c => c.GetForDepartmentAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(() => _holders);
			certificationRecords.Setup(c => c.GetByTypeIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(() => _holders);
			certificationRecords.Setup(c => c.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _holders.FirstOrDefault(h => h.PersonnelCertificationId == (int)id));
			var credits = new Mock<IPersonnelCertificationCreditsRepository>();
			credits.Setup(c => c.GetByLearningRecordIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int d, IEnumerable<int> ids) => _creditRows.Where(c => c.LearningRecordId.HasValue && ids.Contains(c.LearningRecordId.Value)).ToList());
			_certifications = new Mock<ICertificationService>();
			_certifications.Setup(c => c.GetCertificationTypeByIdAsync(TypeId)).ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = TypeId, DepartmentId = DepartmentId, Type = "EMT", Code = "NREMT-E", RenewalCreditHoursRequired = 40m });
			_certifications.Setup(c => c.GetCertificationCreditsAsync(It.IsAny<int>())).ReturnsAsync((int id) => _creditRows.Where(c => c.PersonnelCertificationId == id).ToList());
			_certifications.Setup(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertificationCredit credit, string user, CancellationToken ct) =>
				{
					credit.PersonnelCertificationCreditId = 1000 + _creditRows.Count;
					_creditRows.Add(credit);
					return credit;
				});
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetTrainingRecordSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _recordSettings);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Member, Officer });
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = Member, FirstName = "Alex" }, new PersonName { UserId = Former, FirstName = "Gone" } });
			_trainings = new Mock<ITrainingRepository>();
			_trainingUsers = new Mock<ITrainingUserRepository>();
			_histories = new Mock<ITrainingCompletionHistoriesRepository>();
			_logs = new Mock<ILogsRepository>();
			_logUsers = new Mock<ILogUsersRepository>();
			_backfills = new Mock<ILearningRecordBackfillsRepository>();

			_writer = new LearningRecordWriter(_records, mappings.Object, categories.Object, certificationRecords.Object, credits.Object,
				new Lazy<ICertificationService>(() => _certifications.Object), _trainings.Object, _trainingUsers.Object, _histories.Object, _logUsers.Object,
				Mock.Of<IRmsOperationalRecordsRepository>(), Mock.Of<IRmsRecordParticipantsRepository>(), Mock.Of<ICalendarItemsRepository>(), Mock.Of<ICalendarItemTypeRepository>(),
				_settings.Object, Mock.Of<IEventAggregator>(), new Lazy<IEnumerable<ILearningRecordObserver>>(() => _observers));
			_service = new LearningRecordService(_records, Mock.Of<ILearningRecordFilesRepository>(), categories.Object, mappings.Object, _backfills.Object, _writer,
				_certifications.Object, certificationRecords.Object, credits.Object, _settings.Object, _departments.Object, Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IShiftsService>(), Mock.Of<IEventAggregator>(), new Resgrid.Services.Records.NullRecordAttachmentScanner());
		}

		private Task<LearningRecordWriteResult> Write(string source, string user, DateTime on, decimal hours, int? category = CategoryId, bool backfill = false, LearningRecordStatuses status = LearningRecordStatuses.Verified) =>
			_writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, source, new[]
			{
				new LearningRecordDraft { UserId = user, Title = "Drill " + source, CompletedOn = on, Hours = hours, CategoryId = category, IsBackfill = backfill, Status = status }
			}, Officer);

		[Test]
		public async Task Nobody_verifies_their_own_submission_and_only_pending_records_are_reviewed()
		{
			var submission = await _service.SubmitExternalAsync(DepartmentId, Member, new LearningRecordDraft { Title = "State EMS conference", CompletedOn = new DateTime(2026, 5, 2), Hours = 8m }, "cert.pdf", "application/pdf", null);
			submission.Status.Should().Be((int)LearningRecordStatuses.Pending);

			Func<Task> self = () => _service.VerifyAsync(DepartmentId, submission.LearningRecordId, Member, null, null);
			(await self.Should().ThrowAsync<InvalidOperationException>()).WithMessage("learning_record_self_review");

			var verified = await _service.VerifyAsync(DepartmentId, submission.LearningRecordId, Officer, "Certificate checked", 7.5m);
			verified.Status.Should().Be((int)LearningRecordStatuses.Verified);
			verified.Hours.Should().Be(7.5m);

			Func<Task> twice = () => _service.RejectAsync(DepartmentId, submission.LearningRecordId, Officer, "no");
			(await twice.Should().ThrowAsync<InvalidOperationException>()).WithMessage("learning_record_not_pending", "a verified record is immutable");
		}

		[Test]
		public async Task Submissions_switched_off_are_refused()
		{
			_recordSettings.MemberSubmissionsDisabled = true;
			(await _service.SubmitExternalAsync(DepartmentId, Member, new LearningRecordDraft { Title = "x", CompletedOn = DateTime.UtcNow, Hours = 1m }, null, null, null)).Should().BeNull();
			_records.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Apply_history_previews_backfilled_records_beside_manual_credits_and_writes_only_what_is_kept()
		{
			_mappingRows.Add(new CategoryCreditMapping { CategoryCreditMappingId = 1, DepartmentId = DepartmentId, DepartmentCertificationTypeId = TypeId, CategoryId = CategoryId, IsEnabled = true, EnabledOn = new DateTime(2026, 11, 1) });
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 300, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId, RecievedOn = new DateTime(2026, 1, 1), ExpiresOn = new DateTime(2027, 12, 31) });
			_creditRows.Add(new PersonnelCertificationCredit { PersonnelCertificationCreditId = 1, PersonnelCertificationId = 300, DepartmentId = DepartmentId, Hours = 4m, CreditDate = new DateTime(2026, 3, 1) });

			await Write("log:1", Member, new DateTime(2026, 3, 1), 4m, backfill: true);
			await Write("log:2", Member, new DateTime(2026, 4, 1), 2m, backfill: true);
			await Write("log:3", Member, new DateTime(2025, 4, 1), 2m, backfill: true);
			_creditRows.Should().HaveCount(1, "backfilled records never credit by themselves (R-T3)");

			var preview = await _service.PreviewHistoryCreditsAsync(DepartmentId, TypeId);
			var cycle = preview.Cycles.Single();
			cycle.Proposed.Select(p => p.Hours).Should().BeEquivalentTo(new[] { 4m, 2m });
			cycle.ManualHours.Should().Be(4m, "the manual credit typed in for the same class is shown beside the proposal");
			cycle.RequiredHours.Should().Be(40m);
			preview.OutsideEveryCycle.Should().Be(1);
			_creditRows.Should().HaveCount(1, "the preview writes nothing");

			var keep = cycle.Proposed.Single(p => p.Hours == 2m).LearningRecordId;
			(await _service.ApplyHistoryCreditsAsync(DepartmentId, TypeId, new[] { keep }, Officer)).Should().Be(1);
			_creditRows.Should().ContainSingle(c => c.LearningRecordId == keep && c.PersonnelCertificationId == 300);
			(await _service.PreviewHistoryCreditsAsync(DepartmentId, TypeId)).ProposedCount.Should().Be(1, "an applied record is not proposed again");
		}

		[Test]
		public async Task Bulk_categorize_by_course_name_moves_uncategorized_records_and_credits_follow()
		{
			_mappingRows.Add(new CategoryCreditMapping { CategoryCreditMappingId = 1, DepartmentId = DepartmentId, DepartmentCertificationTypeId = TypeId, CategoryId = CategoryId, IsEnabled = true, EnabledOn = new DateTime(2026, 1, 1) });
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 300, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId });
			await Write("log:10", Member, new DateTime(2026, 6, 1), 3m, category: null);
			await Write("log:11", Member, new DateTime(2026, 6, 2), 3m, category: null, backfill: true);

			var count = await _service.CategorizeAsync(DepartmentId, CategoryId, null, null, new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), null, Officer);

			count.Should().Be(2);
			_records.Live.Should().OnlyContain(r => r.CategoryId == CategoryId);
			_creditRows.Should().ContainSingle("only the record that is not backfilled credits on categorizing");
			Func<Task> foreign = () => _service.CategorizeAsync(DepartmentId, 999, "x", null, null, null, null, Officer);
			await foreign.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task The_hours_report_hides_former_members_unless_asked_and_shows_unresolved_duplicates()
		{
			await Write("log:20", Member, new DateTime(2026, 6, 1), 3m);
			await Write("log:21", Former, new DateTime(2026, 6, 1), 5m);
			await Write("log:22", Member, new DateTime(2026, 6, 3), 1m, status: LearningRecordStatuses.Pending);
			var dup = _records.Rows.First();
			dup.PossibleDuplicateOfId = 99;

			var query = new TrainingHoursReportQuery { DepartmentId = DepartmentId, From = new DateTime(2026, 1, 1), To = new DateTime(2027, 1, 1), GroupBy = TrainingHoursGroupings.Member };
			var current = await _service.GetHoursReportAsync(query);
			current.TotalHours.Should().Be(3m, "pending records do not count and former members are left out by default");
			current.UnresolvedDuplicates.Should().Be(1);
			current.PendingRecords.Should().Be(1);

			query.IncludeFormerMembers = true;
			var audit = await _service.GetHoursReportAsync(query);
			audit.TotalHours.Should().Be(8m, "R-T13: departed members' records stay reportable behind the switch");
			audit.Rows.Single(r => r.Key == Former).IsFormerMember.Should().BeTrue();

			query.GroupBy = TrainingHoursGroupings.Category;
			(await _service.GetHoursReportAsync(query)).Rows.Single().TargetHours.Should().Be(192m);
		}

		[Test]
		public async Task Records_training_metrics_and_the_hours_report_read_the_same_ledger()
		{
			var record = new RmsOperationalRecord { RmsOperationalRecordId = "rec-1", DepartmentId = DepartmentId, DefinitionKey = RmsDefinitionKeys.Training, StartedOn = new DateTime(2026, 6, 1, 9, 0, 0), EndedOn = new DateTime(2026, 6, 1, 13, 0, 0) };
			var drafts = LearningRecordWriter.RecordsDrafts(record, new[]
			{
				new RmsRecordParticipant { UserId = Member },
				new RmsRecordParticipant { UserId = Officer, ParticipationStart = new DateTime(2026, 6, 1, 9, 0, 0), ParticipationEnd = new DateTime(2026, 6, 1, 11, 0, 0) }
			}, false);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, LearningRecordRules.RecordsRef("rec-1"), drafts, Officer);

			var metrics = Resgrid.Services.Records.RecordsAnalyticsService.LedgerTrainingMetrics(new[] { record }, _records.Live.ToList(), r => "2026-06");
			var report = await _service.GetHoursReportAsync(new TrainingHoursReportQuery { DepartmentId = DepartmentId, From = new DateTime(2026, 1, 1), To = new DateTime(2027, 1, 1) });

			metrics.Hours.Should().Be(6d);
			((double)report.TotalHours).Should().Be(metrics.Hours, "the analytics and the transcript cannot disagree");
			metrics.MembersTrained.Should().Be(report.Members);

			// A participant removed by an amendment leaves both at once.
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, LearningRecordRules.RecordsRef("rec-1"), drafts.Take(1).ToList(), Officer);
			Resgrid.Services.Records.RecordsAnalyticsService.LedgerTrainingMetrics(new[] { record }, _records.Live.ToList(), r => "2026-06").Hours.Should().Be(4d);
		}

		[Test]
		public async Task The_backfill_writes_verified_history_with_original_dates_never_credits_and_a_dry_run_writes_nothing()
		{
			_mappingRows.Add(new CategoryCreditMapping { CategoryCreditMappingId = 1, DepartmentId = DepartmentId, DepartmentCertificationTypeId = TypeId, CategoryId = CategoryId, IsEnabled = true, EnabledOn = new DateTime(2020, 1, 1) });
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 300, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId });
			var training = new Training { TrainingId = 8, DepartmentId = DepartmentId, Name = "Hazmat refresher", Hours = 6m, CategoryId = CategoryId };
			_trainings.Setup(t => t.GetTrainingsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new[] { training });
			_trainingUsers.Setup(t => t.GetByTrainingIdAsync(8)).ReturnsAsync(new List<TrainingUser> { new TrainingUser { TrainingId = 8, UserId = Member, Cycle = 1, Complete = true, CompletedOn = new DateTime(2025, 3, 1) } });
			_histories.Setup(h => h.GetForTrainingAsync(DepartmentId, 8)).ReturnsAsync(new List<TrainingCompletionHistory> { new TrainingCompletionHistory { TrainingId = 8, UserId = Member, Cycle = 0, CompletedOn = new DateTime(2024, 3, 1) } });
			_logs.Setup(l => l.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new[]
			{
				new Log { LogId = 4, DepartmentId = DepartmentId, LogType = (int)LogTypes.Training, Course = "Ropes", StartedOn = new DateTime(2023, 5, 1, 9, 0, 0), EndedOn = new DateTime(2023, 5, 1, 11, 0, 0) },
				new Log { LogId = 5, DepartmentId = DepartmentId, LogType = 1 }
			});
			_logUsers.Setup(l => l.GetLogsByLogIdAsync(4)).ReturnsAsync(new[] { new LogUser { LogId = 4, UserId = Member } });
			var backfill = new LearningRecordBackfillService(_backfills.Object, _records, _writer, _trainings.Object, _trainingUsers.Object, _histories.Object, _logs.Object, _logUsers.Object,
				Mock.Of<IRmsOperationalRecordsRepository>(), Mock.Of<IRmsRecordParticipantsRepository>());

			var dry = await backfill.RunAsync(new LearningRecordBackfill { LearningRecordBackfillId = 1, DepartmentId = DepartmentId, DryRun = true, State = (int)LearningRecordBackfillStates.Running }, 1000);
			dry.State.Should().Be((int)LearningRecordBackfillStates.Completed);
			dry.CompletionsFound.Should().Be(2);
			dry.TrainingRecordsFound.Should().Be(1);
			dry.RecordsWritten.Should().Be(3, "the dry run counts what it would write");
			_records.Rows.Should().BeEmpty("a dry run writes nothing");

			var job = await backfill.RunAsync(new LearningRecordBackfill { LearningRecordBackfillId = 2, DepartmentId = DepartmentId, State = (int)LearningRecordBackfillStates.Running }, 1000);
			job.RecordsWritten.Should().Be(3);
			_records.Live.Should().OnlyContain(r => r.IsBackfill && r.Status == (int)LearningRecordStatuses.Verified);
			_records.Live.Select(r => r.CompletedOn.Year).Should().BeEquivalentTo(new[] { 2024, 2025, 2023 }, "original dates");
			_creditRows.Should().BeEmpty("R-T3: backfill never credits a certification by itself");

			var rerun = await backfill.RunAsync(new LearningRecordBackfill { LearningRecordBackfillId = 3, DepartmentId = DepartmentId, State = (int)LearningRecordBackfillStates.Running }, 1000);
			rerun.RecordsWritten.Should().Be(0);
			rerun.RecordsSkipped.Should().Be(3, "a re-run never duplicates");
		}

		[Test]
		public async Task A_backfill_out_of_budget_resumes_where_it_stopped()
		{
			_trainings.Setup(t => t.GetTrainingsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(Enumerable.Range(1, 3).Select(i => new Training { TrainingId = i, DepartmentId = DepartmentId, Name = "T" + i }).ToArray());
			_trainingUsers.Setup(t => t.GetByTrainingIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new List<TrainingUser> { new TrainingUser { TrainingId = id, UserId = Member, Complete = true, CompletedOn = new DateTime(2026, 1, id) } });
			_histories.Setup(h => h.GetForTrainingAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync(new List<TrainingCompletionHistory>());
			_logs.Setup(l => l.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(Array.Empty<Log>());
			var backfill = new LearningRecordBackfillService(_backfills.Object, _records, _writer, _trainings.Object, _trainingUsers.Object, _histories.Object, _logs.Object, _logUsers.Object,
				Mock.Of<IRmsOperationalRecordsRepository>(), Mock.Of<IRmsRecordParticipantsRepository>());

			var job = new LearningRecordBackfill { LearningRecordBackfillId = 9, DepartmentId = DepartmentId, State = (int)LearningRecordBackfillStates.Running };
			await backfill.RunAsync(job, 2);
			job.State.Should().Be((int)LearningRecordBackfillStates.Queued, "the next run picks it up");
			job.ResumeAfter.Should().Be("trainings:2");
			_records.Live.Should().HaveCount(2);

			job.State = (int)LearningRecordBackfillStates.Running;
			await backfill.RunAsync(job, 2);
			job.State.Should().Be((int)LearningRecordBackfillStates.Completed);
			_records.Live.Should().HaveCount(3);
		}

		[Test]
		public async Task Worker_85_claims_each_job_once_and_never_asks_a_billing_gate()
		{
			var job = new LearningRecordBackfill { LearningRecordBackfillId = 4, DepartmentId = DepartmentId, State = (int)LearningRecordBackfillStates.Queued };
			_backfills.Setup(b => b.GetRunnableAsync(It.IsAny<DateTime>(), TrainingRecordsLogic.JobsPerRun)).ReturnsAsync(new List<LearningRecordBackfill> { job, new LearningRecordBackfill { LearningRecordBackfillId = 5, DepartmentId = 13 } });
			_backfills.Setup(b => b.ClaimAsync(4, (int)LearningRecordBackfillStates.Queued, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_backfills.Setup(b => b.ClaimAsync(5, It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			var runner = new Mock<ILearningRecordBackfillService>();
			runner.Setup(r => r.RunAsync(job, TrainingRecordsLogic.SourcesPerJob, It.IsAny<CancellationToken>()))
				.ReturnsAsync((LearningRecordBackfill b, int budget, CancellationToken ct) => { b.State = (int)LearningRecordBackfillStates.Completed; b.RecordsWritten = 7; return b; });

			var result = await TrainingRecordsLogic.Process(_backfills.Object, runner.Object, DateTime.UtcNow);

			result.Jobs.Should().Be(1, "the job another worker claimed is skipped");
			result.Completed.Should().Be(1);
			result.Written.Should().Be(7);
			runner.Verify(r => r.RunAsync(It.Is<LearningRecordBackfill>(b => b.LearningRecordBackfillId == 5), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void Retention_follows_the_category_then_the_department_default_and_counts_from_departure()
		{
			var record = new LearningRecord { CompletedOn = new DateTime(2026, 1, 10) };
			_service.RetainedUntil(record, new TrainingCategory { RetentionYears = 3 }, null, null).Should().Be(new DateTime(2029, 1, 10));
			_service.RetainedUntil(record, new TrainingCategory(), new TrainingRecordSettings { DefaultRetentionYears = 5 }, new DateTime(2027, 2, 1)).Should().Be(new DateTime(2032, 2, 1));
			_service.RetainedUntil(record, null, new TrainingRecordSettings(), null).Should().BeNull();
		}
	}
}
