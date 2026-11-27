using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.KnowledgeK3
{
	/// <summary>
	/// K3 path recommendations: the ranking is deterministic (any signal order, the same list) and explained by its reasons; the service
	/// reads requirements, path assignments, competency gaps and role audiences without writing anything; members see their own and officers
	/// their scope; the LMS gate and the explanation's Enhanced AI gate fail closed; the explanation is validated and never sees a name.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsRecommendationTests
	{
		private const int Dept = LmsWorld.DepartmentId;

		private string _endpoint, _apiKey, _model, _revision, _digest;
		private LmsWorld _world;
		private Mock<ILmsRequirementService> _requirements;
		private Mock<ILmsPathAssignmentsRepository> _assignments;
		private Mock<ILmsPathsRepository> _paths;
		private Mock<ILmsCompetencyService> _competencies;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IAiKnowledgeService> _knowledge;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<ILlmClient> _llm;
		private Mock<ILmsEnrollmentsRepository> _enrollmentWrites;
		private List<LlmRequest> _sent;
		private List<AiGenerationRow> _rows;
		private LmsRecommendationService _service;
		private int _ladders, _pumps, _ropes, _hazmat;

		[SetUp]
		public async Task SetUp()
		{
			(_endpoint, _apiKey, _model, _revision, _digest) = (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest);
			AiConfig.Endpoint = "https://ai.example.test/v1/chat/completions";
			AiConfig.ApiKey = "test-key";
			AiConfig.Model = "Qwen/Qwen3-8B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);

			_world = new LmsWorld();
			_ladders = (await _world.PublishedCourseAsync("Ladder operations", new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "L", ContentHtml = "<p>x</p>" })).Course.LmsCourseId;
			_pumps = (await _world.PublishedCourseAsync("Pump operations", new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "P", ContentHtml = "<p>x</p>" })).Course.LmsCourseId;
			_ropes = (await _world.PublishedCourseAsync("Rope rescue", new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "R", ContentHtml = "<p>x</p>" })).Course.LmsCourseId;
			_hazmat = (await _world.PublishedCourseAsync("Hazmat awareness", new LmsLesson { Kind = (int)LmsLessonKinds.Blocks, Title = "H", ContentHtml = "<p>x</p>" })).Course.LmsCourseId;
			await _world.Audiences.ReplaceAsync(Dept, _ropes, new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "40") });

			_requirements = new Mock<ILmsRequirementService>();
			_requirements.Setup(r => r.GetMyRequirementsAsync(Dept, LmsWorld.Member, It.IsAny<DateTime>())).ReturnsAsync(new List<LmsMyRequirement>
			{
				Requirement("Annual pump refresher", _pumps, LmsRequirementStates.Overdue, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
				Requirement("Hazmat awareness yearly", _hazmat, LmsRequirementStates.Compliant, null)
			});
			_assignments = new Mock<ILmsPathAssignmentsRepository>();
			_assignments.Setup(a => a.GetForUserAsync(Dept, It.IsAny<string>())).ReturnsAsync(new List<LmsPathAssignment>());
			_paths = new Mock<ILmsPathsRepository>();
			_paths.Setup(p => p.GetVisibleAsync(Dept)).ReturnsAsync(new List<LmsPath>());
			_competencies = new Mock<ILmsCompetencyService>();
			_competencies.Setup(c => c.GetFrameworksAsync(Dept)).ReturnsAsync(new List<LmsCompetencyFramework> { new LmsCompetencyFramework { LmsCompetencyFrameworkId = 3, DepartmentId = Dept, Name = "FF1" } });
			_competencies.Setup(c => c.GetMemberGapsAsync(Dept, 3, It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(new LmsCompetencyGapReport
			{
				Rows = new List<LmsCompetencyGapRow>
				{
					new LmsCompetencyGapRow { LmsCompetencyItemId = 30, Code = "5.3", Label = "Ladders", State = LmsCompetencyEvidenceStates.None },
					new LmsCompetencyGapRow { LmsCompetencyItemId = 31, Code = "5.4", Label = "Pumps", State = LmsCompetencyEvidenceStates.Current }
				}
			});
			_competencies.Setup(c => c.GetAlignmentsAsync(Dept, 3)).ReturnsAsync(new List<LmsCompetencyAlignment>
			{
				new LmsCompetencyAlignment { LmsCompetencyItemId = 30, SubjectKind = (int)LmsCompetencySubjectKinds.Course, SubjectId = _ladders.ToString() },
				new LmsCompetencyAlignment { LmsCompetencyItemId = 31, SubjectKind = (int)LmsCompetencySubjectKinds.Course, SubjectId = _hazmat.ToString() }
			});
			var audience = new Mock<IContentAudienceService>();
			audience.Setup(a => a.GetMemberFactsAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string u) => new ContentAudienceMemberFacts { UserId = u, IsActive = true });
			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 40, DepartmentId = Dept, Name = "Rescue" } });
			_world.Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = LmsWorld.Member, FirstName = "Mia", LastName = "Member" }, new PersonName { UserId = LmsWorld.Member2, FirstName = "Max", LastName = "Two" }
			});

			_knowledge = new Mock<IAiKnowledgeService>();
			_knowledge.Setup(k => k.IsEntitledAsync(Dept)).ReturnsAsync(true);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(p => p.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);
			var admission = new Mock<IAiBackgroundAdmission>();
			admission.Setup(x => x.ReserveBackgroundAsync(Dept, LmsRecommendationService.Feature, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>())).ReturnsAsync(new AiUsageReservation("r1", Dept, "background", LmsRecommendationService.ReservedTokens, DateTime.UtcNow.AddMinutes(2)));
			var usage = new Mock<IAiUsageMeter>();
			usage.Setup(x => x.RemainingAsync(Dept, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			_rows = new List<AiGenerationRow>();
			var generations = new Mock<IKnowledgeGapsRepository>();
			generations.Setup(g => g.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>())).Callback((AiGenerationRow r, CancellationToken _) => _rows.Add(r))
				.Returns(Task.CompletedTask);
			_sent = new List<LlmRequest>();
			_llm = new Mock<ILlmClient>();

			// Reads go to the in-memory store; any write through the enrollments store is a failure.
			_enrollmentWrites = new Mock<ILmsEnrollmentsRepository>(MockBehavior.Strict);
			_enrollmentWrites.Setup(e => e.GetForUserAsync(Dept, It.IsAny<string>())).Returns((int d, string u) => _world.Enrollments.GetForUserAsync(d, u));

			_service = new LmsRecommendationService(_requirements.Object, _assignments.Object, _paths.Object, _competencies.Object, _world.Courses, _world.Audiences,
				_enrollmentWrites.Object, audience.Object, _roles.Object, _world.Departments.Object, _knowledge.Object, _protection.Object, Mock.Of<IAdminAssistConversationProtection>(),
				admission.Object, usage.Object, new Lazy<ILlmClient>(() => _llm.Object), generations.Object, TimeProvider.System);
		}

		[TearDown]
		public void TearDown() => (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest) = (_endpoint, _apiKey, _model, _revision, _digest);

		private static LmsMyRequirement Requirement(string name, int courseId, LmsRequirementStates state, DateTime? due) => new LmsMyRequirement
		{
			Requirement = new LmsRequirement { Name = name, Kind = (int)LmsRequirementKinds.Course, TargetRefId = courseId },
			Status = new LmsRequirementStatus { State = state, Satisfied = state == LmsRequirementStates.Compliant, DueUtc = due, EffectiveDueUtc = due },
			CourseId = courseId,
			TargetName = name
		};

		private static TrainingManagementScope NoScope(string user) => TrainingManagementScope.None(user, Dept);

		#region Ranking

		[Test]
		public async Task Requirements_then_competency_gaps_then_role_courses_each_with_its_reason()
		{
			var set = await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, DateTime.UtcNow);

			set.Error.Should().Be(LmsErrors.None);
			set.Items.Select(i => i.TargetId).Should().Equal(_pumps, _ladders, _ropes);
			set.Items[0].Reasons.Single().Reason.Should().Be(LmsRecommendationReasons.RequirementOverdue);
			set.Items[0].DueUtc.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
			set.Items[1].Reasons.Single().Reason.Should().Be(LmsRecommendationReasons.CompetencyMissing);
			set.Items[1].Reasons.Single().Detail.Should().Be("5.3 Ladders");
			set.Items[2].Reasons.Single().Should().Match<LmsRecommendationSignal>(s => s.Reason == LmsRecommendationReasons.RoleAudience && s.Detail == "Rescue");
			set.Items.Should().NotContain(i => i.TargetId == _hazmat, "a met requirement and current evidence recommend nothing");
			set.CanExplain.Should().BeTrue();
		}

		[Test]
		public void The_ranking_is_the_same_for_any_order_of_the_same_signals()
		{
			var signals = new List<LmsRecommendationSignal>
			{
				new LmsRecommendationSignal { TargetId = 1, Reason = LmsRecommendationReasons.RoleAudience, Detail = "Engineer" },
				new LmsRecommendationSignal { TargetId = 2, Reason = LmsRecommendationReasons.RequirementAtRisk, DueUtc = new DateTime(2026, 12, 1), Detail = "B" },
				new LmsRecommendationSignal { TargetId = 3, Reason = LmsRecommendationReasons.RequirementAtRisk, DueUtc = new DateTime(2026, 11, 30), Detail = "A" },
				new LmsRecommendationSignal { TargetId = 4, Reason = LmsRecommendationReasons.CompetencyMissing, Detail = "x" },
				new LmsRecommendationSignal { TargetId = 4, Reason = LmsRecommendationReasons.RoleAudience, Detail = "Engineer" },
				new LmsRecommendationSignal { TargetId = 5, TargetKind = LmsRecommendationTargetKinds.Path, Reason = LmsRecommendationReasons.PathInProgress, Detail = "Driver" },
				new LmsRecommendationSignal { TargetId = 6, Reason = LmsRecommendationReasons.CompetencyMissing, Detail = "y" },
				new LmsRecommendationSignal { TargetId = 7, Reason = LmsRecommendationReasons.RoleAudience, Detail = "hidden" }
			};
			var titles = new Dictionary<(LmsRecommendationTargetKinds, int), string>
			{
				[(LmsRecommendationTargetKinds.Course, 1)] = "Alpha", [(LmsRecommendationTargetKinds.Course, 2)] = "Bravo", [(LmsRecommendationTargetKinds.Course, 3)] = "Charlie",
				[(LmsRecommendationTargetKinds.Course, 4)] = "Delta", [(LmsRecommendationTargetKinds.Path, 5)] = "Echo", [(LmsRecommendationTargetKinds.Course, 6)] = "Foxtrot"
			};

			var expected = LmsRecommendationRanker.Rank(signals, titles, new HashSet<int> { 6 });
			expected.Select(r => r.TargetId).Should().Equal(3, 2, 5, 4, 1);
			expected.Single(r => r.TargetId == 4).Score.Should().Be(LmsRecommendationRanker.Weight(LmsRecommendationReasons.CompetencyMissing) + LmsRecommendationRanker.ExtraReasonBonus);

			var random = new Random(11);
			for (var i = 0; i < 25; i++)
			{
				var shuffled = signals.OrderBy(_ => random.Next()).ToList();
				LmsRecommendationRanker.Rank(shuffled, titles, new HashSet<int> { 6 })
					.Select(r => (r.TargetKind, r.TargetId, r.Score, string.Join(",", r.Reasons.Select(x => x.Reason + x.Detail))))
					.Should().Equal(expected.Select(r => (r.TargetKind, r.TargetId, r.Score, string.Join(",", r.Reasons.Select(x => x.Reason + x.Detail)))));
			}
		}

		[Test]
		public void A_completed_course_drops_out_unless_a_requirement_still_needs_it_and_an_untitled_target_is_never_shown()
		{
			var titles = new Dictionary<(LmsRecommendationTargetKinds, int), string> { [(LmsRecommendationTargetKinds.Course, 1)] = "A", [(LmsRecommendationTargetKinds.Course, 2)] = "B" };
			var ranked = LmsRecommendationRanker.Rank(new[]
			{
				new LmsRecommendationSignal { TargetId = 1, Reason = LmsRecommendationReasons.RoleAudience },
				new LmsRecommendationSignal { TargetId = 2, Reason = LmsRecommendationReasons.RequirementDue },
				new LmsRecommendationSignal { TargetId = 9, Reason = LmsRecommendationReasons.RequirementOverdue }
			}, titles, new HashSet<int> { 1, 2 });
			ranked.Select(r => r.TargetId).Should().Equal(2);
		}

		#endregion Ranking

		#region Scope and gates

		[Test]
		public async Task Members_see_their_own_officers_see_their_scope_and_nobody_else_sees_anything()
		{
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member2), NoScope(LmsWorld.Member2), LmsWorld.Member, DateTime.UtcNow)).Error
				.Should().Be(LmsErrors.NotAuthorized, "a member is not an officer");

			var groupAdmin = NoScope(LmsWorld.Grader);
			groupAdmin.Assign = new TrainingRightScope { AdministeredMemberUserIds = new HashSet<string>(new[] { LmsWorld.Member }, StringComparer.OrdinalIgnoreCase) };
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Grader), groupAdmin, LmsWorld.Member.ToUpperInvariant(), DateTime.UtcNow)).Items.Should().NotBeEmpty("their group");
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Grader), groupAdmin, LmsWorld.Member2, DateTime.UtcNow)).Error.Should().Be(LmsErrors.NotAuthorized, "not their group");
			(await _service.GetMembersInScopeAsync(_world.Actor(LmsWorld.Grader), groupAdmin)).Select(m => m.UserId).Should().Equal(LmsWorld.Grader, LmsWorld.Member);

			var manager = _world.Actor(LmsWorld.Admin, manage: true);
			(await _service.GetForMemberAsync(manager, NoScope(LmsWorld.Admin), LmsWorld.Member, DateTime.UtcNow)).Items.Should().NotBeEmpty();
			(await _service.GetForMemberAsync(manager, NoScope(LmsWorld.Admin), "removed-member", DateTime.UtcNow)).Error.Should().Be(LmsErrors.NotFound, "only active members");
		}

		[Test]
		public async Task The_LMS_gate_closes_recommendations_and_reading_them_writes_nothing()
		{
			_world.Gate.AddonActive = false;
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, DateTime.UtcNow)).Error.Should().Be(LmsErrors.AddonRequired);
			_world.Gate.AddonActive = null;
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, DateTime.UtcNow)).Error.Should().Be(LmsErrors.AddonRequired);
			_world.Gate.AddonActive = true;
			_world.Gate.FlagOn = false;
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, DateTime.UtcNow)).Error.Should().Be(LmsErrors.AddonRequired);
			_world.Gate.FlagOn = true;

			await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, DateTime.UtcNow);
			// The strict enrollments store allowed only its read; the path service (which enrolls unlocked steps) is not a dependency.
			_enrollmentWrites.Verify(e => e.GetForUserAsync(Dept, LmsWorld.Member), Times.Once);
			typeof(LmsRecommendationService).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
				.Should().NotContain(new[] { typeof(ILmsPathService), typeof(ILmsLearnerService), typeof(ILearningRecordWriter) });
		}

		#endregion Scope and gates

		#region Explanation

		private void Explains(object arguments) =>
			_llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _sent.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", AiTutorPrompt.ExplainTool, JsonSerializer.Serialize(arguments)) }, 400, 60, "tool_calls"));

		[Test]
		public async Task The_explanation_is_written_from_the_reasons_only_and_never_sees_a_name()
		{
			Explains(new
			{
				items = new[]
				{
					new { @ref = "R1", text = "Your annual pump refresher was due on 2026-10-01." },
					new { @ref = "R2", text = "You need 7 more hours of ladder work." }
				}
			});

			var explanation = await _service.ExplainAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, "en", DateTime.UtcNow);

			explanation.Outcome.Should().Be(KnowledgeOutcomes.Answered);
			explanation.Texts.Should().ContainKey(LmsRecommendationExplanation.Key(LmsRecommendationTargetKinds.Course, _pumps));
			explanation.Texts.Should().NotContainKey(LmsRecommendationExplanation.Key(LmsRecommendationTargetKinds.Course, _ladders), "7 is not in its reasons");
			var prompt = string.Join("\n", _sent.Single().Messages.Select(m => m.Content));
			prompt.Should().NotContain(LmsWorld.Member).And.NotContain("Mia");
			_rows.Single().PromptVersion.Should().Be(AiTutorPrompt.ExplainVersion);
		}

		[Test]
		public async Task The_explanation_fails_closed_without_Enhanced_AI_or_with_Advanced_Data_Protection()
		{
			_knowledge.Setup(k => k.IsEntitledAsync(Dept)).ReturnsAsync(false);
			(await _service.ExplainAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, "en", DateTime.UtcNow)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled);
			(await _service.GetForMemberAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, DateTime.UtcNow)).CanExplain.Should().BeFalse();
			_knowledge.Setup(k => k.IsEntitledAsync(Dept)).ReturnsAsync(true);

			_protection.Setup(p => p.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Enabled);
			(await _service.ExplainAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, "en", DateTime.UtcNow)).Outcome.Should().Be(KnowledgePracticeOutcomes.KeptLocal);

			_world.Gate.AddonActive = false;
			(await _service.ExplainAsync(_world.Actor(LmsWorld.Member), NoScope(LmsWorld.Member), null, "en", DateTime.UtcNow)).Outcome.Should().Be(KnowledgeOutcomes.NotEntitled);
			_llm.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Explanation
	}
}
