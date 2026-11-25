using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT3
{
	/// <summary>
	/// Learning paths (Training T3 item 4) on in-memory repositories: A1's ordered-list Academy assignments become system paths linked by key
	/// without losing progress (it is read from the lesson completions), prerequisites hold steps back until they unlock, and finishing a
	/// department path issues its certification through T2's issue path.
	/// </summary>
	[TestFixture]
	public class LmsPathServiceTests
	{
		private const int Dept = 7;
		private const string Learner = "00000000-0000-4000-8000-000000000042";
		private static readonly DateTime Now = DateTime.UtcNow;

		private sealed class FakePaths : MemRepo<LmsPath>, ILmsPathsRepository
		{
			public Task<List<LmsPath>> GetVisibleAsync(int departmentId) => Task.FromResult(Out(Rows.Where(p => (p.DepartmentId == departmentId || p.DepartmentId == 0) && !p.IsDeleted)));
			public Task<LmsPath> GetVisibleByIdAsync(int departmentId, int lmsPathId) => Task.FromResult(One(p => (p.DepartmentId == departmentId || p.DepartmentId == 0) && p.LmsPathId == lmsPathId));
			public Task<LmsPath> GetBySystemKeyAsync(string systemKey) => Task.FromResult(One(p => p.DepartmentId == 0 && string.Equals(p.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase)));
		}

		private sealed class FakeSteps : MemRepo<LmsPathStep>, ILmsPathStepsRepository
		{
			public Task<List<LmsPathStep>> GetForPathsAsync(IEnumerable<int> lmsPathIds) =>
				Task.FromResult(Out(Rows.Where(s => lmsPathIds.Contains(s.LmsPathId)).OrderBy(s => s.LmsPathId).ThenBy(s => s.SortOrder)));

			public async Task ReplaceAsync(int departmentId, int lmsPathId, IEnumerable<LmsPathStep> steps, CancellationToken cancellationToken = default)
			{
				Rows.RemoveAll(s => s.DepartmentId == departmentId && s.LmsPathId == lmsPathId);
				foreach (var step in steps)
				{
					step.LmsPathStepId = 0;
					step.LmsPathId = lmsPathId;
					step.DepartmentId = departmentId;
					await InsertAsync(step, cancellationToken);
				}
			}
		}

		/// <summary>Course completions read from the enrollments, like the Dapper projection.</summary>
		private sealed class FakeFacts(FakeLmsEnrollments enrollments) : ILmsRequirementFactsRepository
		{
			public Task<List<LmsRequirementFact>> GetCourseCompletionsAsync(int departmentId, IEnumerable<int> lmsCourseIds) =>
				Task.FromResult(enrollments.Rows.Where(e => e.DepartmentId == departmentId && e.Status == (int)LmsEnrollmentStatuses.Completed && e.CompletedOn.HasValue &&
					lmsCourseIds.Contains(e.LmsCourseId)).Select(e => new LmsRequirementFact { UserId = e.UserId, RefId = e.LmsCourseId, CompletedOn = e.CompletedOn.Value }).ToList());

			public Task<List<LmsRequirementFact>> GetTrainingCompletionsAsync(int departmentId, IEnumerable<int> trainingIds) => Task.FromResult(new List<LmsRequirementFact>());
			public Task<List<LmsRequirementFact>> GetCategoryHoursAsync(int departmentId, IEnumerable<int> categoryIds, DateTime fromUtc) => Task.FromResult(new List<LmsRequirementFact>());
			public Task<List<LmsRequirementFact>> GetEvaluationsAsync(int departmentId, IEnumerable<string> codes) => Task.FromResult(new List<LmsRequirementFact>());
		}

		private FakePaths _paths;
		private FakeSteps _steps;
		private FakeLmsPaths _assignments;
		private FakeLmsEnrollments _enrollments;
		private FakeLmsCourses _courses;
		private Mock<ILmsLearnerService> _learner;
		private Mock<ILmsAcademyService> _academy;
		private Mock<ILmsEntitlementService> _entitlements;
		private Mock<IContentAudienceService> _audience;
		private Mock<ITrainingService> _trainings;
		private Mock<ICertificationService> _certifications;
		private Mock<IEventAggregator> _events;
		private List<TrainingWorkflowEvent> _raised;
		private List<(int CourseId, string UserId)> _enrolled;

		private LmsPathService Service() => new LmsPathService(_paths, _steps, _assignments, _enrollments, _courses, new FakeFacts(_enrollments), _learner.Object, _academy.Object,
			_entitlements.Object, _audience.Object, _trainings.Object, _certifications.Object, _events.Object);

		[SetUp]
		public void SetUp()
		{
			_paths = new FakePaths();
			_steps = new FakeSteps();
			_assignments = new FakeLmsPaths();
			_enrollments = new FakeLmsEnrollments();
			_courses = new FakeLmsCourses();
			_learner = new Mock<ILmsLearnerService>();
			_academy = new Mock<ILmsAcademyService>();
			_entitlements = new Mock<ILmsEntitlementService>();
			_audience = new Mock<IContentAudienceService>();
			_trainings = new Mock<ITrainingService>();
			_certifications = new Mock<ICertificationService>();
			_events = new Mock<IEventAggregator>();
			_raised = new List<TrainingWorkflowEvent>();
			_enrolled = new List<(int CourseId, string UserId)>();
			LmsPathService.ResetSeedMemo();
			_academy.Setup(a => a.EnsureSeededAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
			_academy.Setup(a => a.Text(It.IsAny<string>(), It.IsAny<string>())).Returns((string key, string _) => key);
			_entitlements.Setup(e => e.IsVisibleAsync(It.IsAny<int>(), It.IsAny<LmsCourse>())).ReturnsAsync(true);
			_events.Setup(e => e.SendMessage(It.IsAny<TrainingWorkflowEvent>())).Callback((TrainingWorkflowEvent e) => _raised.Add(e));
			_learner.Setup(l => l.AssignAsync(It.IsAny<LmsActor>(), It.IsAny<int>(), It.IsAny<IEnumerable<IContentAudienceRule>>(), It.IsAny<DateTime?>(), It.IsAny<LmsEnrollmentSources>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((LmsActor a, int course, IEnumerable<IContentAudienceRule> who, DateTime? due, LmsEnrollmentSources source, string key, CancellationToken _) =>
				{
					foreach (var rule in who)
						_enrolled.Add((course, rule.RefId));
					return new LmsAssignOutcome { Created = who.Count() };
				});
			_audience.Setup(a => a.ResolveAsync(Dept, It.IsAny<IEnumerable<IContentAudienceRule>>()))
				.ReturnsAsync(new ContentAudienceResolution { MemberUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Learner } });

			// The Academy course set as A1 seeded it: one system course per lesson, keyed by SystemKey.
			var id = 100;
			foreach (var definition in AcademyCourseManifest.Courses)
				_courses.Rows.Add(new LmsCourse
				{
					LmsCourseId = ++id, DepartmentId = 0, PublisherKind = (int)LmsPublisherKinds.System, SystemKey = definition.SystemKey, Title = definition.LessonId,
					Status = (int)ProtocolStatuses.Published, CurrentVersionId = id
				});
		}

		private int CourseFor(string lessonId) => _courses.Rows.Single(c => c.SystemKey == AcademyCourseManifest.SystemKeyFor(lessonId)).LmsCourseId;

		private void Completed(int courseId, DateTime on) => _enrollments.Rows.Add(new LmsEnrollment
		{
			LmsEnrollmentId = _enrollments.Rows.Count + 1, DepartmentId = Dept, LmsCourseId = courseId, UserId = Learner, Status = (int)LmsEnrollmentStatuses.Completed, CompletedOn = on,
			AssignedOn = on.AddDays(-1)
		});

		private static LmsActor Actor(bool addon = true) => new LmsActor
		{
			DepartmentId = Dept, UserId = Learner, IsCurrentMember = true, IsDepartmentAdmin = true,
			Gate = new LmsDepartmentGate { DepartmentId = Dept, ModuleOn = true, FlagOn = true, AddonActive = addon, LastEntitledOn = Now }
		};

		[Test]
		public async Task A1_ordered_list_assignments_become_system_paths_without_losing_progress()
		{
			// Before T3: the Dispatcher role path assigned as a plain list (PathKey, no path id), three of its lessons already done.
			var dispatcher = AcademyCourseManifest.FindPath("dispatcher");
			await _assignments.InsertAsync(new LmsPathAssignment { DepartmentId = Dept, PathKey = "dispatcher", UserId = Learner, AssignedOn = Now.AddDays(-30) }, default);
			foreach (var lesson in dispatcher.LessonIds.Take(3))
				Completed(CourseFor(lesson), Now.AddDays(-20));

			var changed = await Service().EnsureSystemPathsAsync();

			changed.Should().BeGreaterThan(0);
			_paths.Rows.Where(p => p.IsSystem).Select(p => p.AssignmentKey).Should().BeEquivalentTo(AcademyCourseManifest.RolePaths.Select(p => p.Key));
			var path = _paths.Rows.Single(p => p.SystemKey == LmsPath.SystemKeyFor("dispatcher"));
			_assignments.Rows.Single().LmsPathId.Should().Be(path.LmsPathId, "the list was linked to its path, not re-assigned");
			_steps.Rows.Count(s => s.LmsPathId == path.LmsPathId).Should().Be(dispatcher.LessonIds.Count);

			var mine = await Service().GetMyPathsAsync(Actor(), Now);
			var progress = mine.Single().Progress;
			progress.Done.Should().Be(3, "the lessons finished as an ordered list count on the path");
			progress.Needed.Should().Be(dispatcher.LessonIds.Count);

			// Seeding again changes nothing and links nothing twice.
			LmsPathService.ResetSeedMemo();
			(await Service().EnsureSystemPathsAsync()).Should().Be(0);
			_paths.Rows.Count(p => p.IsSystem).Should().Be(AcademyCourseManifest.RolePaths.Count);
		}

		[Test]
		public async Task Assigning_a_path_enrolls_only_unlocked_steps_and_the_next_unlocks_once_its_prerequisite_is_done()
		{
			var a = _courses.Rows[0].LmsCourseId;
			var b = _courses.Rows[1].LmsCourseId;
			var c = _courses.Rows[2].LmsCourseId;
			var saved = await Service().SavePathAsync(Actor(), new LmsPathInput
			{
				Name = "Engineer onboarding",
				Steps = new List<LmsPathStepInput>
				{
					new LmsPathStepInput { Kind = LmsPathStepKinds.Course, TargetRefId = a },
					new LmsPathStepInput { Kind = LmsPathStepKinds.Course, TargetRefId = b, Prerequisites = "1" },
					new LmsPathStepInput { Kind = LmsPathStepKinds.Course, TargetRefId = c, Prerequisites = "2" }
				}
			});
			saved.Succeeded.Should().BeTrue();
			var pathId = ((LmsPath)saved.Value).LmsPathId;

			var outcome = await Service().AssignAsync(Actor(), pathId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, Learner) }, Now.AddDays(30));
			outcome.Created.Should().Be(1);
			_enrolled.Should().Equal(new[] { (a, Learner) }, "only the first step has no prerequisite");
			_raised.Should().ContainSingle(e => e.Change == TrainingWorkflowChanges.Assigned && e.PathId == pathId);

			Completed(a, Now.AddHours(-2));
			await Service().AdvanceDepartmentAsync(Dept, Actor().Gate, Now);
			_enrolled.Should().Equal(new[] { (a, Learner), (b, Learner) });
		}

		[Test]
		public async Task Finishing_a_department_path_stamps_it_once_and_issues_its_certification()
		{
			var a = _courses.Rows[0].LmsCourseId;
			var b = _courses.Rows[1].LmsCourseId;
			_certifications.Setup(x => x.GetCertificationTypeByIdAsync(9)).ReturnsAsync(new DepartmentCertificationType
			{
				DepartmentCertificationTypeId = 9, DepartmentId = Dept, Type = "Dispatcher", IsActive = true, AppliesTo = (int)CertificationAppliesTo.Person
			});
			_learner.Setup(l => l.IssueOrRenewCertificationAsync(Dept, Learner, 9, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(555);
			var saved = await Service().SavePathAsync(Actor(), new LmsPathInput
			{
				Name = "Dispatcher certification", IssuesCertificationTypeId = 9,
				Steps = new List<LmsPathStepInput> { new LmsPathStepInput { Kind = LmsPathStepKinds.Course, TargetRefId = a }, new LmsPathStepInput { Kind = LmsPathStepKinds.Course, TargetRefId = b, Prerequisites = "1" } }
			});
			var pathId = ((LmsPath)saved.Value).LmsPathId;
			await Service().AssignAsync(Actor(), pathId, new[] { new ContentAudienceRule(ContentAudienceKinds.User, Learner) }, null);
			Completed(a, Now.AddDays(-2));
			Completed(b, Now.AddDays(-1));

			await Service().AdvanceDepartmentAsync(Dept, Actor().Gate, Now);
			await Service().AdvanceDepartmentAsync(Dept, Actor().Gate, Now);

			var assignment = _assignments.Rows.Single();
			assignment.CompletedOn.Should().BeCloseTo(Now.AddDays(-1), TimeSpan.FromSeconds(1));
			assignment.PersonnelCertificationId.Should().Be(555);
			_learner.Verify(l => l.IssueOrRenewCertificationAsync(Dept, Learner, 9, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once());
			_raised.Count(e => e.Change == TrainingWorkflowChanges.Completed && e.PathId == pathId).Should().Be(1);
		}

		[Test]
		public async Task Department_paths_need_the_add_on_and_a_step_may_only_need_earlier_steps()
		{
			var a = _courses.Rows[0].LmsCourseId;
			var noAddon = await Service().SavePathAsync(Actor(addon: false), new LmsPathInput { Name = "x", Steps = new List<LmsPathStepInput> { new LmsPathStepInput { TargetRefId = a } } });
			noAddon.Error.Should().Be(LmsErrors.AddonRequired);

			var cycle = await Service().SavePathAsync(Actor(), new LmsPathInput
			{
				Name = "x",
				Steps = new List<LmsPathStepInput> { new LmsPathStepInput { TargetRefId = a, Prerequisites = "1" } }
			});
			cycle.Error.Should().Be(LmsErrors.InvalidInput);
			cycle.Value.Should().Be("Err.PathPrerequisiteNotEarlier");
		}
	}
}
