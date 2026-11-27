using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Round 5F, P2 LMS parts behind IProtocolLearningExtensions: question-bank acknowledgment quizzes (keys never in the learner payload,
	/// server-side scoring, the snapshot kept, a pass completes the waiting acknowledgment) and weekly micro-learning (the active, licensed
	/// audience only; worker 86 reads due departments and asks the gate once per department).
	/// </summary>
	[TestFixture]
	public class LmsProtocolLearningServiceTests
	{
		private const int Dept = 1;
		private const int ProtocolId = 1;
		private const int VersionId = 101;
		private const int BankId = 10;
		private const string Alice = "alice";
		private const string Bob = "bob";
		private const string Carol = "carol";
		private const string Officer = "officer";

		private sealed class World
		{
			public readonly List<LmsProtocolLearning> Learnings = new List<LmsProtocolLearning>();
			public readonly List<LmsProtocolQuizAttempt> Attempts = new List<LmsProtocolQuizAttempt>();
			public readonly List<LmsMicroLearningItem> Items = new List<LmsMicroLearningItem>();
			public readonly List<LmsQuestion> Questions = new List<LmsQuestion>();
			public ProtocolAcknowledgment Acknowledgment;
			public bool LmsActive = true;
			public bool ClaimSucceeds = true;
			public List<int> DueDepartments = new List<int> { Dept };
			public HashSet<string> Licensed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Alice, Bob, Carol };
			public HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Alice, Bob, Officer };
			public HashSet<string> Audience = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Alice, Bob, Carol };

			public readonly Mock<ILmsAccessService> Access = new Mock<ILmsAccessService>();
			public readonly Mock<ILmsSeatService> Seats = new Mock<ILmsSeatService>();
			public readonly Mock<IProtocolAcknowledgmentService> Acknowledgments = new Mock<IProtocolAcknowledgmentService>();
			public readonly Mock<ILmsMicroLearningRepository> Micro = new Mock<ILmsMicroLearningRepository>();
			public readonly Mock<ICommunicationService> Communication = new Mock<ICommunicationService>();
			public readonly LmsProtocolLearningService Service;

			public World()
			{
				for (var i = 1; i <= 5; i++)
					Questions.Add(new LmsQuestion
					{
						LmsQuestionId = i, DepartmentId = Dept, LmsQuestionBankId = BankId, Kind = (int)LmsQuestionKinds.SingleChoice, Text = "Q" + i, Points = 1, SortOrder = i,
						Explanation = "SECRET-EXPLANATION",
						Options = new List<LmsQuestionOption> { new LmsQuestionOption { Key = "a", Text = "A" }, new LmsQuestionOption { Key = "b", Text = "B", IsCorrect = true } }
					});
				// A short answer needs a grader: never drawn for a protocol quiz.
				Questions.Add(new LmsQuestion { LmsQuestionId = 9, DepartmentId = Dept, LmsQuestionBankId = BankId, Kind = (int)LmsQuestionKinds.ShortAnswer, Text = "Explain", Points = 1 });
				Learnings.Add(new LmsProtocolLearning
				{
					LmsProtocolLearningId = 1, DepartmentId = Dept, DispatchProtocolId = ProtocolId, LmsQuestionBankId = BankId, QuizRequired = true, QuestionCount = 3,
					PassMark = 80, MicroLearning = true
				});
				Acknowledgment = new ProtocolAcknowledgment { DepartmentId = Dept, DispatchProtocolId = ProtocolId, ProtocolVersionId = VersionId, UserId = Alice, Status = (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz };

				var learnings = new Mock<ILmsProtocolLearningsRepository>();
				learnings.Setup(x => x.GetForProtocolAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int p) => Learnings.FirstOrDefault(l => l.DepartmentId == d && l.DispatchProtocolId == p));
				learnings.Setup(x => x.GetForDepartmentAsync(Dept)).ReturnsAsync(() => Learnings.Where(l => l.DepartmentId == Dept).ToList());
				learnings.Setup(x => x.GetMicroLearningDueDepartmentIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => DueDepartments.ToList());
				learnings.Setup(x => x.SaveOrUpdateAsync(It.IsAny<LmsProtocolLearning>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((LmsProtocolLearning l, CancellationToken _, bool __) =>
					{
						if (l.LmsProtocolLearningId == 0) { l.LmsProtocolLearningId = Learnings.Count + 1; Learnings.Add(l); }
						return l;
					});

				var attempts = new Mock<ILmsProtocolQuizAttemptsRepository>();
				attempts.Setup(x => x.InsertAsync(It.IsAny<LmsProtocolQuizAttempt>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((LmsProtocolQuizAttempt a, CancellationToken _, bool __) => { a.LmsProtocolQuizAttemptId = 500 + Attempts.Count; Attempts.Add(Copy(a)); return a; });
				attempts.Setup(x => x.GetForUserVersionAsync(Dept, It.IsAny<string>(), It.IsAny<int>()))
					.ReturnsAsync((int d, string u, int v) => Attempts.Where(a => a.UserId == u && a.ProtocolVersionId == v).OrderByDescending(a => a.LmsProtocolQuizAttemptId).Select(Copy).ToList());
				attempts.Setup(x => x.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Copy(Attempts.FirstOrDefault(a => a.LmsProtocolQuizAttemptId == id)));
				attempts.Setup(x => x.TryUpdateAsync(It.IsAny<LmsProtocolQuizAttempt>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsProtocolQuizAttempt a, long expected, CancellationToken _) =>
					{
						var stored = Attempts.Single(x => x.LmsProtocolQuizAttemptId == a.LmsProtocolQuizAttemptId);
						if (stored.RowVersion != expected) return false;
						a.RowVersion = expected + 1;
						Attempts[Attempts.IndexOf(stored)] = Copy(a);
						return true;
					});

				Micro.Setup(x => x.TryClaimRunAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => ClaimSucceeds);
				Micro.Setup(x => x.TryInsertItemAsync(It.IsAny<LmsMicroLearningItem>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsMicroLearningItem i, CancellationToken _) =>
					{
						if (Items.Any(x => x.UserId == i.UserId && x.WeekKey == i.WeekKey)) return false;
						i.LmsMicroLearningItemId = 700 + Items.Count;
						Items.Add(i);
						return true;
					});
				Micro.Setup(x => x.GetItemAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Copy(Items.FirstOrDefault(i => i.LmsMicroLearningItemId == id)));
				Micro.Setup(x => x.GetForUserAsync(Dept, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((int d, string u, int take) => Items.Where(i => i.UserId == u).Select(Copy).ToList());
				Micro.Setup(x => x.GetRecentForDepartmentAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync(() => Items.ToList());
				Micro.Setup(x => x.TryAnswerAsync(It.IsAny<LmsMicroLearningItem>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LmsMicroLearningItem i, CancellationToken _) =>
					{
						var stored = Items.Single(x => x.LmsMicroLearningItemId == i.LmsMicroLearningItemId);
						if (stored.AnsweredOn.HasValue) return false;
						stored.AnsweredOn = i.AnsweredOn; stored.AnswersJson = i.AnswersJson; stored.Correct = i.Correct;
						return true;
					});

				var banks = new Mock<ILmsQuestionBanksRepository>();
				banks.Setup(x => x.GetForDepartmentAsync(Dept)).ReturnsAsync(() => new List<LmsQuestionBank>
				{
					new LmsQuestionBank { LmsQuestionBankId = BankId, DepartmentId = Dept, Name = "SOG checks", IsShared = true },
					new LmsQuestionBank { LmsQuestionBankId = 11, DepartmentId = Dept, Name = "Private", IsShared = false, OwnerUserId = Officer }
				});
				var questions = new Mock<ILmsQuestionsRepository>();
				questions.Setup(x => x.GetForBankAsync(Dept, BankId)).ReturnsAsync(() => Questions.ToList());

				var protocols = new Mock<IProtocolsService>();
				protocols.Setup(x => x.GetProtocolHeaderByIdAsync(ProtocolId)).ReturnsAsync(new DispatchProtocol
				{
					DispatchProtocolId = ProtocolId, DepartmentId = Dept, Code = "P1", Name = "Mayday", Status = (int)ProtocolStatuses.Published, CurrentVersionId = VersionId,
					Kind = (int)ProtocolKinds.Sog
				});
				var versions = new Mock<IProtocolVersionsRepository>();
				versions.Setup(x => x.GetByIdForDepartmentAsync(Dept, VersionId)).ReturnsAsync(new ProtocolVersion
				{
					ProtocolVersionId = VersionId, DepartmentId = Dept, DispatchProtocolId = ProtocolId, Major = 1, Status = (int)ProtocolVersionStatuses.Published
				});
				var audiences = new Mock<IProtocolAudiencesRepository>();
				audiences.Setup(x => x.GetForProtocolAsync(Dept, ProtocolId)).ReturnsAsync(new List<ProtocolAudience>());
				var audienceService = new Mock<IContentAudienceService>();
				audienceService.Setup(x => x.ResolveAsync(Dept, It.IsAny<IEnumerable<IContentAudienceRule>>()))
					.ReturnsAsync(() => new ContentAudienceResolution { MemberUserIds = new HashSet<string>(Audience, StringComparer.OrdinalIgnoreCase) });
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Dept", TimeZone = "Eastern Standard Time" });
				departments.Setup(x => x.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(() => new HashSet<string>(Active, StringComparer.OrdinalIgnoreCase));

				Access.Setup(x => x.CanAuthorAndManageAsync(It.IsAny<int>())).ReturnsAsync(() => LmsActive);
				Access.Setup(x => x.IsFlagEnabledAsync(It.IsAny<int>())).ReturnsAsync(() => LmsActive);
				Seats.Setup(x => x.GetLicensedUserIdsAsync(It.IsAny<int>())).ReturnsAsync(() => LmsActive ? new HashSet<string>(Licensed) : new HashSet<string>());
				Acknowledgments.Setup(x => x.GetForVersionAndUserAsync(Dept, VersionId, It.IsAny<string>())).ReturnsAsync(() => Acknowledgment);
				Acknowledgments.Setup(x => x.CompleteIfQuizPassedAsync(Dept, VersionId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

				Service = new LmsProtocolLearningService(learnings.Object, attempts.Object, Micro.Object, banks.Object, questions.Object, Access.Object, Seats.Object,
					protocols.Object, versions.Object, audiences.Object, audienceService.Object, departments.Object, Mock.Of<IDepartmentSettingsService>(),
					Mock.Of<IUserProfileService>(), new Lazy<ICommunicationService>(() => Communication.Object),
					new Lazy<IProtocolAcknowledgmentService>(() => Acknowledgments.Object), Mock.Of<IEventAggregator>())
				{
					RandomFactory = () => new Random(7)
				};
			}

			public static ProtocolReader Reader(string userId, bool officer = false) => new ProtocolReader
			{
				DepartmentId = Dept, UserId = userId, IsCurrentMember = true, IsDepartmentAdmin = officer
			};

			public static T Copy<T>(T value) where T : class =>
				value == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
		}

		private static List<ProtocolQuizAnswer> Answers(ProtocolQuizView view, Func<ProtocolQuizQuestion, string> key) =>
			view.Questions.Select(q => new ProtocolQuizAnswer { QuestionId = q.QuestionId, Keys = new List<string> { key(q) } }).ToList();

		#region Bank quizzes

		[Test]
		public async Task The_quiz_payload_never_carries_a_key_and_a_pass_completes_the_waiting_acknowledgment()
		{
			var world = new World();
			var view = await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en");

			view.Error.Should().Be(ProtocolLearningErrors.None);
			view.Questions.Should().HaveCount(3, "the protocol asks for three questions").And.OnlyContain(q => q.QuestionId != 9, "a short answer is never drawn");
			var json = JsonConvert.SerializeObject(view);
			json.Should().NotContain("IsCorrect").And.NotContain("CorrectKeys").And.NotContain("SECRET-EXPLANATION");
			typeof(ProtocolQuizQuestion).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "CorrectKeys", "CorrectOrder", "Explanation" });
			typeof(ProtocolQuizOption).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(new[] { "Key", "Text" });

			// The snapshot with the key stays on the server, with the revision it was taken against.
			var stored = world.Attempts.Single();
			LmsAttemptSnapshot.Parse(stored.SnapshotJson).Questions.Should().OnlyContain(q => q.CorrectKeys.SequenceEqual(new[] { "b" }));
			stored.RevisionHash.Should().NotBeNullOrWhiteSpace();
			(await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en")).AttemptId.Should().Be(view.AttemptId, "an open attempt resumes");

			var result = await world.Service.SubmitBankQuizAsync(World.Reader(Alice), view.AttemptId, Answers(view, _ => "b"));
			result.Passed.Should().BeTrue();
			result.Percent.Should().Be(100);
			result.Correct.Should().Be(3);
			result.AcknowledgmentCompleted.Should().BeTrue();
			world.Acknowledgments.Verify(x => x.CompleteIfQuizPassedAsync(Dept, VersionId, Alice, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

			(await world.Service.SubmitBankQuizAsync(World.Reader(Alice), view.AttemptId, Answers(view, _ => "a"))).Error.Should().Be(ProtocolLearningErrors.AlreadySubmitted);
			(await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en")).Error.Should().Be(ProtocolLearningErrors.AlreadySubmitted, "the quiz is already passed");
			var state = await world.Service.GetBankQuizStateAsync(Dept, ProtocolId, VersionId, Alice);
			state.Should().Match<ProtocolBankQuizState>(s => s.Configured && s.Passed && s.Attempts == 1 && s.BestPercent == 100 && s.OpenAttemptId == null);
		}

		[Test]
		public async Task A_failed_attempt_does_not_complete_anything_and_the_next_one_is_a_new_draw()
		{
			var world = new World();
			var view = await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en");
			var result = await world.Service.SubmitBankQuizAsync(World.Reader(Alice), view.AttemptId, Answers(view, _ => "a"));

			result.Passed.Should().BeFalse();
			result.AcknowledgmentCompleted.Should().BeFalse();
			world.Acknowledgments.Verify(x => x.CompleteIfQuizPassedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

			var again = await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en");
			again.AttemptId.Should().NotBe(view.AttemptId);
			world.Attempts.Single(a => a.LmsProtocolQuizAttemptId == again.AttemptId).AttemptNumber.Should().Be(2);
			(await world.Service.SubmitBankQuizAsync(World.Reader(Bob), again.AttemptId, Answers(again, _ => "b"))).Error.Should().Be(ProtocolLearningErrors.NotFound, "another member's attempt");
		}

		[Test]
		public async Task Only_a_member_who_owes_the_acknowledgment_of_a_bank_quiz_version_takes_the_quiz()
		{
			var world = new World { Acknowledgment = null };
			(await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en")).Error.Should().Be(ProtocolLearningErrors.NotAssigned);

			world.Acknowledgment = new ProtocolAcknowledgment { DepartmentId = Dept, ProtocolVersionId = VersionId, UserId = Alice, Status = (int)ProtocolAcknowledgmentStatuses.Acknowledged };
			(await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en")).Error.Should().Be(ProtocolLearningErrors.NotAssigned, "nothing is owed any more");

			world.Acknowledgment = new ProtocolAcknowledgment { DepartmentId = Dept, ProtocolVersionId = VersionId, UserId = Alice, Status = (int)ProtocolAcknowledgmentStatuses.Open };
			world.Learnings.Single().QuizRequired = false;
			(await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en")).Error.Should().Be(ProtocolLearningErrors.NotAssigned, "the protocol does not ask for a quiz");

			// A lapsed add-on does not strand a member who owes the quiz (rule 4): no gate is asked to take it.
			world.Learnings.Single().QuizRequired = true;
			world.LmsActive = false;
			(await world.Service.StartBankQuizAsync(World.Reader(Alice), VersionId, "en")).Error.Should().Be(ProtocolLearningErrors.None);
		}

		[Test]
		public async Task Settings_need_the_lms_an_officer_and_a_shared_bank_with_questions_the_server_can_score()
		{
			var world = new World();
			var settings = new ProtocolLearningSettings { ProtocolId = ProtocolId, QuestionBankId = BankId, QuizRequired = true, QuestionCount = 200, PassMark = 0, MicroLearning = true };

			(await world.Service.SaveSettingsAsync(World.Reader(Alice), settings)).Should().Be(ProtocolLearningErrors.NotAuthorized);
			world.LmsActive = false;
			(await world.Service.SaveSettingsAsync(World.Reader(Officer, true), settings)).Should().Be(ProtocolLearningErrors.AddonRequired);
			world.LmsActive = true;
			(await world.Service.SaveSettingsAsync(World.Reader(Officer, true), new ProtocolLearningSettings { ProtocolId = ProtocolId, QuestionBankId = 11, QuizRequired = true }))
				.Should().Be(ProtocolLearningErrors.InvalidInput, "a private bank is its owner's, not the department's");

			(await world.Service.SaveSettingsAsync(World.Reader(Officer, true), settings)).Should().Be(ProtocolLearningErrors.None);
			var row = world.Learnings.Single();
			row.QuestionCount.Should().Be(LmsProtocolLearning.MaxQuestionCount);
			row.PassMark.Should().Be(LmsProtocolLearning.DefaultPassMark);
			(await world.Service.GetBankChoicesAsync(Dept)).Should().ContainSingle().Which.DrawableQuestions.Should().Be(5);

			world.Questions.RemoveAll(q => q.Kind != (int)LmsQuestionKinds.ShortAnswer);
			(await world.Service.SaveSettingsAsync(World.Reader(Officer, true), settings)).Should().Be(ProtocolLearningErrors.NoQuestions);
		}

		#endregion Bank quizzes

		#region Micro-learning

		[Test]
		public async Task The_weekly_question_goes_to_the_active_licensed_audience_only_and_keeps_its_key_until_answered()
		{
			var world = new World();
			// Alice: active, licensed, in the audience. Bob: active, in the audience, no seat. Carol: licensed and in the audience, but not
			// an active member (inactive-member policy). Officer: active, not in the audience.
			world.Licensed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Alice, Carol, Officer };

			(await world.Service.RunMicroLearningAsync(DateTime.UtcNow)).Should().Be(1);

			var item = world.Items.Single();
			item.UserId.Should().Be(Alice);
			item.DispatchProtocolId.Should().Be(ProtocolId);
			item.WeekKey.Should().MatchRegex(@"^\d{4}-W\d{2}$");
			LmsAttemptSnapshot.Parse(item.SnapshotJson).Questions.Single().CorrectKeys.Should().Equal("b");

			var before = (await world.Service.GetMyMicroLearningAsync(Dept, Alice, 10)).Single();
			before.CorrectKeys.Should().BeNull();
			before.Explanation.Should().BeNull();
			JsonConvert.SerializeObject(before).Should().NotContain("SECRET-EXPLANATION");

			var answered = await world.Service.AnswerMicroLearningAsync(Dept, Alice, item.LmsMicroLearningItemId, new[] { "b" });
			answered.Correct.Should().BeTrue();
			answered.CorrectKeys.Should().Equal("b");
			answered.Explanation.Should().Be("SECRET-EXPLANATION", "after the one locked answer the member sees why");
			(await world.Service.AnswerMicroLearningAsync(Dept, Alice, item.LmsMicroLearningItemId, new[] { "a" })).Error.Should().Be(ProtocolLearningErrors.AlreadyAnswered);
			(await world.Service.AnswerMicroLearningAsync(Dept, Bob, item.LmsMicroLearningItemId, new[] { "b" })).Error.Should().Be(ProtocolLearningErrors.NotFound);

			(await world.Service.RunMicroLearningAsync(DateTime.UtcNow)).Should().Be(0, "one question per member per week");
		}

		[Test]
		public async Task The_worker_reads_only_due_departments_claims_first_and_asks_the_gate_once_per_department()
		{
			var world = new World { DueDepartments = new List<int>() };
			(await world.Service.RunMicroLearningAsync(DateTime.UtcNow)).Should().Be(0);
			world.Access.Invocations.Should().BeEmpty("no due department, no gate (R-L9)");
			world.Seats.Invocations.Should().BeEmpty();

			// Claimed by another worker: no gate either.
			world.DueDepartments = new List<int> { Dept };
			world.ClaimSucceeds = false;
			(await world.Service.RunMicroLearningAsync(DateTime.UtcNow)).Should().Be(0);
			world.Access.Invocations.Should().BeEmpty();

			world.ClaimSucceeds = true;
			await world.Service.RunMicroLearningAsync(DateTime.UtcNow);
			world.Access.Verify(x => x.IsFlagEnabledAsync(Dept), Times.Once);
			world.Seats.Verify(x => x.GetLicensedUserIdsAsync(Dept), Times.Once);
			world.Access.Verify(x => x.CanAuthorAndManageAsync(It.IsAny<int>()), Times.Never);
			world.Micro.Verify(x => x.TryClaimRunAsync(Dept, It.IsAny<DateTime>(), It.Is<DateTime>(d => d > DateTime.UtcNow), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Exactly(2), "the clock moves to next week before anything is sent");
		}

		[Test]
		public async Task Selecting_micro_learning_protocols_needs_an_officer_and_the_add_on()
		{
			var world = new World();
			world.Learnings.Single().MicroLearning = false;
			(await world.Service.ScheduleMicroLearningAsync(World.Reader(Alice), new[] { ProtocolId }, DateTime.UtcNow)).Should().Be(-1);
			world.LmsActive = false;
			(await world.Service.ScheduleMicroLearningAsync(World.Reader(Officer, true), new[] { ProtocolId }, DateTime.UtcNow)).Should().Be(-1);
			world.LmsActive = true;
			(await world.Service.ScheduleMicroLearningAsync(World.Reader(Officer, true), new[] { ProtocolId, 999 }, DateTime.UtcNow)).Should().Be(1, "999 is not a protocol of the department");
			(await world.Service.GetMicroLearningProtocolIdsAsync(Dept)).Should().Equal(ProtocolId);
			(await world.Service.ScheduleMicroLearningAsync(World.Reader(Officer, true), Array.Empty<int>(), DateTime.UtcNow)).Should().Be(0);
			(await world.Service.GetMicroLearningProtocolIdsAsync(Dept)).Should().BeEmpty();
		}

		[Test]
		public void The_week_the_clock_and_the_tagged_banks_follow_the_rules()
		{
			LmsProtocolLearningRules.WeekKey(new DateTime(2026, 11, 25)).Should().Be("2026-W48");
			LmsProtocolLearningRules.WeekKey(new DateTime(2027, 1, 1)).Should().Be("2026-W53", "ISO weeks: 1 January 2027 is in 2026's last week");
			LmsProtocolLearningRules.NextRunLocal(new DateTime(2026, 11, 25, 15, 0, 0)).Should().Be(new DateTime(2026, 11, 30, 9, 0, 0));
			LmsProtocolLearningRules.NextRunLocal(new DateTime(2026, 11, 30, 8, 0, 0)).Should().Be(new DateTime(2026, 11, 30, 9, 0, 0), "Monday before nine is today");
			LmsProtocolLearningRules.NextRunLocal(new DateTime(2026, 11, 30, 9, 0, 0)).Should().Be(new DateTime(2026, 12, 7, 9, 0, 0));

			var protocol = new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = Dept, Code = "SOG-12", Number = "4.2" };
			LmsProtocolLearningRules.IsBankTaggedTo(new LmsQuestionBank { LmsQuestionBankId = 3, DepartmentId = Dept, Tags = "hazmat, sog-12" }, protocol, null).Should().BeTrue();
			LmsProtocolLearningRules.IsBankTaggedTo(new LmsQuestionBank { LmsQuestionBankId = 3, DepartmentId = Dept, Tags = "SOG-123" }, protocol, null).Should().BeFalse("whole tags only");
			LmsProtocolLearningRules.IsBankTaggedTo(new LmsQuestionBank { LmsQuestionBankId = 3, DepartmentId = Dept }, protocol, 3).Should().BeTrue("the linked bank");
			LmsProtocolLearningRules.IsBankTaggedTo(new LmsQuestionBank { LmsQuestionBankId = 3, DepartmentId = 2, Tags = "SOG-12" }, protocol, null).Should().BeFalse("another department's bank");

			var ids = new[] { 4, 2, 9 };
			LmsProtocolLearningRules.ChooseProtocol("alice", "2026-W48", ids).Should().Be(LmsProtocolLearningRules.ChooseProtocol("ALICE", "2026-W48", ids.Reverse()), "stable for a member and a week");
			Enumerable.Range(1, 30).Select(w => LmsProtocolLearningRules.ChooseProtocol("alice", "2026-W" + w.ToString("00"), ids)).Distinct().Count().Should().BeGreaterThan(1, "protocols take turns");
		}

		#endregion Micro-learning
	}
}
