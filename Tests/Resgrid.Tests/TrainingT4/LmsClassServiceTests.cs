using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT3;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.TrainingT4
{
	/// <summary>
	/// Training T4 part 1 (round 5B): the class, lesson-plan and worker-95 services with their stores in memory. Enrolment and the waitlist
	/// (a withdrawal promotes the next active member), approval, QR sign-in (an old code is refused; GPS optional), walk-ins and absent
	/// marking, the sign-off (one ClassSession record per attendee, none for the absent, the same drafts again on a second sign-off), the
	/// retention hold on the purge, the lesson-plan lifecycle, the roster-status audience, and worker 95 (due-work queries only, the gate
	/// once per department with due work and failing closed, inactive members skipped).
	/// </summary>
	[TestFixture]
	public class LmsClassServiceTests
	{
		private const int Dept = 7;
		private const string Manager = "manager-1";

		private sealed class Harness
		{
			public readonly MemoryTable<LmsClassSession> Sessions = new MemoryTable<LmsClassSession>();
			public readonly MemoryTable<LmsClassEnrollment> Enrollments = new MemoryTable<LmsClassEnrollment>();
			public readonly MemoryTable<LmsClassInstructor> Instructors = new MemoryTable<LmsClassInstructor>();
			public readonly MemoryTable<LmsLessonPlan> Plans = new MemoryTable<LmsLessonPlan>();
			public readonly MemoryTable<LmsLessonPlanVersion> Versions = new MemoryTable<LmsLessonPlanVersion>();
			public readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public readonly List<(LearningRecordSourceKinds Kind, string Ref, List<LearningRecordDraft> Drafts)> Writes = new();
			public readonly List<(string UserId, string MessageKey)> Notices = new();
			public readonly Mock<ILmsClassSessionsRepository> SessionRepo = new Mock<ILmsClassSessionsRepository>();
			public readonly Mock<ILmsClassEnrollmentsRepository> EnrollmentRepo = new Mock<ILmsClassEnrollmentsRepository>();
			public readonly Mock<ILmsAccessService> Access = new Mock<ILmsAccessService>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public readonly Mock<ILmsClassNotifier> Notifier = new Mock<ILmsClassNotifier>();
			public readonly Mock<ICalendarService> Calendar = new Mock<ICalendarService>();
			public readonly Mock<ILmsTrainingPlansRepository> TrainingPlans = new Mock<ILmsTrainingPlansRepository>();
			public readonly Mock<ILmsTrainingPlanItemsRepository> PlanItems = new Mock<ILmsTrainingPlanItemsRepository>();
			public LmsClassService Service;
			public LmsLessonPlanService LessonPlans;
			public LmsClassSweepService Sweep;

			public Harness(params string[] active)
			{
				foreach (var id in active)
					Active.Add(id);
				Active.Add(Manager);

				Sessions.Bind(SessionRepo);
				SessionRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Sessions.Rows.FirstOrDefault(s => s.DepartmentId == d && s.LmsClassSessionId == id));
				SessionRepo.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool>()))
					.ReturnsAsync((int d, DateTime? f, DateTime? t, bool del) => Sessions.Rows.Where(s => s.DepartmentId == d && (del || !s.IsDeleted)).ToList());
				SessionRepo.Setup(r => r.PurgeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, int id, DateTime now, CancellationToken _) => Sessions.Rows.RemoveAll(s => s.LmsClassSessionId == id && LmsClassRules.CanPurge(s, now)) == 1);

				Enrollments.Bind(EnrollmentRepo);
				EnrollmentRepo.Setup(r => r.GetForSessionAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int s) => Enrollments.Rows.Where(e => e.LmsClassSessionId == s).ToList());
				EnrollmentRepo.Setup(r => r.GetForSessionsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) => Enrollments.Rows.Where(e => ids.Contains(e.LmsClassSessionId)).ToList());
				EnrollmentRepo.Setup(r => r.GetForMemberAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
					.ReturnsAsync((int d, int s, string u) => Enrollments.Rows.FirstOrDefault(e => e.LmsClassSessionId == s && string.Equals(e.UserId, u, StringComparison.OrdinalIgnoreCase)));
				EnrollmentRepo.Setup(r => r.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, string u) => Enrollments.Rows.Where(e => string.Equals(e.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());
				EnrollmentRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Enrollments.Rows.FirstOrDefault(e => e.LmsClassEnrollmentId == id));
				EnrollmentRepo.Setup(r => r.PromoteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, int id, DateTime now, CancellationToken _) =>
				{
					var row = Enrollments.Rows.FirstOrDefault(e => e.LmsClassEnrollmentId == id && e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted);
					if (row == null)
						return false;
					row.Status = (int)LmsClassEnrollmentStatuses.Enrolled;
					row.WaitlistPosition = null;
					row.PromotedOn = now;
					return true;
				});
				EnrollmentRepo.Setup(r => r.ClaimReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, int id, DateTime now, CancellationToken _) =>
				{
					var row = Enrollments.Rows.First(e => e.LmsClassEnrollmentId == id);
					if (row.ReminderSentOn.HasValue)
						return false;
					row.ReminderSentOn = now;
					return true;
				});

				var instructorRepo = new Mock<ILmsClassInstructorsRepository>();
				Instructors.Bind(instructorRepo);
				instructorRepo.Setup(r => r.GetForSessionAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int s) => Instructors.Rows.Where(i => i.LmsClassSessionId == s).ToList());
				instructorRepo.Setup(r => r.GetForUserAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, string u) => Instructors.Rows.Where(i => string.Equals(i.UserId, u, StringComparison.OrdinalIgnoreCase)).ToList());

				var planRepo = new Mock<ILmsLessonPlansRepository>();
				Plans.Bind(planRepo);
				planRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Plans.Rows.FirstOrDefault(p => p.LmsLessonPlanId == id));
				var versionRepo = new Mock<ILmsLessonPlanVersionsRepository>();
				Versions.Bind(versionRepo);
				versionRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Versions.Rows.FirstOrDefault(v => v.LmsLessonPlanVersionId == id));
				versionRepo.Setup(r => r.GetForPlanAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int p) => Versions.Rows.Where(v => v.LmsLessonPlanId == p).OrderByDescending(v => v.LmsLessonPlanVersionId).ToList());
				versionRepo.Setup(r => r.MoveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, int id, int from, int to, CancellationToken _) =>
				{
					var row = Versions.Rows.FirstOrDefault(v => v.LmsLessonPlanVersionId == id && v.Status == from);
					if (row == null)
						return false;
					row.Status = to;
					return true;
				});

				Departments.Setup(d => d.GetActiveMemberUserIdsAsync(It.IsAny<int>())).ReturnsAsync(() => new HashSet<string>(Active, StringComparer.OrdinalIgnoreCase));
				Departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((string id, int dept, bool _) =>
					new DepartmentMember { UserId = id, DepartmentId = dept, IsDeleted = !Active.Contains(id) });
				Departments.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, Name = "Station 7", TimeZone = "UTC" });
				Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(() => Active.Select(id => new PersonName { UserId = id, FirstName = id, LastName = "X" }).ToList());
				Departments.Setup(d => d.GetAllUsersForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()))
					.ReturnsAsync(() => Active.Select(id => new IdentityUser { Id = id, Email = id + "@example.org" }).ToList());

				var settings = new Mock<IDepartmentSettingsService>();
				settings.Setup(s => s.GetTrainingRecordSettingsAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new TrainingRecordSettings());
				var learningRecords = new Mock<ILearningRecordService>();
				learningRecords.Setup(l => l.GetCategoriesAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync(new List<TrainingCategory> { new TrainingCategory { TrainingCategoryId = 4, DepartmentId = Dept, Name = "BBP", RetentionYears = 3 } });
				var writer = new Mock<ILearningRecordWriter>();
				writer.Setup(w => w.WriteSourceAsync(It.IsAny<int>(), It.IsAny<LearningRecordSourceKinds>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, LearningRecordSourceKinds k, string r, IReadOnlyCollection<LearningRecordDraft> drafts, string u, CancellationToken _) =>
					{
						Writes.Add((k, r, drafts.ToList()));
						return new LearningRecordWriteResult { Written = drafts.Count };
					});
				var groups = new Mock<IDepartmentGroupsService>();
				groups.Setup(g => g.GetAllGroupsForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 3, DepartmentId = Dept, Name = "Station 3" } });
				var roles = new Mock<IPersonnelRolesService>();
				roles.Setup(r => r.GetAllRolesForUsersInDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new Dictionary<string, List<PersonnelRole>>
				{
					{ "a", new List<PersonnelRole> { new PersonnelRole { Name = "Firefighter" }, new PersonnelRole { Name = "EMT" } } }
				});
				var certifications = new Mock<ICertificationService>();
				certifications.Setup(c => c.GetCertificationsByUserIdAsync(It.IsAny<string>())).ReturnsAsync(new List<PersonnelCertification>
				{
					new PersonnelCertification { DepartmentId = Dept, UserId = "lead", Name = "Instructor I", Number = "IFSAC-1" }
				});
				Calendar.Setup(c => c.GetCheckInsByCalendarItemAsync(It.IsAny<int>())).ReturnsAsync(new List<CalendarItemCheckIn>());
				Calendar.Setup(c => c.GetAllCalendarItemTypesForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<CalendarItemType>());
				var nextCalendarId = 900;
				Calendar.Setup(c => c.AddNewCalendarItemAsync(It.IsAny<CalendarItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((CalendarItem item, string tz, CancellationToken _) => { item.CalendarItemId = nextCalendarId++; return item; });
				var shifts = new Mock<IShiftsService>();
				shifts.Setup(s => s.GetShiftDaySchedulesForDateRangeAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>())).ReturnsAsync(new List<ShiftDaySchedule>());
				Notifier.Setup(n => n.SendAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<System.Globalization.CultureInfo, object[]>>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, IEnumerable<string> users, string t, string m, Func<System.Globalization.CultureInfo, object[]> a, string c, CancellationToken _) =>
					{
						var list = users.Where(Active.Contains).ToList();
						foreach (var user in list)
							Notices.Add((user, m));
						return list.Count;
					});

				Service = new LmsClassService(SessionRepo.Object, EnrollmentRepo.Object, instructorRepo.Object, Mock.Of<ILmsClassCostLinesRepository>(), Mock.Of<ILmsTrainingResourcesRepository>(),
					Mock.Of<ILmsClassResourceBookingsRepository>(), planRepo.Object, versionRepo.Object, TrainingPlans.Object, PlanItems.Object,
					Mock.Of<ILmsCourseService>(), Mock.Of<ITrainingService>(), Calendar.Object, Departments.Object, groups.Object, roles.Object, certifications.Object, settings.Object,
					learningRecords.Object, writer.Object, shifts.Object, Mock.Of<ILmsTrainingRequestService>(), Mock.Of<ILmsGateService>(), Notifier.Object, new UnavailableTrainingTimecardOffers(),
					new ILmsPostClassEvaluation[0], Mock.Of<IEventAggregator>());
				LessonPlans = new LmsLessonPlanService(planRepo.Object, versionRepo.Object, Mock.Of<IEventAggregator>());
				Sweep = new LmsClassSweepService(SessionRepo.Object, EnrollmentRepo.Object, Service, Access.Object, Departments.Object, Notifier.Object, Mock.Of<IEventAggregator>());
			}

			public LmsClassSession Add(int capacity, bool waitlist = true, bool approval = false, DateTime? starts = null, int department = Dept)
			{
				var start = starts ?? DateTime.UtcNow.AddDays(5);
				return Sessions.Save(new LmsClassSession
				{
					DepartmentId = department, CalendarItemId = 500, Title = "BBP refresher", StartsOn = start, EndsOn = start.AddHours(2), Capacity = capacity, WaitlistEnabled = waitlist,
					EnrollmentOpen = true, RequiresApproval = approval, AllowSelfUnenroll = true, QrEnabled = true, QrSecret = LmsCrewCodes.NewSecret(), CategoryId = 4, RetentionYears = 3,
					ReminderHours = 24
				});
			}
		}

		[Test]
		public async Task A_full_class_waitlists_and_a_withdrawal_promotes_the_next_active_member()
		{
			var h = new Harness("a", "b", "c", "gone");
			var session = h.Add(capacity: 1);
			var now = DateTime.UtcNow;

			(await h.Service.EnrollSelfAsync(Dept, session.LmsClassSessionId, "a", now)).Status.Should().Be((int)LmsClassEnrollmentStatuses.Enrolled);
			(await h.Service.EnrollSelfAsync(Dept, session.LmsClassSessionId, "gone", now)).Status.Should().Be((int)LmsClassEnrollmentStatuses.Waitlisted);
			var b = await h.Service.EnrollSelfAsync(Dept, session.LmsClassSessionId, "b", now);
			b.Status.Should().Be((int)LmsClassEnrollmentStatuses.Waitlisted);
			b.WaitlistPosition.Should().Be(2);
			(await h.Service.EnrollSelfAsync(Dept, session.LmsClassSessionId, "b", now)).LmsClassEnrollmentId.Should().Be(b.LmsClassEnrollmentId, "enrolling twice returns the same row");

			h.Active.Remove("gone");
			await h.Service.UnenrollSelfAsync(Dept, session.LmsClassSessionId, "a", now);

			h.Enrollments.Rows.Single(e => e.UserId == "b").Status.Should().Be((int)LmsClassEnrollmentStatuses.Enrolled, "b is the next active member");
			h.Enrollments.Rows.Single(e => e.UserId == "gone").Status.Should().Be((int)LmsClassEnrollmentStatuses.Waitlisted, "a member who left is passed over");
			h.Notices.Should().Contain(("b", "Notice.Promoted"));
		}

		[Test]
		public async Task Full_without_a_waitlist_refuses_and_an_approval_session_waits_for_a_manager()
		{
			var h = new Harness("a", "b");
			var closed = h.Add(capacity: 1, waitlist: false);
			await h.Service.EnrollSelfAsync(Dept, closed.LmsClassSessionId, "a", DateTime.UtcNow);
			Func<Task> full = () => h.Service.EnrollSelfAsync(Dept, closed.LmsClassSessionId, "b", DateTime.UtcNow);
			(await full.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_full");

			var approval = h.Add(capacity: 5, approval: true);
			var pending = await h.Service.EnrollSelfAsync(Dept, approval.LmsClassSessionId, "a", DateTime.UtcNow);
			pending.Status.Should().Be((int)LmsClassEnrollmentStatuses.PendingApproval);
			(await h.Service.DecideAsync(Dept, pending.LmsClassEnrollmentId, true, Manager, DateTime.UtcNow)).Status.Should().Be((int)LmsClassEnrollmentStatuses.Enrolled);
			h.Notices.Should().Contain(("a", "Notice.Enrolled"));

			Func<Task> again = () => h.Service.DecideAsync(Dept, pending.LmsClassEnrollmentId, false, Manager, DateTime.UtcNow);
			(await again.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_not_pending");

			Func<Task> notMember = () => h.Service.EnrollSelfAsync(Dept, approval.LmsClassSessionId, "stranger", DateTime.UtcNow);
			(await notMember.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_not_member");
		}

		[Test]
		public async Task Qr_sign_in_refuses_an_old_code_takes_gps_only_when_asked_and_adds_walk_ins()
		{
			var h = new Harness("a", "walker");
			var now = DateTime.UtcNow;
			var session = h.Add(capacity: 10, starts: now.AddMinutes(20));
			await h.Service.EnrollSelfAsync(Dept, session.LmsClassSessionId, "a", now);

			var old = LmsCrewCodes.CodeFor(session.QrSecret, LmsCrewCodes.Slice(now) - 3);
			Func<Task> stale = () => h.Service.SignInAsync(Dept, session.LmsClassSessionId, "a", old, null, null, now);
			(await stale.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_code_expired");

			var code = h.Service.CurrentCode(session, now);
			var signed = await h.Service.SignInAsync(Dept, session.LmsClassSessionId, "a", code, null, null, now);
			signed.CheckInOn.Should().Be(now);
			signed.CheckInMethod.Should().Be((int)LmsClassCheckInMethods.Qr);
			signed.CheckInLatitude.Should().BeNull("GPS was optional and not sent");

			var walkIn = await h.Service.SignInAsync(Dept, session.LmsClassSessionId, "walker", code, 39.7392m, -104.9903m, now);
			walkIn.IsWalkIn.Should().BeTrue();
			walkIn.Status.Should().Be((int)LmsClassEnrollmentStatuses.Enrolled);
			walkIn.CheckInLatitude.Should().Be(39.7392m);

			session.GpsRequired = true;
			session.GpsLatitude = 39.7392m;
			session.GpsLongitude = -104.9903m;
			session.GpsRadiusMeters = 200;
			Func<Task> noGps = () => h.Service.SignInAsync(Dept, session.LmsClassSessionId, "walker", code, null, null, now);
			(await noGps.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_gps_required");
		}

		[Test]
		public async Task Sign_off_writes_one_record_per_attendee_none_for_the_absent_and_the_same_records_again()
		{
			var h = new Harness("a", "b", "c", "lead");
			var start = DateTime.UtcNow.AddHours(-3);
			var session = h.Add(capacity: 10, starts: start);
			foreach (var user in new[] { "a", "b", "c" })
				h.Enrollments.Save(new LmsClassEnrollment { DepartmentId = Dept, LmsClassSessionId = session.LmsClassSessionId, UserId = user, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = start.AddDays(-2) });
			var a = h.Enrollments.Rows.Single(e => e.UserId == "a");
			a.CheckInOn = start;
			a.CheckOutOn = start.AddMinutes(90);
			a.CheckInMethod = (int)LmsClassCheckInMethods.Qr;
			await h.Service.MarkAttendanceAsync(Dept, h.Enrollments.Rows.Single(e => e.UserId == "b").LmsClassEnrollmentId, LmsClassAttendanceStates.Attended, null, Manager, DateTime.UtcNow);
			h.Instructors.Save(new LmsClassInstructor { DepartmentId = Dept, LmsClassSessionId = session.LmsClassSessionId, UserId = "lead" });

			var result = await h.Service.SignOffAsync(Dept, session.LmsClassSessionId, "lead", DateTime.UtcNow);

			result.Attended.Should().Be(2);
			result.Absent.Should().Be(1, "c never signed in and nobody marked them: absent");
			h.Writes.Should().ContainSingle();
			var write = h.Writes[0];
			write.Kind.Should().Be(LearningRecordSourceKinds.ClassSession);
			write.Ref.Should().Be(session.LmsClassSessionId.ToString());
			write.Drafts.Where(d => d.ComponentKey == string.Empty).Select(d => d.UserId).Should().BeEquivalentTo("a", "b");
			write.Drafts.Single(d => d.UserId == "a").Hours.Should().Be(1.5m);
			write.Drafts.Single(d => d.UserId == "b").Hours.Should().Be(2m);
			write.Drafts.Should().NotContain(d => d.UserId == "c");
			write.Drafts.Single(d => d.ComponentKey == LmsClassRules.InstructorComponent).UserId.Should().Be("lead");

			var signed = h.Sessions.Rows.Single(s => s.LmsClassSessionId == session.LmsClassSessionId);
			signed.Status.Should().Be((int)LmsClassSessionStatuses.SignedOff);
			signed.RetainUntil.Should().Be(signed.EndsOn.AddYears(3));
			a.JobTitle.Should().Be("Firefighter, EMT", "the roster keeps the job titles");
			a.MemberName.Should().Be("a X");
			h.Instructors.Rows.Single().Qualifications.Should().Be("Instructor I (IFSAC-1)", "the trainer's qualifications come from their live certifications");

			await h.Service.SignOffAsync(Dept, session.LmsClassSessionId, "lead", DateTime.UtcNow);
			h.Writes.Should().HaveCount(2);
			h.Writes[1].Drafts.Select(d => (d.UserId, d.ComponentKey, d.Hours, d.SessionKey)).Should().Equal(write.Drafts.Select(d => (d.UserId, d.ComponentKey, d.Hours, d.SessionKey)),
				"signing again hands the writer the same keys, so nothing is written twice");
		}

		[Test]
		public async Task A_class_cannot_be_signed_off_before_it_starts_or_after_it_was_cancelled()
		{
			var h = new Harness("a");
			var future = h.Add(capacity: 5);
			Func<Task> early = () => h.Service.SignOffAsync(Dept, future.LmsClassSessionId, Manager, DateTime.UtcNow);
			(await early.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_not_started");

			await h.Service.EnrollSelfAsync(Dept, future.LmsClassSessionId, "a", DateTime.UtcNow);
			await h.Service.CancelAsync(Dept, future.LmsClassSessionId, "Weather", Manager);
			h.Notices.Should().Contain(("a", "Notice.Cancelled"));
			Func<Task> cancelled = () => h.Service.SignOffAsync(Dept, future.LmsClassSessionId, Manager, DateTime.UtcNow.AddDays(10));
			(await cancelled.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_cancelled");
		}

		[Test]
		public async Task Roster_retention_blocks_the_purge_until_it_has_passed()
		{
			var h = new Harness("a");
			var session = h.Add(capacity: 5, starts: DateTime.UtcNow.AddHours(-3));
			await h.Service.SignOffAsync(Dept, session.LmsClassSessionId, Manager, DateTime.UtcNow);
			await h.Service.DeleteAsync(Dept, session.LmsClassSessionId, Manager);

			Func<Task> early = () => h.Service.PurgeAsync(Dept, session.LmsClassSessionId, DateTime.UtcNow.AddYears(1));
			(await early.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_retention_hold");
			h.Sessions.Rows.Should().ContainSingle("the roster is the legal record until its retention ends");

			(await h.Service.PurgeAsync(Dept, session.LmsClassSessionId, DateTime.UtcNow.AddYears(3).AddDays(2))).Should().BeTrue();
			h.Sessions.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_lesson_plan_is_drafted_submitted_approved_and_published_and_a_new_version_supersedes_it()
		{
			var h = new Harness();
			var plan = await h.LessonPlans.ApplyStarterAsync(Dept, "live-fire", Manager);
			plan.StarterKey.Should().Be("live-fire");
			var draft = (await h.LessonPlans.GetVersionsAsync(Dept, plan.LmsLessonPlanId)).Single();
			draft.Content().PreBriefChecklist.Should().NotBeEmpty();

			Func<Task> noSummary = () => h.LessonPlans.SubmitAsync(Dept, draft.LmsLessonPlanVersionId, " ", Manager);
			(await noSummary.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_change_summary_required");
			Func<Task> notApproved = () => h.LessonPlans.PublishAsync(Dept, draft.LmsLessonPlanVersionId, false, Manager);
			(await notApproved.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_plan_not_approved");

			await h.LessonPlans.SaveDraftAsync(Dept, draft.LmsLessonPlanVersionId, "Live fire at the burn building", new LmsLessonPlanContent { Objectives = { "Advance a line" }, StudentsPerInstructor = 5 }, Manager);
			var submitted = await h.LessonPlans.SubmitAsync(Dept, draft.LmsLessonPlanVersionId, "First version", Manager);
			submitted.ContentHash.Should().Be(LmsClassRules.Hash(submitted.ContentJson));
			Func<Task> edit = () => h.LessonPlans.SaveDraftAsync(Dept, draft.LmsLessonPlanVersionId, "x", new LmsLessonPlanContent(), Manager);
			(await edit.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_plan_not_draft", "a submitted version is locked");
			Func<Task> noReason = () => h.LessonPlans.DecideAsync(Dept, draft.LmsLessonPlanVersionId, false, null, Manager);
			(await noReason.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_reason_required");

			await h.LessonPlans.DecideAsync(Dept, draft.LmsLessonPlanVersionId, true, null, Manager);
			var first = await h.LessonPlans.PublishAsync(Dept, draft.LmsLessonPlanVersionId, false, Manager);
			first.Number.Should().Be("1.0");
			h.Plans.Rows.Single().CurrentVersionId.Should().Be(first.LmsLessonPlanVersionId);

			var next = await h.LessonPlans.NewDraftAsync(Dept, plan.LmsLessonPlanId, Manager);
			(await h.LessonPlans.NewDraftAsync(Dept, plan.LmsLessonPlanId, Manager)).LmsLessonPlanVersionId.Should().Be(next.LmsLessonPlanVersionId, "one open draft at a time");
			next.Content().Objectives.Should().Equal("Advance a line");
			await h.LessonPlans.SubmitAsync(Dept, next.LmsLessonPlanVersionId, "Ratios", Manager);
			next.ContentJson = new LmsLessonPlanContent { Objectives = { "Changed after submission" } }.ToJson();
			Func<Task> changed = () => h.LessonPlans.DecideAsync(Dept, next.LmsLessonPlanVersionId, true, null, Manager);
			(await changed.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_plan_changed", "the approval is bound to the submitted content");
			next.ContentJson = first.ContentJson;
			await h.LessonPlans.DecideAsync(Dept, next.LmsLessonPlanVersionId, true, "ok", Manager);
			(await h.LessonPlans.PublishAsync(Dept, next.LmsLessonPlanVersionId, false, Manager)).Number.Should().Be("1.1");
			h.Versions.Rows.Single(v => v.LmsLessonPlanVersionId == first.LmsLessonPlanVersionId).Status.Should().Be((int)ProtocolVersionStatuses.Superseded);
		}

		[Test]
		public async Task The_roster_status_audience_resolves_active_members_by_state()
		{
			var h = new Harness("a", "b", "c");
			var session = h.Add(capacity: 10);
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = session.LmsClassSessionId, DepartmentId = Dept, UserId = "a", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Attended });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = session.LmsClassSessionId, DepartmentId = Dept, UserId = "b", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Absent });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = session.LmsClassSessionId, DepartmentId = Dept, UserId = "c", Status = (int)LmsClassEnrollmentStatuses.Waitlisted });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = session.LmsClassSessionId, DepartmentId = Dept, UserId = "gone", Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Absent });
			h.Access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(true);
			h.Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(true);
			var audience = new LmsEventRosterAudienceService(h.SessionRepo.Object, h.EnrollmentRepo.Object, h.Departments.Object, h.Access.Object);

			(await audience.IsAvailableAsync(Dept)).Should().BeTrue();
			(await audience.ResolveAsync(Dept, session.LmsClassSessionId, EventRosterAudienceStatuses.Enrolled)).Should().BeEquivalentTo("a", "b");
			(await audience.ResolveAsync(Dept, session.LmsClassSessionId, EventRosterAudienceStatuses.Attended)).Should().BeEquivalentTo("a");
			(await audience.ResolveAsync(Dept, session.LmsClassSessionId, EventRosterAudienceStatuses.Absent)).Should().BeEquivalentTo(new[] { "b" }, "the member who left is never addressed");
			(await audience.ResolveAsync(Dept, session.LmsClassSessionId, EventRosterAudienceStatuses.Waitlisted)).Should().BeEquivalentTo("c");
			(await audience.ResolveAsync(99, session.LmsClassSessionId, EventRosterAudienceStatuses.Enrolled)).Should().BeEmpty("department scoped");

			h.Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ThrowsAsync(new Exception("billing down"));
			(await audience.IsAvailableAsync(Dept)).Should().BeFalse("the gate fails closed");
		}

		[Test]
		public async Task Worker_95_reminds_and_promotes_from_due_work_only_with_the_gate_once_per_department_failing_closed()
		{
			var h = new Harness("a", "b", "c", "x");
			var now = DateTime.UtcNow;
			var open = h.Add(capacity: 1, starts: now.AddHours(10));
			var closedDept = h.Add(capacity: 5, starts: now.AddHours(12), department: 8);
			var later = h.Add(capacity: 5, starts: now.AddDays(5));
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = open.LmsClassSessionId, DepartmentId = Dept, UserId = "a", Status = (int)LmsClassEnrollmentStatuses.Enrolled });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = open.LmsClassSessionId, DepartmentId = Dept, UserId = "gone", Status = (int)LmsClassEnrollmentStatuses.Enrolled });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = closedDept.LmsClassSessionId, DepartmentId = 8, UserId = "x", Status = (int)LmsClassEnrollmentStatuses.Enrolled });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = later.LmsClassSessionId, DepartmentId = Dept, UserId = "b", Status = (int)LmsClassEnrollmentStatuses.Enrolled });
			h.Enrollments.Save(new LmsClassEnrollment { LmsClassSessionId = later.LmsClassSessionId, DepartmentId = Dept, UserId = "c", Status = (int)LmsClassEnrollmentStatuses.Waitlisted, WaitlistPosition = 1 });

			h.SessionRepo.Setup(r => r.GetReminderCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<LmsClassSession> { open, closedDept, later });
			h.SessionRepo.Setup(r => r.GetWaitlistCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<LmsClassSession> { later });
			h.SessionRepo.Setup(r => r.GetPurgeCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<LmsClassSession>());
			h.Access.Setup(a => a.CanAuthorAndManageAsync(Dept)).ReturnsAsync(true);
			h.Access.Setup(a => a.CanAuthorAndManageAsync(8)).ThrowsAsync(new Exception("billing down"));

			var result = await LmsClassesLogic.Process(h.Sweep, now);

			result.Reminders.Should().Be(1, "only a, in the reminder window of an open department; the member who left is skipped");
			h.Notices.Should().Contain(("a", "Notice.Reminder")).And.NotContain(n => n.UserId == "x" || n.UserId == "gone" || n.UserId == "b");
			h.Enrollments.Rows.Single(e => e.UserId == "a").ReminderSentOn.Should().NotBeNull("claimed before sending");
			h.Enrollments.Rows.Single(e => e.UserId == "gone").ReminderSentOn.Should().BeNull();
			result.Promoted.Should().Be(1, "capacity 5 has room for the waitlisted member");
			result.GateClosed.Should().Be(1, "department 8's gate failed and its work waits");
			h.Access.Verify(a => a.CanAuthorAndManageAsync(Dept), Times.Once, "the gate is asked once per department per run");
			h.Access.Verify(a => a.CanAuthorAndManageAsync(It.Is<int>(d => d != Dept && d != 8)), Times.Never);
			h.SessionRepo.Verify(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool>()), Times.Never, "no per-department sweep");

			var again = await LmsClassesLogic.Process(h.Sweep, now);
			again.Reminders.Should().Be(0, "a reminder is sent once");
		}

		[Test]
		public async Task Worker_95_failures_are_counted_not_thrown()
		{
			var sweep = new Mock<ILmsClassSweepService>();
			sweep.Setup(s => s.RunDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("database down"));
			(await LmsClassesLogic.Process(sweep.Object, DateTime.UtcNow)).Failures.Should().Be(1);
		}

		[Test]
		public async Task A_meeting_report_marks_matched_members_attended_with_their_minutes()
		{
			var h = new Harness("a", "b");
			var session = h.Add(capacity: 10, starts: DateTime.UtcNow.AddHours(-2));
			await h.Service.EnrollMemberAsync(Dept, session.LmsClassSessionId, "a", Manager, DateTime.UtcNow.AddHours(-3));
			var csv = "Name (Original Name),User Email,Duration (Minutes)\nA X,a@example.org,50\nB X,,40\nUnknown Person,nobody@example.org,30\n";

			var result = await h.Service.ImportAttendanceAsync(Dept, session.LmsClassSessionId, csv, Manager, DateTime.UtcNow);

			result.Matched.Should().Be(2, "a by e-mail, b by name");
			result.WalkIns.Should().Be(1);
			result.Unmatched.Select(r => r.Name).Should().Equal("Unknown Person");
			h.Enrollments.Rows.Single(e => e.UserId == "a").ImportedMinutes.Should().Be(50);
			h.Enrollments.Rows.Single(e => e.UserId == "b").IsWalkIn.Should().BeTrue();
			h.Enrollments.Rows.Should().OnlyContain(e => e.Attendance == (int)LmsClassAttendanceStates.Attended);
		}
		[Test]
		public async Task A_weekly_series_creates_a_session_and_calendar_item_per_week_and_a_plan_line_becomes_a_class()
		{
			var h = new Harness("lead");
			var start = new DateTime(2027, 4, 6, 19, 0, 0);
			var created = await h.Service.CreateAsync(Dept, new LmsClassSessionInput
			{
				Title = "Driver training", LocalStart = start, LocalEnd = start.AddHours(3), Capacity = 6, WaitlistEnabled = true, EnrollmentOpen = true, QrEnabled = true,
				CategoryId = 4, Repeat = LmsClassRepeatKinds.Weekly, Occurrences = 3, InstructorUserIds = { "lead" }, RestrictGroupIds = { 3, 999 }, MeetingUrl = "https://example.org/meet"
			}, Manager);

			created.Should().HaveCount(3);
			created.Select(s => s.SeriesKey).Distinct().Should().ContainSingle().Which.Should().NotBeNullOrEmpty();
			created.Select(s => s.StartsOn).Should().Equal(start, start.AddDays(7), start.AddDays(14));
			created.Select(s => s.CalendarItemId).Should().OnlyHaveUniqueItems().And.NotContainNulls();
			created.Should().OnlyContain(s => s.RestrictGroupIds == "3", "a group of another department is dropped");
			created.Should().OnlyContain(s => s.RetentionYears == 3, "the category's retention");
			created.Select(s => s.QrSecret).Should().OnlyHaveUniqueItems("each session has its own rotating code");
			h.Instructors.Rows.Should().HaveCount(3).And.OnlyContain(i => i.UserId == "lead" && i.Role == (int)LmsClassInstructorRoles.Lead);
			h.Calendar.Verify(c => c.AddNewCalendarItemAsync(It.Is<CalendarItem>(i => i.CheckInType == (int)CalendarItemCheckInTypes.Disabled), "UTC", It.IsAny<CancellationToken>()), Times.Exactly(3));

			Func<Task> badUrl = () => h.Service.CreateAsync(Dept, new LmsClassSessionInput { Title = "x", LocalStart = start, LocalEnd = start.AddHours(1), MeetingUrl = "javascript:alert(1)" }, Manager);
			(await badUrl.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_meeting_url_invalid");
			Func<Task> backwards = () => h.Service.CreateAsync(Dept, new LmsClassSessionInput { Title = "x", LocalStart = start, LocalEnd = start }, Manager);
			(await backwards.Should().ThrowAsync<InvalidOperationException>()).WithMessage("lms_class_times_invalid");

			var line = new LmsTrainingPlanItem { LmsTrainingPlanItemId = 41, LmsTrainingPlanId = 40, DepartmentId = Dept, Month = 5, Title = "Hose evolutions", PlannedHours = 2m, CategoryId = 4 };
			h.PlanItems.Setup(r => r.GetByIdForDepartmentAsync(Dept, 41)).ReturnsAsync(line);
			h.PlanItems.Setup(r => r.SaveOrUpdateAsync(It.IsAny<LmsTrainingPlanItem>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsTrainingPlanItem i, CancellationToken _, bool __) => i);
			h.TrainingPlans.Setup(r => r.GetByIdForDepartmentAsync(Dept, 40)).ReturnsAsync(new LmsTrainingPlan { LmsTrainingPlanId = 40, DepartmentId = Dept, Year = 2027, Name = "2027" });

			var fromPlan = await h.Service.CreateFromPlanItemAsync(Dept, 41, Manager);
			fromPlan.LmsTrainingPlanItemId.Should().Be(41);
			fromPlan.StartsOn.Should().Be(new DateTime(2027, 5, 3, 9, 0, 0), "the first weekday of the month at 09:00");
			fromPlan.EndsOn.Should().Be(new DateTime(2027, 5, 3, 11, 0, 0));
			line.ClassSessionId.Should().Be(fromPlan.LmsClassSessionId, "the round-4C seam now holds the class session");
			line.CalendarItemId.Should().Be(fromPlan.CalendarItemId);
			(await h.Service.CreateFromPlanItemAsync(Dept, 41, Manager)).LmsClassSessionId.Should().Be(fromPlan.LmsClassSessionId, "a line becomes one class");
		}
	}
}
