using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Mocks;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT6
{
	/// <summary>
	/// Round 9A end to end: a course completion an instructor recorded issues (or renews) the course's certification through the real learner,
	/// writer, ledger and issuance engine; voiding the recording reopens the enrolment and the certification goes back (withdrawn, or its
	/// previous expiry restored) unless the member's own later completion still covers it.
	/// </summary>
	[TestFixture]
	public class CertificationVoidEndToEndTests
	{
		private const int D = LmsWorld.DepartmentId;

		private static TrainingCompletionRecording Recording(int id, int courseId) => new TrainingCompletionRecording
		{
			TrainingCompletionRecordingId = id, DepartmentId = D, UserId = LmsWorld.Member, TargetKind = (int)TrainingCompletionRecordingTargets.Course, TargetId = courseId,
			SourceKind = (int)TrainingCompletionRecordingSources.Manual, RecordedByUserId = LmsWorld.Admin, RecordedOn = DateTime.UtcNow, CompletedOn = DateTime.UtcNow.AddMinutes(-30),
			AttestationVersion = TrainingCompletionAttestation.CurrentVersion, Note = "Group session"
		};

		private sealed class Harness
		{
			public readonly LmsWorld World = new LmsWorld();
			public readonly TrainingCompletionRecordingService Recordings;
			public readonly List<TrainingCompletionRecording> Stored = new List<TrainingCompletionRecording>();
			public LmsCourse Course;
			public List<LmsLesson> Lessons;

			public Harness()
			{
				World.CertificationTypes[8] = new DepartmentCertificationType
				{
					DepartmentCertificationTypeId = 8, DepartmentId = D, Type = "Forklift operator", AppliesTo = (int)CertificationAppliesTo.Person, DefaultValidityMonths = 12, IsActive = true
				};
				World.CertificationService.Setup(c => c.SoftDeleteCertificationAsync(It.IsAny<int>(), D, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int id, int d, string u, CancellationToken _) => { World.Certifications.Single(c => c.PersonnelCertificationId == id).IsDeleted = true; return true; });
				World.CertificationService.Setup(c => c.RollBackCertificationAsync(It.IsAny<int>(), D, It.IsAny<PersonnelCertificationStatuses>(), It.IsAny<DateTime?>(),
						It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int id, int d, PersonnelCertificationStatuses status, DateTime? expires, DateTime? received, string reason, string user, CancellationToken _) =>
					{
						var record = World.Certifications.Single(c => c.PersonnelCertificationId == id);
						record.Status = (int)status;
						record.ExpiresOn = expires;
						record.RecievedOn = received;
						return record;
					});
				// The LMS side of the baseline seam: no requirement set here, so only voids are acted on.
				World.Observers.Add(new LmsCertificationLearningRecordObserver(World.CertificationIssuance, Mock.Of<ILmsCertificationRequirementService>(),
					Mock.Of<ILmsCertificationRequirementSetsRepository>(), Mock.Of<ILmsGateService>()));

				var repository = new Mock<ITrainingCompletionRecordingsRepository>();
				repository.Setup(r => r.GetByIdForDepartmentAsync(D, It.IsAny<int>())).ReturnsAsync((int d, int id) => Stored.SingleOrDefault(r => r.TrainingCompletionRecordingId == id));
				repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<TrainingCompletionRecording>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((TrainingCompletionRecording r, CancellationToken _, bool __) => r);
				var authorization = new Mock<IAuthorizationService>();
				authorization.Setup(a => a.GetTrainingManagementScopeAsync(It.IsAny<string>(), D)).ReturnsAsync((string user, int d) =>
				{
					var scope = TrainingManagementScope.None(user, d);
					if (user == LmsWorld.Admin)
						scope.Record = new TrainingRightScope { ManagesAll = true };
					return scope;
				});
				Recordings = new TrainingCompletionRecordingService(repository.Object, Mock.Of<ITrainingService>(), Mock.Of<ITrainingUserRepository>(), World.Records, World.Writer,
					authorization.Object, World.Departments.Object,
					new ITrainingCompletionRecordingTarget[] { new LmsCourseCompletionRecordingTarget(World.Enrollments, World.Progress, World.Records, World.Writer) },
					Mock.Of<IEventAggregator>(), Mock.Of<ITrainingRecordNoticeService>());
			}

			public async Task<TrainingCompletionRecording> RecordAsync()
			{
				LmsCourseVersion version;
				(Course, version, Lessons) = await World.PublishedCourseAsync("Forklift", LmsWorld.Reading("Daily checks"));
				World.Versions.Rows.Single(v => v.LmsCourseVersionId == version.LmsCourseVersionId).IssuesCertificationTypeId = 8;
				var recording = Recording(61, Course.LmsCourseId);
				(await World.Learner.RecordCompletionForMemberAsync(D, Course.LmsCourseId, LmsWorld.Member, recording)).Succeeded.Should().BeTrue();
				recording.EnrollmentId = World.Enrollments.Rows.Single().LmsEnrollmentId;
				recording.LearningRecordId = World.Records.Live.Single().LearningRecordId;
				Stored.Add(recording);
				return recording;
			}

			public void Notified(string messageKey, Times times) => World.CertificationNotifier.Verify(n => n.SendAsync(D, It.Is<IEnumerable<string>>(u => u.Single() == LmsWorld.Member),
				"CertReq.Notify.Title", messageKey, It.IsAny<Func<CultureInfo, object[]>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), times);
		}

		[Test]
		public async Task Voiding_a_recorded_course_completion_takes_back_the_certification_it_issued()
		{
			var h = new Harness();
			await h.RecordAsync();
			var certification = h.World.Certifications.Should().ContainSingle().Subject;
			var issuance = h.World.Issuances.Should().ContainSingle().Subject;
			issuance.Source.Should().Be((int)LmsCertificationIssueSources.CourseCompletion);
			issuance.Basis.Should().Equal(h.World.Records.Rows.Single().LearningRecordId);
			h.Notified("CertReq.Notify.Issued", Times.Once());

			var voided = await h.Recordings.VoidAsync(D, 61, LmsWorld.Admin, "Recorded for the wrong member");

			voided.Refusal.Should().BeNull();
			h.World.Enrollments.Rows.Single().IsOpen.Should().BeTrue("the enrolment reopened");
			certification.IsDeleted.Should().BeTrue("the certification the completion issued is withdrawn");
			issuance.State.Should().Be((int)LmsCertificationIssuanceStates.RolledBack);
			issuance.RolledBackByUserId.Should().Be(LmsWorld.Admin);
			h.Notified("CertReq.Notify.Withdrawn", Times.Once());
		}

		[Test]
		public async Task Voiding_a_recorded_course_completion_restores_the_expiry_it_renewed()
		{
			var h = new Harness();
			var expiring = DateTime.UtcNow.Date.AddDays(10);
			var received = DateTime.UtcNow.Date.AddMonths(-12).AddDays(10);
			h.World.Certifications.Add(new PersonnelCertification
			{
				PersonnelCertificationId = 77, DepartmentId = D, UserId = LmsWorld.Member, DepartmentCertificationTypeId = 8, ExpiresOn = expiring, RecievedOn = received,
				Status = (int)PersonnelCertificationStatuses.Active
			});
			await h.RecordAsync();
			var record = h.World.Certifications.Single();
			record.ExpiresOn.Should().BeAfter(expiring, "the course renewed it");

			await h.Recordings.VoidAsync(D, 61, LmsWorld.Admin, "Not this class");

			record.ExpiresOn.Should().Be(expiring);
			record.RecievedOn.Should().Be(received);
			record.Status.Should().Be((int)PersonnelCertificationStatuses.Active);
			record.IsDeleted.Should().BeFalse();
			h.Notified("CertReq.Notify.Restored", Times.Once());
		}

		[Test]
		public async Task The_members_own_later_completion_keeps_the_certification_when_the_recorded_one_is_voided()
		{
			var h = new Harness();
			await h.RecordAsync();
			var certification = h.World.Certifications.Single();
			var own = await h.World.Learner.EnrollSelfAsync(h.World.Actor(LmsWorld.Member), h.Course.LmsCourseId);
			(await h.World.Learner.CompleteLessonAsync(D, LmsWorld.Member, own.Enrollment.LmsEnrollmentId, h.Lessons[0].LmsLessonId, null)).Count.Should().Be(1);

			await h.Recordings.VoidAsync(D, 61, LmsWorld.Admin, "Recorded twice");

			certification.IsDeleted.Should().BeFalse("the member's own completion still covers it");
			var issuance = h.World.Issuances.First();
			issuance.State.Should().Be((int)LmsCertificationIssuanceStates.Live);
			issuance.Basis.Should().Equal(h.World.Records.Live.Single().LearningRecordId);
			h.Notified("CertReq.Notify.Withdrawn", Times.Never());
		}
	}

	/// <summary>
	/// Round 9A: a yearly required reading (an employee handbook) end to end on the baseline Trainings: opening it does not complete it, the
	/// member's confirmation does (and goes on the transcript), and the yearly repeat assigns it again with a new due date.
	/// </summary>
	[TestFixture]
	public class HandbookReadAndConfirmTests
	{
		private const int DepartmentId = 12;
		private static readonly Department Department = new Department { DepartmentId = DepartmentId, TimeZone = "Romance Standard Time" };

		[Test]
		public async Task A_handbook_review_completes_only_on_confirmation_and_comes_round_again_next_year()
		{
			var trainings = new MockTrainingRepository();
			var users = new MockTrainingUserRepository();
			var questions = new MockTrainingQuestionRepository();
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(Department);
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string>(new[] { "staff" }, StringComparer.OrdinalIgnoreCase));
			var writer = new Mock<ILearningRecordWriter>();
			var service = new TrainingService(trainings, new MockTrainingAttachmentRepository(), users, questions, Mock.Of<ICommunicationService>(), departments.Object,
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IKnowledgeIndexQueue>(), Mock.Of<IUserProfileService>(), null, Mock.Of<ITrainingQuizAttemptsRepository>(), null,
				Mock.Of<ITrainingCompletionHistoriesRepository>(), Mock.Of<ITrainingQuestionFlagsRepository>(), Mock.Of<IMemberLeaveService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentGroupsService>(), new Lazy<ILearningRecordWriter>(() => writer.Object));
			var assignments = new TrainingAssignmentService(trainings, users, Mock.Of<ITrainingAudiencesRepository>(), Mock.Of<ITrainingCompletionHistoriesRepository>(),
				Mock.Of<IContentAudienceService>(), departments.Object, Mock.Of<IEventAggregator>());

			var now = DateTime.UtcNow;
			var handbook = new Training
			{
				TrainingId = 3, DepartmentId = DepartmentId, Name = "Employee handbook 2026", Description = "Read the handbook", TrainingText = "...", CreatedByUserId = "hr",
				CreatedOn = now.AddDays(-2), DueDays = 30, RequiresAcknowledgment = true, RepeatMonths = 12, NextRepeatOn = now.AddYears(1), Users = new List<TrainingUser>()
			};
			trainings.SeedTraining(handbook);
			var row = new TrainingUser { TrainingUserId = 1, TrainingId = 3, UserId = "staff", AssignedOn = now.AddDays(-1), DueOn = now.AddDays(29) };
			users.SeedUser(row);
			handbook.Users.Add(row);

			var cycleBefore = row.Cycle;
			var viewed = await service.SetTrainingAsViewedAsync(3, "staff");
			viewed.Viewed.Should().BeTrue();
			viewed.Complete.Should().BeFalse("opening a read-and-confirm training is not reading it");
			writer.Verify(w => w.WriteCourseCompletionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

			(await service.AcknowledgeTrainingAsync(DepartmentId, 3, "someone-else")).Should().BeNull("only an assignee confirms");
			var confirmed = await service.AcknowledgeTrainingAsync(DepartmentId, 3, "staff");
			confirmed.Complete.Should().BeTrue();
			confirmed.CompletedOn.Should().NotBeNull();
			writer.Verify(w => w.WriteCourseCompletionAsync(DepartmentId, 3, "staff", false, It.IsAny<CancellationToken>()), Times.Once, "the confirmation goes on the transcript");
			(await service.AcknowledgeTrainingAsync(DepartmentId, 3, "staff")).Complete.Should().BeTrue("confirming twice changes nothing");
			writer.Verify(w => w.WriteCourseCompletionAsync(DepartmentId, 3, "staff", false, It.IsAny<CancellationToken>()), Times.Once);

			// A year later the repeat assigns it again: open, with a new due date and a new cycle.
			handbook.NextRepeatOn = now.AddMinutes(-1);
			(await assignments.RunRepeatAsync(handbook, now)).Should().Be(1);
			var again = (await users.GetAllAsync()).Single();
			again.Complete.Should().BeFalse();
			again.Cycle.Should().Be(cycleBefore + 1, "a new assignment cycle");
			again.DueOn.Should().Be(TrainingDueDates.DueForAssignment(handbook, now, Department));
		}

		[Test]
		public async Task A_training_with_a_quiz_or_without_the_flag_is_not_confirmed_by_the_button()
		{
			var trainings = new MockTrainingRepository();
			var users = new MockTrainingUserRepository();
			var questions = new MockTrainingQuestionRepository();
			var service = new TrainingService(trainings, new MockTrainingAttachmentRepository(), users, questions, Mock.Of<ICommunicationService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IKnowledgeIndexQueue>(), Mock.Of<IUserProfileService>());
			trainings.SeedTraining(new Training
			{
				TrainingId = 4, DepartmentId = DepartmentId, Name = "Plain", CreatedOn = DateTime.UtcNow.AddDays(-1),
				Users = new List<TrainingUser> { new TrainingUser { TrainingUserId = 4, TrainingId = 4, UserId = "staff" } }
			});
			users.SeedUser(new TrainingUser { TrainingUserId = 4, TrainingId = 4, UserId = "staff" });
			(await service.AcknowledgeTrainingAsync(DepartmentId, 4, "staff")).Should().BeNull("the training does not ask for a confirmation");

			trainings.SeedTraining(new Training
			{
				TrainingId = 5, DepartmentId = DepartmentId, Name = "Quizzed", RequiresAcknowledgment = true, CreatedOn = DateTime.UtcNow.AddDays(-1),
				Users = new List<TrainingUser> { new TrainingUser { TrainingUserId = 5, TrainingId = 5, UserId = "staff" } }
			});
			users.SeedUser(new TrainingUser { TrainingUserId = 5, TrainingId = 5, UserId = "staff" });
			questions.SeedQuestion(new TrainingQuestion { TrainingQuestionId = 50, TrainingId = 5, Question = "?" });
			(await service.AcknowledgeTrainingAsync(DepartmentId, 5, "staff")).Should().BeNull("passing the quiz completes a quizzed training");
		}
	}

	/// <summary>
	/// Round 9A: certification notices go through the normal notification path (SendNotificationAsync: the member's own channel choices for
	/// push, email and SMS apply), once per recipient, in their language, never to inactive members.
	/// </summary>
	[TestFixture]
	public class CertificationNoticeDeliveryTests
	{
		[Test]
		public async Task Certification_notices_go_once_through_the_members_channels_and_never_to_inactive_members()
		{
			var bypass = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			try
			{
				var communication = new Mock<ICommunicationService>();
				communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), 9, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
					It.IsAny<UserProfile>(), false, It.IsAny<string>())).ReturnsAsync(true);
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(9, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 9, Name = "Plant 9" });
				departments.Setup(d => d.GetActiveMemberUserIdsAsync(9)).ReturnsAsync(new HashSet<string>(new[] { "active" }, StringComparer.OrdinalIgnoreCase));
				var profiles = new Mock<IUserProfileService>();
				profiles.Setup(p => p.GetProfileByUserIdAsync("active", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "active", Language = "de" });
				var notifier = new LmsProgramNotifier(new Lazy<ICommunicationService>(() => communication.Object), departments.Object, Mock.Of<IDepartmentSettingsService>(), profiles.Object);

				var sent = await notifier.SendAsync(9, new[] { "active", "ACTIVE", "disabled-member" }, "CertReq.Notify.Title", "CertReq.Notify.Renewed",
					culture => new object[] { "Gabelstapler", new DateTime(2027, 12, 31).ToString("d", culture) }, LmsCertificationIssuanceService.EventCode(301));

				sent.Should().Be(1);
				communication.Verify(c => c.SendNotificationAsync("active", 9, It.Is<string>(m => m.Contains("Gabelstapler") && m.Contains("verlängert")), It.IsAny<string>(),
					It.IsAny<Department>(), "Zertifikat aktualisiert", It.IsAny<UserProfile>(), false, "CR:301"), Times.Once);
				communication.Verify(c => c.SendNotificationAsync("disabled-member", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
					It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = bypass;
			}
		}
	}
}
