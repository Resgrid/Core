using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.KnowledgeK3
{
	/// <summary>
	/// Owner decision 2026-11-26: question explanations join the knowledge corpus without leaking answers. A bank's live, explained questions
	/// are chunked (question + explanation, never options or keys); retired questions, deleted banks, the system publisher's banks and a
	/// closed LMS gate index nothing; and only the people the bank editor already shows the answer keys to retrieve them (managers, and
	/// course editors for shared banks and their own) - never a learner, even one who can still take the assessment.
	/// </summary>
	[TestFixture]
	public class LmsQuestionExplanationKnowledgeSourceTests
	{
		private const int Dept = LmsWorld.DepartmentId;
		private const string Key = "KEY-OPTION-TEXT";
		private const string Editor = "lms-course-editor";

		private LmsWorld _world;
		private Mock<ILmsAccessService> _access;
		private Mock<ILmsGateService> _gate;
		private LmsQuestionExplanationKnowledgeSource _source;
		private readonly Dictionary<string, LmsActor> _actors = new Dictionary<string, LmsActor>(StringComparer.OrdinalIgnoreCase);

		[SetUp]
		public void SetUp()
		{
			_world = new LmsWorld();
			_access = new Mock<ILmsAccessService>();
			_access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(true);
			_gate = new Mock<ILmsGateService>();
			_actors.Clear();
			_gate.Setup(g => g.GetActorAsync(Dept, It.IsAny<string>()))
				.ReturnsAsync((int d, string user) => _actors.TryGetValue(user, out var actor) ? actor : _world.Actor(user));
			_source = new LmsQuestionExplanationKnowledgeSource(_access.Object, _gate.Object, _world.Banks, _world.Questions, _world.BankService);
		}

		private static LmsQuestion Explained(string text, string explanation, bool retired = false)
		{
			var question = LmsWorld.Single(text, "a", "a", "b");
			question.Options[0].Text = Key;
			question.Explanation = explanation;
			question.IsRetired = retired;
			return question;
		}

		private async Task<LmsQuestionBank> BankAsync(bool shared, string owner, params LmsQuestion[] questions)
		{
			var bank = await _world.BankAsync(questions);
			var stored = _world.Banks.Rows.Single(b => b.LmsQuestionBankId == bank.LmsQuestionBankId);
			stored.IsShared = shared;
			stored.OwnerUserId = owner;
			stored.Name = "Bank " + bank.LmsQuestionBankId;
			return stored;
		}

		private static KnowledgeReader Reader(string userId) => new KnowledgeReader { DepartmentId = Dept, UserId = userId, CanViewTrainings = true, Locale = "en" };

		private async Task<KnowledgeReaderScope> ScopeAsync(string userId)
		{
			var scope = new KnowledgeReaderScope { DepartmentId = Dept, UserId = userId, IsCurrentMember = true };
			await _source.AddReadableAsync(Reader(userId), scope);
			return scope;
		}

		[Test]
		public async Task A_banks_live_explained_questions_are_chunked_without_their_options_or_key()
		{
			var bank = await BankAsync(true, LmsWorld.Admin,
				Explained("Which valve opens first?", "The intake valve opens first so the pump is never starved."),
				Explained("Retired question", "RETIRED-EXPLANATION", retired: true),
				Explained("Unexplained question", null));
			var explained = _world.Questions.Rows.Single(q => q.Text == "Which valve opens first?");

			(await _source.ListItemIdsAsync(Dept, KnowledgeSourceKinds.QuestionExplanation)).Should().BeEquivalentTo(bank.LmsQuestionBankId.ToString());
			var content = await _source.BuildAsync(Dept, KnowledgeSourceKinds.QuestionExplanation, bank.LmsQuestionBankId.ToString());

			var chunk = content.Chunks.Should().ContainSingle().Subject;
			chunk.SourceKind.Should().Be((int)KnowledgeSourceKinds.QuestionExplanation);
			chunk.ItemId.Should().Be(bank.LmsQuestionBankId.ToString());
			chunk.Anchor.Should().Be("q" + explained.LmsQuestionId);
			chunk.Title.Should().Be(bank.Name);
			chunk.Text.Should().Contain("Which valve opens first?").And.Contain("The intake valve opens first");
			chunk.AccessFlags.Should().Be((int)KnowledgeAccessFlags.AssigneesRead);
			chunk.AudienceJson.Should().BeNull();
			content.Chunks.Should().NotContain(c => c.Text.Contains(Key) || (c.HeadingPath ?? string.Empty).Contains(Key), "the options and the answer key are never read");
			content.Chunks.Should().NotContain(c => c.Text.Contains("RETIRED-EXPLANATION"));
		}

		[Test]
		public async Task Deleted_banks_system_banks_another_departments_and_a_closed_flag_index_nothing()
		{
			var deleted = await BankAsync(true, LmsWorld.Admin, Explained("Q", "E"));
			_world.Banks.Rows.Single(b => b.LmsQuestionBankId == deleted.LmsQuestionBankId).IsDeleted = true;
			var system = await BankAsync(true, null, Explained("Academy Q", "Academy E"));
			_world.Banks.Rows.Single(b => b.LmsQuestionBankId == system.LmsQuestionBankId).DepartmentId = 0;
			foreach (var q in _world.Questions.Rows.Where(q => q.LmsQuestionBankId == system.LmsQuestionBankId))
				q.DepartmentId = 0;

			(await _source.ListItemIdsAsync(Dept, KnowledgeSourceKinds.QuestionExplanation)).Should().BeEmpty();
			(await _source.BuildAsync(Dept, KnowledgeSourceKinds.QuestionExplanation, deleted.LmsQuestionBankId.ToString())).Should().BeNull();
			(await _source.BuildAsync(Dept, KnowledgeSourceKinds.QuestionExplanation, system.LmsQuestionBankId.ToString())).Should().BeNull();
			(await _source.BuildAsync(Dept + 1, KnowledgeSourceKinds.QuestionExplanation, deleted.LmsQuestionBankId.ToString())).Should().BeNull();
			(await _source.BuildAsync(Dept, KnowledgeSourceKinds.Course, deleted.LmsQuestionBankId.ToString())).Should().BeNull();

			var live = await BankAsync(true, LmsWorld.Admin, Explained("Q2", "E2"));
			_access.Setup(a => a.IsFlagEnabledAsync(Dept)).ReturnsAsync(false);
			(await _source.ListItemIdsAsync(Dept, KnowledgeSourceKinds.QuestionExplanation)).Should().BeEmpty();
			(await _source.BuildAsync(Dept, KnowledgeSourceKinds.QuestionExplanation, live.LmsQuestionBankId.ToString())).Should().BeNull();
		}

		[Test]
		public async Task Only_those_shown_the_answer_keys_retrieve_explanations_never_a_learner()
		{
			var shared = await BankAsync(true, LmsWorld.Admin, Explained("Shared Q", "Shared E"));
			var editorsOwn = await BankAsync(false, Editor, Explained("Own Q", "Own E"));
			var private1 = await BankAsync(false, LmsWorld.Admin, Explained("Private Q", "Private E"));
			_actors[LmsWorld.Admin] = _world.AdminActor;
			_actors["manager"] = _world.Actor("manager", manage: true);
			// A course editor (an owner with the edit right) authors banks: shared ones and their own private ones.
			await _world.Owners.InsertAsync(new LmsCourseOwner { DepartmentId = Dept, LmsCourseId = 1, UserId = Editor, CanEdit = true }, CancellationToken.None);
			// A grader and an assigner are not shown answer keys in the bank editor.
			_actors[LmsWorld.Grader] = _world.Actor(LmsWorld.Grader, grade: true, assign: true);

			string Id(LmsQuestionBank b) => b.LmsQuestionBankId.ToString();
			HashSet<string> Readable(KnowledgeReaderScope s) =>
				s.ReadableItems.TryGetValue(KnowledgeSourceKinds.QuestionExplanation, out var set) ? set : new HashSet<string>();

			Readable(await ScopeAsync(LmsWorld.Admin)).Should().BeEquivalentTo(Id(shared), Id(editorsOwn), Id(private1));
			Readable(await ScopeAsync("manager")).Should().BeEquivalentTo(Id(shared), Id(editorsOwn), Id(private1));
			Readable(await ScopeAsync(Editor)).Should().BeEquivalentTo(Id(shared), Id(editorsOwn));
			Readable(await ScopeAsync(LmsWorld.Member)).Should().BeEmpty("a learner never retrieves an explanation, whatever the assessment's state");
			Readable(await ScopeAsync(LmsWorld.Grader)).Should().BeEmpty();

			// End to end through the access filter, on the stored chunk.
			var chunk = (await _source.BuildAsync(Dept, KnowledgeSourceKinds.QuestionExplanation, Id(shared))).Chunks.Single();
			KnowledgeAccessFilter.CanRead(await ScopeAsync(LmsWorld.Member), chunk).Should().BeFalse();
			KnowledgeAccessFilter.CanRead(await ScopeAsync("manager"), chunk).Should().BeTrue();

			// The LMS gate closes it for everyone, managers included; so does a reader who cannot view trainings.
			_world.Gate.AddonActive = false;
			Readable(await ScopeAsync("manager")).Should().BeEmpty();
			_world.Gate.AddonActive = true;
			var noTrainings = new KnowledgeReaderScope { DepartmentId = Dept, UserId = "manager", IsCurrentMember = true };
			await _source.AddReadableAsync(new KnowledgeReader { DepartmentId = Dept, UserId = "manager", CanViewTrainings = false }, noTrainings);
			Readable(noTrainings).Should().BeEmpty();
		}

		[Test]
		public async Task Saving_or_retiring_a_question_and_deleting_its_bank_queue_the_bank_for_reindex()
		{
			var bank = await BankAsync(true, LmsWorld.Admin);
			var admin = _world.AdminActor;
			var id = bank.LmsQuestionBankId.ToString();

			var saved = await _world.BankService.SaveQuestionAsync(admin, new LmsQuestion
			{
				LmsQuestionBankId = bank.LmsQuestionBankId, Kind = (int)LmsQuestionKinds.TrueFalse, Text = "Pumps need water", Explanation = "Always",
				Options = new List<LmsQuestionOption> { new LmsQuestionOption { Text = "True", IsCorrect = true }, new LmsQuestionOption { Text = "False" } }
			});
			saved.Error.Should().Be(LmsErrors.None);
			await _world.BankService.RetireQuestionAsync(admin, ((LmsQuestion)saved.Value).LmsQuestionId);
			await _world.BankService.DeleteBankAsync(admin, bank.LmsQuestionBankId);

			_world.KnowledgeQueue.Verify(q => q.EnqueueAsync(Dept, KnowledgeSourceKinds.QuestionExplanation, id, It.IsAny<CancellationToken>()), Times.Exactly(3));
		}

		[Test]
		public void A_citation_opens_the_bank_editor_at_the_question()
		{
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.QuestionExplanation, "12", "r1", "q345").Should().Be("/User/Courses/Bank/12?question=345");
			KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.QuestionExplanation, "12", "r1", "bogus").Should().Be("/User/Courses/Bank/12");
			KnowledgeLinks.QuestionAnchor(345).Should().Be("q345");
			KnowledgeLinks.QuestionAnchor(0).Should().BeNull();
			KnowledgeLinks.TryParseQuestionAnchor("q0", out _).Should().BeFalse();
		}
	}
}
