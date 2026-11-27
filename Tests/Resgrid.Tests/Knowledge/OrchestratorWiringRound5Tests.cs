using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// The seams round 5 left between streams: call-context answers (K3) read the call's determinant from the P4 case flow, and a class
	/// session whose post-class evaluation is a skill sheet (T4 part 1) opens a crew evaluation through the skills hook (T4 part 2).
	/// </summary>
	[TestFixture]
	public class OrchestratorWiringRound5Tests
	{
		[Test]
		public async Task Call_determinant_is_the_latest_case_that_is_not_removed()
		{
			var repo = new Mock<ICallProtocolsRepository>();
			repo.Setup(r => r.GetCallProtocolsByCallIdAsync(7)).ReturnsAsync(new List<CallProtocol>
			{
				new CallProtocol { CallProtocolId = 1, CallId = 7, Determinant = "10-D-1" },
				new CallProtocol { CallProtocolId = 3, CallId = 7, Determinant = "26-A-1", RemovedOn = System.DateTime.UtcNow },
				new CallProtocol { CallProtocolId = 2, CallId = 7, Determinant = " 9-E-1 " },
				new CallProtocol { CallProtocolId = 4, CallId = 7, Determinant = null }
			});
			var source = new CallProtocolDeterminantSource(repo.Object);

			(await source.GetDeterminantAsync(5, 7)).Should().Be("9-E-1");
			(await source.GetDeterminantAsync(5, 0)).Should().BeNull();
			(await source.GetDeterminantAsync(0, 7)).Should().BeNull();
		}

		[Test]
		public async Task A_skill_sheet_post_class_evaluation_opens_one_crew_evaluation_with_the_class_session_key()
		{
			var hook = new Mock<ILmsPostClassEvaluationHook>();
			LmsPostClassEvaluationRequest seen = null;
			hook.Setup(h => h.StartPostClassEvaluationAsync(It.IsAny<LmsPostClassEvaluationRequest>(), It.IsAny<CancellationToken>()))
				.Callback<LmsPostClassEvaluationRequest, CancellationToken>((r, _) => seen = r)
				.ReturnsAsync(new LmsSkillEvaluation());
			var evaluation = new LmsSkillSheetPostClassEvaluation(hook.Object);
			var session = new LmsClassSession { LmsClassSessionId = 42, DepartmentId = 5, CalendarItemId = 900, PostClassEvaluationRef = "17", Location = "Station 1" };

			var started = await evaluation.StartAsync(new LmsActor { DepartmentId = 5, UserId = "instructor" }, session, new[] { "a", "b", "A", " " });

			evaluation.Kind.Should().Be(LmsClassPostEvaluationKinds.SkillSheet);
			started.Should().Be(2);
			seen.ClassSessionRef.Should().Be(LmsClassRules.SessionKey(session));
			seen.LmsSkillSheetId.Should().Be(17);
			seen.EvaluatorUserId.Should().Be("instructor");
			seen.MemberUserIds.Should().BeEquivalentTo(new[] { "a", "b" });

			session.PostClassEvaluationRef = "not-a-sheet";
			(await evaluation.StartAsync(new LmsActor { DepartmentId = 5, UserId = "instructor" }, session, new[] { "a" })).Should().Be(0);
			hook.Verify(h => h.StartPostClassEvaluationAsync(It.IsAny<LmsPostClassEvaluationRequest>(), It.IsAny<CancellationToken>()), Times.Once());
		}
	}
}
