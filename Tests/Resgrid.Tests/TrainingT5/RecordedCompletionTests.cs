using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Tests.TrainingT1;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Round 7B (permission 135): completions an instructor records for members. The permission and its group scope, active members only,
	/// never for yourself, the attestation and the note; the baseline assignment and the transcript record carry "recorded by" (never the
	/// member's own); the first completion stands; the void goes through the T1 path for that member only; the LMS course path honours the
	/// seat (§17.4), completes every lesson, writes the LmsCourseCompletion record and issues the certification; external learners only from a
	/// class roster; workflow trigger 244 carries no contact data.
	/// </summary>
	[TestFixture]
	public class RecordedCompletionTests
	{
		private const int Dept = 4701;
		private const string Officer = "officer-1";
		private const string GroupAdmin = "group-admin-1";
		private const string Alice = "member-alice";
		private const string Bob = "member-bob";
		private const string Carol = "member-carol-hidden";

		#region Baseline harness

		private sealed class Baseline
		{
			public readonly List<TrainingCompletionRecording> Recordings = new();
			public readonly List<TrainingUser> Assignments = new();
			public readonly List<(LearningRecordSourceKinds Kind, string Ref, LearningRecordDraft Draft)> Writes = new();
			public readonly FakeLearningRecordsRepository Records = new FakeLearningRecordsRepository();
			public readonly List<object> Events = new();
			public readonly Mock<IAuthorizationService> Authorization = new();
			public readonly Training Training;
			public readonly TrainingCompletionRecordingService Service;
			public readonly Mock<ITrainingCompletionRecordingTarget> CourseTarget = new();
			/// <summary>Round 9B: the member notices of recorded completions and their voids.</summary>
			public readonly Mock<ITrainingRecordNoticeService> Notices = new();

			public Baseline(bool withQuiz = true)
			{
				Training = new Training
				{
					TrainingId = 77, DepartmentId = Dept, Name = "CPR refresher", Hours = 2m, CreatedByUserId = Officer,
					Users = Assignments, Questions = withQuiz ? new List<TrainingQuestion> { new TrainingQuestion { TrainingQuestionId = 1, Question = "Rate?" } } : new List<TrainingQuestion>()
				};
				var recordings = new Mock<ITrainingCompletionRecordingsRepository>();
				recordings.Setup(r => r.InsertAsync(It.IsAny<TrainingCompletionRecording>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((TrainingCompletionRecording r, CancellationToken _, bool __) => { r.TrainingCompletionRecordingId = 500 + Recordings.Count; Recordings.Add(r); return r; });
				recordings.Setup(r => r.SaveOrUpdateAsync(It.IsAny<TrainingCompletionRecording>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((TrainingCompletionRecording r, CancellationToken _, bool __) => r);
				recordings.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<int>()))
					.ReturnsAsync((int d, int id) => Recordings.FirstOrDefault(r => r.TrainingCompletionRecordingId == id));
				recordings.Setup(r => r.GetForTargetAsync(Dept, It.IsAny<int>(), It.IsAny<int>()))
					.ReturnsAsync((int d, int k, int id) => Recordings.Where(r => r.TargetKind == k && r.TargetId == id).ToList());

				var trainings = new Mock<ITrainingService>();
				trainings.Setup(t => t.GetTrainingByIdAsync(Training.TrainingId)).ReturnsAsync(() => Training);

				var trainingUsers = new Mock<ITrainingUserRepository>();
				trainingUsers.Setup(t => t.GetTrainingUserByTrainingIdAndUserIdAsync(Training.TrainingId, It.IsAny<string>()))
					.ReturnsAsync((int id, string user) => Assignments.FirstOrDefault(a => string.Equals(a.UserId, user, StringComparison.OrdinalIgnoreCase)));
				trainingUsers.Setup(t => t.InsertAssignmentAsync(It.IsAny<TrainingUser>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((TrainingUser a, CancellationToken _) => { a.TrainingUserId = 900 + Assignments.Count; Assignments.Add(a); return a; });
				trainingUsers.Setup(t => t.UpdateColumnsAsync(It.IsAny<TrainingUser>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

				var writer = new Mock<ILearningRecordWriter>();
				writer.Setup(w => w.WriteSourceAsync(Dept, It.IsAny<LearningRecordSourceKinds>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<LearningRecordDraft>>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, LearningRecordSourceKinds kind, string reference, IReadOnlyCollection<LearningRecordDraft> drafts, string actor, CancellationToken _) =>
					{
						var result = new LearningRecordWriteResult();
						foreach (var draft in drafts)
						{
							Writes.Add((kind, reference, draft));
							var (record, _) = Records.InsertIfAbsentAsync(new LearningRecord
							{
								DepartmentId = d, UserId = draft.UserId, SourceKind = (int)kind, SourceRefId = reference, Title = draft.Title, CompletedOn = draft.CompletedOn,
								Status = (int)LearningRecordStatuses.Verified, CompletionMethod = draft.CompletionMethod, RecordedByUserId = draft.RecordedByUserId,
								RecordedOn = draft.RecordedOn, RecordedCompletionId = draft.RecordedCompletionId
							}).Result;
							result.Written++;
							result.Records.Add(record);
						}
						return result;
					});
				writer.Setup(w => w.RemoveCreditsAsync(It.IsAny<LearningRecord>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept))
					.ReturnsAsync(() => new HashSet<string>(new[] { Officer, GroupAdmin, Alice, Bob }, StringComparer.OrdinalIgnoreCase));

				Authorization.Setup(a => a.GetTrainingManagementScopeAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string user, int d) => Scope(user));

				var events = new Mock<IEventAggregator>();
				events.Setup(e => e.SendMessage(It.IsAny<TrainingCompletionRecordedEvent>())).Callback((TrainingCompletionRecordedEvent e) => Events.Add(e));
				events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => Events.Add(e));
				CourseTarget.SetupGet(t => t.Kind).Returns(TrainingCompletionRecordingTargets.Course);

				Service = new TrainingCompletionRecordingService(recordings.Object, trainings.Object, trainingUsers.Object, Records, writer.Object, Authorization.Object,
					departments.Object, new[] { CourseTarget.Object }, events.Object, Notices.Object);
			}

			/// <summary>The officer records for everyone; the group admin only for Alice; nobody else holds the right.</summary>
			public static TrainingManagementScope Scope(string user)
			{
				var scope = TrainingManagementScope.None(user, Dept);
				if (user == Officer)
					scope.Record = new TrainingRightScope { ManagesAll = true };
				else if (user == GroupAdmin)
					scope.Record = new TrainingRightScope { AdministeredMemberUserIds = new HashSet<string>(new[] { Alice, GroupAdmin }, StringComparer.OrdinalIgnoreCase) };
				return scope;
			}
		}

		private static TrainingCompletionRecordingRequest Request(string recorder, params string[] members) => new TrainingCompletionRecordingRequest
		{
			RecorderUserId = recorder,
			MemberUserIds = members.ToList(),
			Note = "Group CPR video and practice at station 3",
			Attested = true,
			AttestationVersion = TrainingCompletionAttestation.CurrentVersion
		};

		#endregion Baseline harness

		#region Permission

		[Test]
		public async Task Without_the_permission_nothing_is_recorded()
		{
			var h = new Baseline();
			var outcome = await h.Service.RecordTrainingAsync(Dept, 77, Request("someone-else", Alice));
			outcome.Error.Should().Be(TrainingCompletionRecordingRules.NotAuthorized);
			h.Recordings.Should().BeEmpty();
			h.Writes.Should().BeEmpty();
		}

		[Test]
		public async Task A_group_admin_records_only_for_their_own_groups_members()
		{
			var h = new Baseline();
			var outcome = await h.Service.RecordTrainingAsync(Dept, 77, Request(GroupAdmin, Alice, Bob));
			outcome.Succeeded.Should().BeTrue();
			outcome.Results.Single(r => r.UserId == Alice).Recorded.Should().BeTrue();
			outcome.Results.Single(r => r.UserId == Bob).Refusal.Should().Be(TrainingCompletionRecordingRules.OutsideScope);
			h.Writes.Should().ContainSingle().Which.Draft.UserId.Should().Be(Alice);
		}

		[Test]
		public async Task Never_for_yourself_never_for_an_inactive_member_and_the_attestation_and_note_are_required()
		{
			var h = new Baseline();
			var outcome = await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Officer, Carol));
			outcome.Results.Single(r => r.UserId == Officer).Refusal.Should().Be(TrainingCompletionRecordingRules.Self);
			outcome.Results.Single(r => r.UserId == Carol).Refusal.Should().Be(TrainingCompletionRecordingRules.NotActiveMember);

			var unattested = Request(Officer, Alice);
			unattested.Attested = false;
			(await h.Service.RecordTrainingAsync(Dept, 77, unattested)).Error.Should().Be(TrainingCompletionRecordingRules.AttestationRequired);
			var noNote = Request(Officer, Alice);
			noNote.Note = " ";
			(await h.Service.RecordTrainingAsync(Dept, 77, noNote)).Error.Should().Be(TrainingCompletionRecordingRules.NoteRequired);
			var unknownStatement = Request(Officer, Alice);
			unknownStatement.AttestationVersion = "1999-01-01";
			(await h.Service.RecordTrainingAsync(Dept, 77, unknownStatement)).Error.Should().Be(TrainingCompletionRecordingRules.AttestationRequired);
			h.Writes.Should().BeEmpty();
		}

		[Test]
		public void The_claim_follows_permission_135_its_own_row_and_never_everyone()
		{
			ClaimsIdentity Claims(bool admin, Permission row, bool groupAdmin = false, params int[] roles)
			{
				var identity = new ClaimsIdentity();
				ClaimsLogic.AddTrainingClaims(identity, admin, row == null ? new List<Permission>() : new List<Permission> { row }, groupAdmin,
					roles.Select(r => new PersonnelRole { PersonnelRoleId = r }).ToList());
				return identity;
			}
			bool Holds(ClaimsIdentity identity) => identity.HasClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.RecordCompletions);
			Permission Row(PermissionActions action, string data = null) => new Permission { PermissionType = (int)PermissionTypes.RecordTrainingCompletions, Action = (int)action, Data = data };

			((int)PermissionTypes.RecordTrainingCompletions).Should().Be(135);
			Holds(Claims(true, null)).Should().BeTrue("department admins always");
			Holds(Claims(false, null)).Should().BeFalse("no row: department admins only");
			Holds(Claims(false, Row(PermissionActions.DepartmentAndGroupAdmins), groupAdmin: true)).Should().BeTrue();
			Holds(Claims(false, Row(PermissionActions.DepartmentAdminsAndSelectRoles, "7"), roles: 7)).Should().BeTrue();
			Holds(Claims(false, Row(PermissionActions.DepartmentAdminsAndSelectRoles, "7"), roles: 8)).Should().BeFalse();
			Holds(Claims(false, Row(PermissionActions.Everyone))).Should().BeFalse("a member never records for the department at large");
			Holds(Claims(false, new Permission { PermissionType = (int)PermissionTypes.CreateTraining, Action = (int)PermissionActions.Everyone })).Should().BeFalse("it never follows Create Training");
			Resgrid.Web.Areas.User.Models.Security.PermissionScreenCatalog.Get((int)PermissionTypes.RecordTrainingCompletions).Should().NotBeNull();
		}

		[Test]
		public void The_scope_records_for_a_group_admins_own_members_and_never_for_yourself()
		{
			var scope = Baseline.Scope(GroupAdmin);
			scope.CanRecordCompletions.Should().BeTrue();
			scope.CanRecordFor(Alice).Should().BeTrue();
			scope.CanRecordFor(Alice.ToUpperInvariant()).Should().BeTrue("user ids compare case-insensitively");
			scope.CanRecordFor(Bob).Should().BeFalse();
			scope.CanRecordFor(GroupAdmin).Should().BeFalse("never for yourself");
			TrainingManagementScope.None(Bob, Dept).CanRecordCompletions.Should().BeFalse();
		}

		#endregion Permission

		#region Baseline training

		[Test]
		public async Task A_recorded_completion_completes_the_assignment_and_writes_the_record_as_recorded_by_the_instructor()
		{
			var h = new Baseline();
			h.Assignments.Add(new TrainingUser { TrainingUserId = 10, TrainingId = 77, UserId = Alice, Cycle = 2 });

			var outcome = await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Alice, Bob));

			outcome.Recorded.Should().Be(2);
			var alice = h.Assignments.Single(a => a.UserId == Alice);
			alice.Complete.Should().BeTrue();
			alice.CompletionMethod.Should().Be((int)LearningCompletionMethods.RecordedByOther);
			alice.RecordedCompletionId.Should().Be(outcome.Results.Single(r => r.UserId == Alice).Recording.TrainingCompletionRecordingId);
			h.Assignments.Single(a => a.UserId == Bob).Complete.Should().BeTrue("a member not assigned is assigned and completed");

			var write = h.Writes.Single(w => w.Draft.UserId == Alice);
			write.Kind.Should().Be(LearningRecordSourceKinds.CourseCompletion);
			write.Ref.Should().Be(LearningRecordRules.CompletionRef(77, 2), "the live cycle's completion");
			write.Draft.CompletionMethod.Should().Be((int)LearningCompletionMethods.RecordedByOther);
			write.Draft.RecordedByUserId.Should().Be(Officer);
			write.Draft.RecordedOn.Should().NotBeNull();

			var record = h.Records.Live.Single(r => r.UserId == Alice);
			record.IsRecordedByOther.Should().BeTrue("never presented as the member's own completion");
			var recording = h.Recordings.Single(r => r.UserId == Alice);
			recording.LearningRecordId.Should().Be(record.LearningRecordId);
			recording.QuizWaived.Should().BeTrue("the training has a quiz the member did not take");
			recording.AttestationVersion.Should().Be(TrainingCompletionAttestation.CurrentVersion);
			recording.Note.Should().Contain("station 3");
			recording.SourceKind.Should().Be((int)TrainingCompletionRecordingSources.Manual);
			h.Events.OfType<AuditEvent>().Count(e => e.Type == AuditLogTypes.TrainingCompletionRecorded).Should().Be(2);
		}

		[Test]
		public async Task The_first_completion_stands_a_member_who_completed_keeps_their_own_and_a_recorded_one_is_never_overwritten()
		{
			var h = new Baseline();
			h.Assignments.Add(new TrainingUser { TrainingUserId = 11, TrainingId = 77, UserId = Bob, Complete = true, CompletedOn = DateTime.UtcNow.AddDays(-3) });

			var outcome = await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Bob));
			outcome.Results.Single().Refusal.Should().Be(TrainingCompletionRecordingRules.AlreadyComplete);
			h.Assignments.Single().CompletionMethod.Should().Be((int)LearningCompletionMethods.Self);

			// The member's own later write of the same source keeps the recorded method (the draft follows the assignment row), and a draft
			// without it never compares equal to the recorded record (so it can never silently become "their own").
			var recorded = new LearningRecord { Title = "CPR", CompletedOn = DateTime.UtcNow.Date, Status = (int)LearningRecordStatuses.Verified, CompletionMethod = 1 };
			LearningRecordRules.SameAs(recorded, new LearningRecordDraft { Title = "CPR", CompletedOn = recorded.CompletedOn, CompletionMethod = 1 }).Should().BeTrue();
			LearningRecordRules.SameAs(recorded, new LearningRecordDraft { Title = "CPR", CompletedOn = recorded.CompletedOn }).Should().BeFalse();
			var live = new TrainingUser { UserId = Alice, Cycle = 0, CompletionMethod = 1, RecordedCompletionId = 42 };
			var draft = LearningRecordWriter.CompletionDraft(h.Training, Alice, DateTime.UtcNow, false, live);
			draft.CompletionMethod.Should().Be(1);
			draft.RecordedCompletionId.Should().Be(42);
		}

		[Test]
		public async Task External_learners_cannot_hold_a_baseline_training_completion()
		{
			var h = new Baseline();
			var outcome = await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, LmsExternalLearnerIds.New()));
			outcome.Results.Single().Refusal.Should().Be(TrainingCompletionRecordingRules.ExternalNotAllowed);
		}

		[Test]
		public async Task A_void_voids_only_that_members_record_and_its_credits_and_reopens_the_assignment()
		{
			var h = new Baseline();
			await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Alice, Bob));
			var aliceRecording = h.Recordings.Single(r => r.UserId == Alice);

			(await h.Service.VoidAsync(Dept, aliceRecording.TrainingCompletionRecordingId, Officer, " ")).Refusal.Should().Be(TrainingCompletionRecordingRules.ReasonRequired);
			(await h.Service.VoidAsync(Dept, aliceRecording.TrainingCompletionRecordingId, Bob, "wrong class")).Refusal.Should().Be(TrainingCompletionRecordingRules.NotAuthorized);

			var voided = await h.Service.VoidAsync(Dept, aliceRecording.TrainingCompletionRecordingId, Officer, "Alice left before the practical");
			voided.Refusal.Should().BeNull();
			aliceRecording.IsVoided.Should().BeTrue();
			aliceRecording.VoidReason.Should().Contain("practical");
			h.Records.Rows.Single(r => r.UserId == Alice).IsVoided.Should().BeTrue();
			h.Records.Rows.Single(r => r.UserId == Bob).IsVoided.Should().BeFalse("the source is shared by the cycle: only this member's record is voided");
			var alice = h.Assignments.Single(a => a.UserId == Alice);
			alice.Complete.Should().BeFalse();
			alice.CompletionMethod.Should().Be((int)LearningCompletionMethods.Self);
			alice.RecordedCompletionId.Should().BeNull();
			(await h.Service.VoidAsync(Dept, aliceRecording.TrainingCompletionRecordingId, Officer, "again")).Refusal.Should().Be(TrainingCompletionRecordingRules.AlreadyVoided);
			h.Events.OfType<AuditEvent>().Should().Contain(e => e.Type == AuditLogTypes.TrainingCompletionRecordVoided);
		}

		[Test]
		public async Task Round_9B_the_member_is_told_once_when_a_completion_is_recorded_for_them_and_once_when_it_is_voided()
		{
			var h = new Baseline();
			await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Alice, Bob));
			var alice = h.Recordings.Single(r => r.UserId == Alice);

			h.Notices.Verify(n => n.NotifyCompletionAsync(It.Is<TrainingCompletionRecording>(r => r.UserId == Alice), false, It.IsAny<CancellationToken>()), Times.Once);
			h.Notices.Verify(n => n.NotifyCompletionAsync(It.Is<TrainingCompletionRecording>(r => r.UserId == Bob), false, It.IsAny<CancellationToken>()), Times.Once);

			(await h.Service.VoidAsync(Dept, alice.TrainingCompletionRecordingId, Officer, "Alice left before the practical")).Refusal.Should().BeNull();
			h.Notices.Verify(n => n.NotifyCompletionAsync(It.Is<TrainingCompletionRecording>(r => r.UserId == Alice && r.IsVoided), true, It.IsAny<CancellationToken>()), Times.Once);
			h.Notices.Verify(n => n.NotifyCompletionAsync(It.Is<TrainingCompletionRecording>(r => r.UserId == Bob), true, It.IsAny<CancellationToken>()), Times.Never);

			// A refused recording tells nobody.
			await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Carol));
			h.Notices.Verify(n => n.NotifyCompletionAsync(It.Is<TrainingCompletionRecording>(r => r.UserId == Carol), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_course_recording_is_voided_through_the_LMS_target()
		{
			var h = new Baseline();
			var recording = new TrainingCompletionRecording { TrainingCompletionRecordingId = 1, DepartmentId = Dept, UserId = Alice, TargetKind = (int)TrainingCompletionRecordingTargets.Course, TargetId = 5 };
			h.Recordings.Add(recording);
			h.CourseTarget.Setup(t => t.VoidAsync(recording, Officer, "duplicate", It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await h.Service.VoidAsync(Dept, 1, Officer, "duplicate")).Refusal.Should().BeNull();
			h.CourseTarget.Verify(t => t.VoidAsync(recording, Officer, "duplicate", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_workflow_payload_of_a_recorded_completion_carries_no_contact_data()
		{
			var h = new Baseline();
			await h.Service.RecordTrainingAsync(Dept, 77, Request(Officer, Alice));
			var e = h.Events.OfType<TrainingCompletionRecordedEvent>().Single();
			e.Trigger.Should().Be(WorkflowTriggerEventType.TrainingCompletionRecorded);
			((int)e.Trigger).Should().Be(244);
			e.UserId.Should().Be(Alice);
			e.RecordedByUserId.Should().Be(Officer);
			e.TargetKind.Should().Be("Training");
			e.QuizWaived.Should().BeTrue();
			var json = JsonConvert.SerializeObject(e);
			json.Should().NotContain("@").And.NotContain("Email").And.NotContain("Phone").And.NotContain("Name\":\"Alice");
			typeof(TrainingCompletionRecordedEvent).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "Email", "PhoneNumber", "MemberName", "Note" });
		}

		[Test]
		public async Task The_record_page_lists_assignees_first_and_hides_who_the_recorder_cannot_record()
		{
			var h = new Baseline();
			h.Assignments.Add(new TrainingUser { TrainingUserId = 12, TrainingId = 77, UserId = Bob });
			var view = await h.Service.GetTrainingRecordingViewAsync(Dept, 77, GroupAdmin);
			view.CanRecord.Should().BeTrue();
			view.HasQuiz.Should().BeTrue();
			view.Candidates.Select(c => c.UserId).Should().BeEquivalentTo(new[] { Alice }, "a group admin sees their own group's members only, never themselves");
		}

		#endregion Baseline training

		#region LMS course

		private static TrainingCompletionRecording Recording(int id, string user, int courseId) => new TrainingCompletionRecording
		{
			TrainingCompletionRecordingId = id, DepartmentId = LmsWorld.DepartmentId, UserId = user, TargetKind = (int)TrainingCompletionRecordingTargets.Course, TargetId = courseId,
			SourceKind = (int)TrainingCompletionRecordingSources.ClassSession, SourceRefId = "57", RecordedByUserId = LmsWorld.Admin, RecordedOn = DateTime.UtcNow,
			CompletedOn = DateTime.UtcNow.AddHours(-1), AttestationVersion = TrainingCompletionAttestation.CurrentVersion
		};

		[Test]
		public async Task A_recorded_course_completion_completes_every_lesson_writes_the_record_with_its_metadata_and_issues_the_certification()
		{
			var world = new LmsWorld();
			world.CertificationTypes[8] = new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = 8, DepartmentId = LmsWorld.DepartmentId, Type = "CPR", AppliesTo = (int)CertificationAppliesTo.Person, DefaultValidityMonths = 24
			};
			var bank = await world.BankAsync(LmsWorld.Single("Rate?", "b", "a", "b"));
			var (course, version, lessons) = await world.PublishedCourseAsync("CPR", LmsWorld.Reading("Video", seatSeconds: 600), LmsWorld.Quiz(bank.LmsQuestionBankId));
			world.Versions.Rows.Single(v => v.LmsCourseVersionId == version.LmsCourseVersionId).IssuesCertificationTypeId = 8;

			var result = await world.Learner.RecordCompletionForMemberAsync(LmsWorld.DepartmentId, course.LmsCourseId, LmsWorld.Member, Recording(61, LmsWorld.Member, course.LmsCourseId));

			result.Succeeded.Should().BeTrue();
			var enrollment = world.Enrollments.Rows.Single();
			enrollment.Status.Should().Be((int)LmsEnrollmentStatuses.Completed);
			enrollment.CompletionMethod.Should().Be((int)LearningCompletionMethods.RecordedByOther);
			enrollment.RecordedCompletionId.Should().Be(61);
			enrollment.Source.Should().Be((int)LmsEnrollmentSources.RecordedCompletion);
			world.Progress.Rows.Where(p => p.LmsEnrollmentId == enrollment.LmsEnrollmentId).Should().HaveCount(lessons.Count)
				.And.OnlyContain(p => p.Status == (int)LmsLessonProgressStatuses.Completed && p.RecordedCompletionId == 61, "every lesson, the quiz included, as a group");

			var record = world.Records.Live.Should().ContainSingle().Subject;
			record.SourceKind.Should().Be((int)LearningRecordSourceKinds.LmsCourseCompletion);
			record.IsRecordedByOther.Should().BeTrue();
			record.RecordedByUserId.Should().Be(LmsWorld.Admin);
			record.RecordedCompletionId.Should().Be(61);
			world.Certifications.Should().ContainSingle().Which.UserId.Should().Be(LmsWorld.Member);
			enrollment.PersonnelCertificationId.Should().Be(world.Certifications.Single().PersonnelCertificationId);
		}

		[Test]
		public async Task A_members_later_own_completion_is_a_second_record_and_the_recorded_one_stands()
		{
			var world = new LmsWorld();
			var (course, _, lessons) = await world.PublishedCourseAsync("CPR", LmsWorld.Reading("Video"));
			(await world.Learner.RecordCompletionForMemberAsync(LmsWorld.DepartmentId, course.LmsCourseId, LmsWorld.Member, Recording(62, LmsWorld.Member, course.LmsCourseId))).Succeeded.Should().BeTrue();

			(await world.Learner.RecordCompletionForMemberAsync(LmsWorld.DepartmentId, course.LmsCourseId, LmsWorld.Member, Recording(63, LmsWorld.Member, course.LmsCourseId)))
				.Error.Should().Be(LmsErrors.AlreadySubmitted, "a member who completed the current version keeps the first completion");

			var own = await world.Learner.EnrollSelfAsync(world.Actor(LmsWorld.Member), course.LmsCourseId);
			own.Enrollment.LmsEnrollmentId.Should().NotBe(world.Enrollments.Rows.First().LmsEnrollmentId);
			(await world.Learner.CompleteLessonAsync(LmsWorld.DepartmentId, LmsWorld.Member, own.Enrollment.LmsEnrollmentId, lessons[0].LmsLessonId, null)).Count.Should().Be(1);

			world.Records.Live.Should().HaveCount(2, "both are kept: the recorded one and the member's own");
			world.Records.Live.Count(r => r.IsRecordedByOther).Should().Be(1);
		}

		[Test]
		public async Task The_course_void_target_voids_the_record_reopens_the_lessons_and_the_enrolment()
		{
			var world = new LmsWorld();
			var (course, _, _) = await world.PublishedCourseAsync("CPR", LmsWorld.Reading("Video"), LmsWorld.Reading("Practice"));
			var recording = Recording(64, LmsWorld.Member, course.LmsCourseId);
			await world.Learner.RecordCompletionForMemberAsync(LmsWorld.DepartmentId, course.LmsCourseId, LmsWorld.Member, recording);
			recording.EnrollmentId = world.Enrollments.Rows.Single().LmsEnrollmentId;
			var writer = new Mock<ILearningRecordWriter>();
			var target = new LmsCourseCompletionRecordingTarget(world.Enrollments, world.Progress, world.Records, writer.Object);

			(await target.VoidAsync(recording, LmsWorld.Admin, "not this class")).Should().BeTrue();

			world.Records.Live.Should().BeEmpty();
			writer.Verify(w => w.RemoveCreditsAsync(It.IsAny<LearningRecord>(), LmsWorld.Admin, It.IsAny<CancellationToken>()), Times.Once);
			var enrollment = world.Enrollments.Rows.Single();
			enrollment.IsOpen.Should().BeTrue();
			enrollment.Status.Should().Be((int)LmsEnrollmentStatuses.InProgress);
			enrollment.CompletionMethod.Should().Be((int)LearningCompletionMethods.Self);
			world.Progress.Rows.Should().OnlyContain(p => p.Status == (int)LmsLessonProgressStatuses.InProgress && p.RecordedCompletionId == null);
		}

		private sealed class LmsHarness
		{
			public readonly LmsWorld World = new LmsWorld();
			public readonly List<TrainingCompletionRecording> Recordings = new();
			public readonly Mock<ILmsAccessService> Access = new();
			public readonly Mock<ILmsClassService> Classes = new();
			public readonly Mock<ILmsExternalLearnersRepository> Learners = new();
			public readonly Mock<ILmsLearnerClassMailer> Mailer = new();
			public readonly LmsCompletionRecordingService Service;

			public LmsHarness()
			{
				var recordings = new Mock<ITrainingCompletionRecordingsRepository>();
				recordings.Setup(r => r.InsertAsync(It.IsAny<TrainingCompletionRecording>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((TrainingCompletionRecording r, CancellationToken _, bool __) => { r.TrainingCompletionRecordingId = 700 + Recordings.Count; Recordings.Add(r); return r; });
				recordings.Setup(r => r.SaveOrUpdateAsync(It.IsAny<TrainingCompletionRecording>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((TrainingCompletionRecording r, CancellationToken _, bool __) => r);
				recordings.Setup(r => r.DeleteAsync(It.IsAny<TrainingCompletionRecording>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((TrainingCompletionRecording r, CancellationToken _) => Recordings.Remove(r));

				var authorization = new Mock<IAuthorizationService>();
				authorization.Setup(a => a.GetTrainingManagementScopeAsync(It.IsAny<string>(), LmsWorld.DepartmentId)).ReturnsAsync((string user, int d) =>
				{
					var scope = TrainingManagementScope.None(user, d);
					if (user == LmsWorld.Admin)
						scope.Record = new TrainingRightScope { ManagesAll = true };
					return scope;
				});
				var baseline = new TrainingCompletionRecordingService(recordings.Object, Mock.Of<ITrainingService>(), Mock.Of<ITrainingUserRepository>(), World.Records,
					Mock.Of<ILearningRecordWriter>(), authorization.Object, World.Departments.Object, Array.Empty<ITrainingCompletionRecordingTarget>(), Mock.Of<IEventAggregator>(),
					Mock.Of<ITrainingRecordNoticeService>());
				Access.Setup(a => a.CanBeTrainedAsync(LmsWorld.DepartmentId, It.IsAny<string>())).ReturnsAsync((int d, string user) => user != LmsWorld.Member2);
				Learners.Setup(l => l.GetByLearnerUserIdsAsync(LmsWorld.DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<LmsExternalLearner>());
				Service = new LmsCompletionRecordingService(baseline, recordings.Object, World.Learner, World.Courses, Mock.Of<ITrainingService>(), Access.Object, Classes.Object,
					Learners.Object, World.Departments.Object, authorization.Object, Mailer.Object, World.Enrollments);
			}
		}

		[Test]
		public async Task A_member_without_an_LMS_seat_cannot_get_a_course_completion_recorded()
		{
			var h = new LmsHarness();
			var (course, _, _) = await h.World.PublishedCourseAsync("CPR", LmsWorld.Reading("Video"));
			var outcome = await h.Service.RecordCourseAsync(LmsWorld.DepartmentId, course.LmsCourseId, Request(LmsWorld.Admin, LmsWorld.Member, LmsWorld.Member2));
			outcome.Results.Single(r => r.UserId == LmsWorld.Member).Recorded.Should().BeTrue();
			outcome.Results.Single(r => r.UserId == LmsWorld.Member2).Refusal.Should().Be(TrainingCompletionRecordingRules.NoSeat, "§17.4: no seat, no LMS course completion");
			h.Recordings.Should().ContainSingle(r => r.UserId == LmsWorld.Member).Which.LearningRecordId.Should().NotBeNull();
			h.World.Enrollments.Rows.Should().ContainSingle();
		}

		[Test]
		public async Task An_external_learner_is_recorded_only_from_a_class_roster_that_has_them()
		{
			var h = new LmsHarness();
			var (course, _, _) = await h.World.PublishedCourseAsync("CPR", LmsWorld.Reading("Video"));
			var learner = new LmsExternalLearner { LmsExternalLearnerId = 3, DepartmentId = LmsWorld.DepartmentId, LearnerUserId = LmsExternalLearnerIds.New(), Status = (int)LmsExternalLearnerStatuses.Active };
			h.Learners.Setup(l => l.GetByLearnerUserIdsAsync(LmsWorld.DepartmentId, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<LmsExternalLearner> { learner });

			var manual = await h.Service.RecordCourseAsync(LmsWorld.DepartmentId, course.LmsCourseId, Request(LmsWorld.Admin, learner.LearnerUserId));
			manual.Results.Single().Refusal.Should().Be(TrainingCompletionRecordingRules.ExternalNotAllowed);

			var session = new LmsClassSession
			{
				LmsClassSessionId = 57, DepartmentId = LmsWorld.DepartmentId, Title = "CPR class", CourseKind = (int)LmsClassCourseKinds.Course, LmsCourseId = course.LmsCourseId,
				StartsOn = DateTime.UtcNow.AddHours(-3), EndsOn = DateTime.UtcNow.AddHours(-1)
			};
			h.Classes.Setup(c => c.GetSessionAsync(LmsWorld.DepartmentId, 57)).ReturnsAsync(session);
			h.Classes.Setup(c => c.GetEnrollmentsAsync(LmsWorld.DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<LmsClassEnrollment>
			{
				new LmsClassEnrollment { LmsClassSessionId = 57, UserId = learner.LearnerUserId, Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Attended },
				new LmsClassEnrollment { LmsClassSessionId = 57, UserId = LmsWorld.Member, Status = (int)LmsClassEnrollmentStatuses.Enrolled, Attendance = (int)LmsClassAttendanceStates.Attended }
			});
			var classRequest = Request(LmsWorld.Admin, learner.LearnerUserId, LmsWorld.Member, LmsWorld.Member2);
			classRequest.Note = null;
			var fromClass = await h.Service.RecordFromClassAsync(LmsWorld.DepartmentId, 57, classRequest);

			fromClass.Succeeded.Should().BeTrue("a class needs no note: the session is the source");
			fromClass.Results.Single(r => r.UserId == learner.LearnerUserId).Recorded.Should().BeTrue();
			fromClass.Results.Single(r => r.UserId == LmsWorld.Member).Recorded.Should().BeTrue();
			fromClass.Results.Single(r => r.UserId == LmsWorld.Member2).Refusal.Should().Be(TrainingCompletionRecordingRules.NotOnRoster);
			h.Recordings.Should().OnlyContain(r => r.SourceKind == (int)TrainingCompletionRecordingSources.ClassSession && r.SourceRefId == "57" && r.CompletedOn == session.EndsOn);
			h.Mailer.Verify(m => m.SendAsync(LmsWorld.DepartmentId, session, It.Is<IEnumerable<string>>(ids => ids.Single() == learner.LearnerUserId),
				LmsLearnerNoticeKinds.CompletionRecorded, It.IsAny<CancellationToken>()), Times.Once, "the learner is told by e-mail");
		}

		[Test]
		public void Nothing_is_recorded_from_a_class_that_has_not_started_is_cancelled_or_links_no_training()
		{
			var now = DateTime.UtcNow;
			var linked = new LmsClassSession { CourseKind = (int)LmsClassCourseKinds.Training, TrainingId = 4, StartsOn = now.AddHours(-1), EndsOn = now };
			LmsCompletionRecordingService.ClassRefusal(linked, now).Should().BeNull();
			LmsCompletionRecordingService.ClassRefusal(new LmsClassSession { CourseKind = (int)LmsClassCourseKinds.Training, TrainingId = 4, StartsOn = now.AddHours(1) }, now)
				.Should().Be(TrainingCompletionRecordingRules.ClassNotStarted);
			LmsCompletionRecordingService.ClassRefusal(new LmsClassSession { CourseKind = (int)LmsClassCourseKinds.None, StartsOn = now.AddHours(-1) }, now)
				.Should().Be(TrainingCompletionRecordingRules.NoLinkedTraining);
			LmsCompletionRecordingService.ClassRefusal(new LmsClassSession { CourseKind = (int)LmsClassCourseKinds.Course, LmsCourseId = 2, StartsOn = now.AddHours(-1), Status = (int)LmsClassSessionStatuses.Cancelled }, now)
				.Should().Be(TrainingCompletionRecordingRules.ClassCancelled);
		}

		#endregion LMS course
	}
}
