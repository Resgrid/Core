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

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>The one transcript's writer rules (§12.2): R-T1 idempotency, R-T2 one session counted once and duplicates, R-T4 void and supersede, R-T5 credits in the right cycle.</summary>
	[TestFixture]
	public class LearningRecordWriterTests
	{
		private const int DepartmentId = 12;
		private const string Member = "member-a";
		private const string Officer = "officer-b";
		private const int TypeId = 70;
		private const int CategoryId = 5;

		private FakeLearningRecordsRepository _records;
		private Mock<ICategoryCreditMappingsRepository> _mappings;
		private Mock<ITrainingCategoriesRepository> _categories;
		private Mock<IPersonnelCertificationRepository> _certificationRecords;
		private Mock<IPersonnelCertificationCreditsRepository> _credits;
		private Mock<ICertificationService> _certifications;
		private Mock<ITrainingRepository> _trainings;
		private Mock<ITrainingUserRepository> _trainingUsers;
		private Mock<ITrainingCompletionHistoriesRepository> _histories;
		private Mock<ICalendarItemsRepository> _calendarItems;
		private Mock<ICalendarItemTypeRepository> _calendarTypes;
		private Mock<IDepartmentSettingsService> _settings;
		private List<CategoryCreditMapping> _mappingRows;
		private List<PersonnelCertification> _holders;
		private List<PersonnelCertificationCredit> _creditRows;
		private LearningRecordWriter _writer;

		[SetUp]
		public void SetUp()
		{
			_records = new FakeLearningRecordsRepository();
			_mappingRows = new List<CategoryCreditMapping>();
			_holders = new List<PersonnelCertification>();
			_creditRows = new List<PersonnelCertificationCredit>();
			_mappings = new Mock<ICategoryCreditMappingsRepository>();
			_mappings.Setup(m => m.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _mappingRows);
			_categories = new Mock<ITrainingCategoriesRepository>();
			_categories.Setup(c => c.GetForDepartmentAsync(DepartmentId, true)).ReturnsAsync(new List<TrainingCategory> { new TrainingCategory { TrainingCategoryId = CategoryId, DepartmentId = DepartmentId, Name = "Company training" } });
			_certificationRecords = new Mock<IPersonnelCertificationRepository>();
			_certificationRecords.Setup(c => c.GetForDepartmentAsync(DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(() => _holders);
			_credits = new Mock<IPersonnelCertificationCreditsRepository>();
			_credits.Setup(c => c.GetByLearningRecordIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>()))
				.ReturnsAsync((int d, IEnumerable<int> ids) => _creditRows.Where(c => c.LearningRecordId.HasValue && ids.Contains(c.LearningRecordId.Value)).ToList());
			_certifications = new Mock<ICertificationService>();
			_certifications.Setup(c => c.AddCertificationCreditAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PersonnelCertificationCredit credit, string user, CancellationToken ct) =>
				{
					credit.PersonnelCertificationCreditId = 1000 + _creditRows.Count;
					_creditRows.Add(credit);
					return credit;
				});
			_certifications.Setup(c => c.DeleteCertificationCreditAsync(It.IsAny<int>(), DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int d, string u, CancellationToken ct) => _creditRows.RemoveAll(c => c.PersonnelCertificationCreditId == id) > 0);
			_trainings = new Mock<ITrainingRepository>();
			_trainingUsers = new Mock<ITrainingUserRepository>();
			_histories = new Mock<ITrainingCompletionHistoriesRepository>();
			_calendarItems = new Mock<ICalendarItemsRepository>();
			_calendarTypes = new Mock<ICalendarItemTypeRepository>();
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetTrainingRecordSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new TrainingRecordSettings { HoursRoundingMinutes = 15 });

			_writer = new LearningRecordWriter(_records, _mappings.Object, _categories.Object, _certificationRecords.Object, _credits.Object,
				new Lazy<ICertificationService>(() => _certifications.Object), _trainings.Object, _trainingUsers.Object, _histories.Object, Mock.Of<ILogUsersRepository>(),
				Mock.Of<IRmsOperationalRecordsRepository>(), Mock.Of<IRmsRecordParticipantsRepository>(), _calendarItems.Object, _calendarTypes.Object, _settings.Object, Mock.Of<IEventAggregator>());
		}

		private static LearningRecordDraft Draft(string user = Member, decimal hours = 2m, string component = "", string session = null, DateTime? start = null, int? category = null) => new LearningRecordDraft
		{
			UserId = user, ComponentKey = component, SessionKey = session, Title = "Pump operations", CompletedOn = (start ?? new DateTime(2026, 6, 1, 14, 0, 0)).AddHours((double)hours),
			StartedOn = start ?? new DateTime(2026, 6, 1, 14, 0, 0), EndedOn = (start ?? new DateTime(2026, 6, 1, 14, 0, 0)).AddHours((double)hours), Hours = hours, CategoryId = category
		};

		private void MapCategory(DateTime enabledOn) =>
			_mappingRows.Add(new CategoryCreditMapping { CategoryCreditMappingId = 1, DepartmentId = DepartmentId, DepartmentCertificationTypeId = TypeId, CategoryId = CategoryId, IsEnabled = true, EnabledOn = enabledOn });

		[Test]
		public async Task Writing_the_same_source_twice_keeps_one_record()
		{
			var first = await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:7", new[] { Draft() }, Officer);
			var second = await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:7", new[] { Draft() }, Officer);

			first.Written.Should().Be(1);
			second.Written.Should().Be(0);
			second.Unchanged.Should().Be(1);
			_records.Live.Should().ContainSingle();
		}

		[Test]
		public async Task A_multi_activity_session_writes_one_record_per_component()
		{
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:8",
				new[] { Draft(component: "pumps", hours: 2m), Draft(component: "ladders", hours: 1m), Draft(user: "member-c", component: "pumps") }, Officer);

			_records.Live.Should().HaveCount(3, "R-T1: the component separates the lines of one drill");
			_records.Live.Where(r => r.UserId == Member).Select(r => r.ComponentKey).Should().BeEquivalentTo("pumps", "ladders");
		}

		[Test]
		public async Task Repeat_completions_each_keep_their_own_record()
		{
			var training = new Training { TrainingId = 33, DepartmentId = DepartmentId, Name = "Annual SCBA", Hours = 1.5m, CategoryId = CategoryId };
			_trainings.Setup(t => t.GetTrainingByTrainingIdAsync(33)).ReturnsAsync(training);
			_trainingUsers.Setup(t => t.GetTrainingUserByTrainingIdAndUserIdAsync(33, Member)).ReturnsAsync(new TrainingUser { TrainingId = 33, UserId = Member, Cycle = 1, Complete = true, CompletedOn = new DateTime(2026, 9, 1) });
			_histories.Setup(h => h.GetForMemberAsync(DepartmentId, Member)).ReturnsAsync(new List<TrainingCompletionHistory>
			{
				new TrainingCompletionHistory { TrainingId = 33, UserId = Member, Cycle = 0, CompletedOn = new DateTime(2025, 9, 1) },
				new TrainingCompletionHistory { TrainingId = 99, UserId = Member, Cycle = 0, CompletedOn = new DateTime(2025, 9, 2) }
			});

			await _writer.WriteCourseCompletionAsync(DepartmentId, 33, Member);
			await _writer.WriteCourseCompletionAsync(DepartmentId, 33, Member);

			_records.Live.Select(r => r.SourceRefId).Should().BeEquivalentTo(new[] { "training:33:0", "training:33:1" },
				"the source is the completion cycle, never the training id, so last year's pass is not dropped (R-T1)");
			_records.Live.Should().OnlyContain(r => r.Hours == 1.5m && r.CategoryId == CategoryId && r.Status == (int)LearningRecordStatuses.Verified);
		}

		[Test]
		public async Task A_changed_source_voids_and_supersedes_and_the_credit_moves()
		{
			MapCategory(new DateTime(2026, 1, 1));
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 500, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId, RecievedOn = new DateTime(2026, 1, 1), ExpiresOn = new DateTime(2027, 12, 31) });

			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:9", new[] { Draft(hours: 2m, category: CategoryId) }, Officer);
			var original = _records.Live.Single();
			_creditRows.Should().ContainSingle(c => c.LearningRecordId == original.LearningRecordId && c.Hours == 2m && c.PersonnelCertificationId == 500);

			var result = await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:9", new[] { Draft(hours: 3m, category: CategoryId) }, Officer);

			result.Voided.Should().Be(1);
			result.Written.Should().Be(1);
			var replacement = _records.Live.Single();
			replacement.SupersedesId.Should().Be(original.LearningRecordId, "R-T4: a verified record is immutable; the correction is a new record");
			_records.Rows.Single(r => r.LearningRecordId == original.LearningRecordId).VoidReason.Should().Be("source_changed");
			_creditRows.Should().ContainSingle(c => c.LearningRecordId == replacement.LearningRecordId && c.Hours == 3m, "the voided record's credit went and the new one's came");
		}

		[Test]
		public async Task A_participant_removed_from_the_source_is_voided_with_their_credit()
		{
			MapCategory(new DateTime(2026, 1, 1));
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 500, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId });
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:10", new[] { Draft(category: CategoryId), Draft(user: "member-c", category: CategoryId) }, Officer);
			_creditRows.Should().HaveCount(1);

			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:10", new[] { Draft(user: "member-c", category: CategoryId) }, Officer);

			_records.Live.Should().ContainSingle(r => r.UserId == "member-c");
			_creditRows.Should().BeEmpty();
		}

		[Test]
		public async Task A_higher_precedence_source_of_one_session_carries_the_hours()
		{
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.CourseCompletion, "training:1:0", new[] { Draft(hours: 4m, session: "class:1") }, Officer);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.CalendarAttendance, "checkin:1", new[] { Draft(hours: 4m, session: "class:1") }, Officer);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:1", new[] { Draft(hours: 4m, session: "class:1") }, Officer);

			_records.Live.Should().HaveCount(3, "every source still shows on the transcript");
			_records.Live.Sum(r => r.Hours).Should().Be(4m, "R-T2: one 4-hour class counts once, not 12 hours");
			_records.Live.Single(r => r.Hours > 0).SourceKind.Should().Be((int)LearningRecordSourceKinds.TrainingRecord);

			// A lower source written after the higher one gets no hours at all.
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.CalendarAttendance, "checkin:2", new[] { Draft(hours: 4m, session: "class:1") }, Officer);
			_records.Live.Sum(r => r.Hours).Should().Be(4m);
		}

		[Test]
		public async Task Unlinked_sources_with_overlapping_times_on_one_day_are_flagged_not_merged()
		{
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:20", new[] { Draft(hours: 3m, start: new DateTime(2026, 6, 1, 9, 0, 0)) }, Officer);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.CalendarAttendance, "checkin:20", new[] { Draft(hours: 2m, start: new DateTime(2026, 6, 1, 10, 0, 0)) }, Officer);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.ExternalSubmission, "submission:x", new[] { Draft(hours: 2m, start: new DateTime(2026, 6, 1, 13, 0, 0)) }, Member);

			var flagged = _records.Live.Where(r => r.PossibleDuplicateOfId.HasValue).ToList();
			flagged.Should().ContainSingle(r => r.SourceRefId == "checkin:20", "09:00-12:00 and 10:00-12:00 overlap; 13:00 does not");
			_records.Live.Sum(r => r.Hours).Should().Be(7m, "a possible duplicate still counts until an officer resolves it");
		}

		[Test]
		public async Task A_credit_lands_in_the_cycle_that_contains_the_date()
		{
			MapCategory(new DateTime(2024, 1, 1));
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 601, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId, RecievedOn = new DateTime(2024, 1, 1), ExpiresOn = new DateTime(2025, 12, 31), Status = (int)PersonnelCertificationStatuses.Expired });
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 602, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId, RecievedOn = new DateTime(2026, 1, 1), ExpiresOn = new DateTime(2027, 12, 31) });

			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:30", new[] { Draft(category: CategoryId, start: new DateTime(2025, 11, 3, 9, 0, 0)) }, Officer);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:31", new[] { Draft(category: CategoryId, start: new DateTime(2026, 2, 3, 9, 0, 0)) }, Officer);
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:32", new[] { Draft(category: CategoryId, start: new DateTime(2023, 2, 3, 9, 0, 0)) }, Officer);

			_creditRows.Select(c => c.PersonnelCertificationId).Should().BeEquivalentTo(new[] { 601, 602 },
				"R-T5: a record verified late does not move into the current cycle, and one outside every cycle credits nothing");
		}

		[Test]
		public async Task Credits_flow_from_the_day_the_mapping_is_turned_on_and_never_from_backfill()
		{
			MapCategory(new DateTime(2026, 6, 1));
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 700, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId });

			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:40", new[] { Draft(category: CategoryId, start: new DateTime(2026, 5, 20, 9, 0, 0)) }, Officer);
			var backfilled = Draft(category: CategoryId, start: new DateTime(2026, 7, 1, 9, 0, 0));
			backfilled.IsBackfill = true;
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:41", new[] { backfilled }, Officer);
			_creditRows.Should().BeEmpty("before the mapping's date, and backfilled history, need \"apply history to credits\" (R-T3)");

			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:42", new[] { Draft(category: CategoryId, start: new DateTime(2026, 7, 2, 9, 0, 0)) }, Officer);
			_creditRows.Should().ContainSingle();

			var backfill = _records.Live.Single(r => r.SourceRefId == "log:41");
			(await _writer.ApplyCreditsAsync(backfill, true, Officer)).Should().Be(1, "apply history credits a backfilled record on purpose");
		}

		[Test]
		public async Task A_manual_credit_writes_a_transcript_record_but_never_a_second_credit()
		{
			MapCategory(new DateTime(2020, 1, 1));
			_certificationRecords.Setup(c => c.GetByIdAsync(800)).ReturnsAsync(new PersonnelCertification { PersonnelCertificationId = 800, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId });
			_certifications.Setup(c => c.GetCertificationTypeByIdAsync(TypeId)).ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = TypeId, Type = "Paramedic", Code = "NRP" });

			var credit = new PersonnelCertificationCredit { PersonnelCertificationCreditId = 77, PersonnelCertificationId = 800, DepartmentId = DepartmentId, CreditDate = new DateTime(2026, 3, 1), Hours = 6m, AddedByUserId = Officer };
			await _writer.WriteManualCreditAsync(credit, Officer);

			var record = _records.Live.Single();
			record.SourceKind.Should().Be((int)LearningRecordSourceKinds.ManualCredit);
			record.Title.Should().Be("Paramedic credit", "the credit's own category and description may be protected and are not copied");
			_creditRows.Should().BeEmpty();

			await _writer.RemoveManualCreditAsync(credit, Officer);
			_records.Live.Should().BeEmpty();
		}

		[Test]
		public async Task Check_in_without_check_out_is_pending_and_a_check_out_supersedes_it_with_rounded_hours()
		{
			var item = new CalendarItem { CalendarItemId = 3, DepartmentId = DepartmentId, Title = "Thursday drill", ItemType = 9 };
			_calendarItems.Setup(c => c.GetByIdAsync(3)).ReturnsAsync(item);
			_calendarTypes.Setup(c => c.GetByIdAsync(9)).ReturnsAsync(new CalendarItemType { CalendarItemTypeId = 9, DepartmentId = DepartmentId, Name = "Drill", CountsAsTraining = true, DefaultCategoryId = CategoryId });

			var checkIn = new CalendarItemCheckIn { CalendarItemCheckInId = "c1", DepartmentId = DepartmentId, CalendarItemId = 3, UserId = Member, CheckInTime = new DateTime(2026, 6, 4, 18, 0, 0) };
			await _writer.WriteCalendarCheckInAsync(checkIn, Member);
			var pending = _records.Live.Single();
			pending.Status.Should().Be((int)LearningRecordStatuses.Pending, "R-T9: never a guess");
			pending.Hours.Should().Be(0m);

			checkIn.CheckOutTime = new DateTime(2026, 6, 4, 20, 8, 0);
			await _writer.WriteCalendarCheckInAsync(checkIn, Member);
			var closed = _records.Live.Single();
			closed.Status.Should().Be((int)LearningRecordStatuses.Verified);
			closed.Hours.Should().Be(2.25m, "2 h 08 min rounds to the nearest 15 minutes");
			closed.SupersedesId.Should().Be(pending.LearningRecordId);
			closed.CategoryId.Should().Be(CategoryId);
		}

		[Test]
		public async Task Attendance_on_a_type_that_is_not_training_writes_nothing()
		{
			_calendarItems.Setup(c => c.GetByIdAsync(4)).ReturnsAsync(new CalendarItem { CalendarItemId = 4, DepartmentId = DepartmentId, ItemType = 10 });
			_calendarTypes.Setup(c => c.GetByIdAsync(10)).ReturnsAsync(new CalendarItemType { CalendarItemTypeId = 10, DepartmentId = DepartmentId, Name = "Meeting" });

			await _writer.WriteCalendarCheckInAsync(new CalendarItemCheckIn { CalendarItemCheckInId = "c2", DepartmentId = DepartmentId, CalendarItemId = 4, UserId = Member, CheckInTime = DateTime.UtcNow, CheckOutTime = DateTime.UtcNow.AddHours(1) }, Member);

			_records.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Voiding_a_source_removes_every_record_and_credit_it_wrote()
		{
			MapCategory(new DateTime(2020, 1, 1));
			_holders.Add(new PersonnelCertification { PersonnelCertificationId = 900, DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = TypeId });
			await _writer.WriteSourceAsync(DepartmentId, LearningRecordSourceKinds.TrainingRecord, "log:50", new[] { Draft(category: CategoryId) }, Officer);

			var result = await _writer.RemoveTrainingLogAsync(DepartmentId, 50, Officer);

			result.Voided.Should().Be(1);
			result.CreditsRemoved.Should().Be(1);
			_records.Live.Should().BeEmpty();
			_records.Rows.Should().ContainSingle("voided records stay for the audit view");
		}

		[Test]
		public void A_training_log_produces_one_draft_per_participant_with_the_log_hours()
		{
			var log = new Log { LogId = 5, DepartmentId = DepartmentId, LogType = (int)LogTypes.Training, Course = "Vent", CourseCode = "V-1", StartedOn = new DateTime(2026, 1, 1, 9, 0, 0), EndedOn = new DateTime(2026, 1, 1, 12, 30, 0), Instructors = "Capt. Ray" };
			var drafts = LearningRecordWriter.LogDrafts(log, new[] { new LogUser { UserId = "a" }, new LogUser { UserId = "A" }, new LogUser { UnitId = 3 }, new LogUser { UserId = "b" } }, false);

			drafts.Should().HaveCount(2);
			drafts.Should().OnlyContain(d => d.Hours == 3.5m && d.CourseCode == "V-1" && d.InstructorNames == "Capt. Ray");
		}
	}
}
