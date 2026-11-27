using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Ai;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.ProtocolAi
{
	/// <summary>
	/// Round 7A (product decision 2026-11-25: Enhanced AI pays for every model call; the LMS is needed only for LMS-only content): protocol
	/// flashcards, the training drafted from a protocol and translation drafts need only Enhanced AI with knowledge answers, fail closed, never
	/// send "no AI" or licensed content, keep content local under Advanced Data Protection, log every output to AiGenerations, validate numbers
	/// against the source and save nothing without an explicit author accept. No LMS type is wired into any of them.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ProtocolAiToolsTests
	{
		private ProtocolAiWorld _world;

		[SetUp]
		public void SetUp() => _world = new ProtocolAiWorld();

		[TearDown]
		public void TearDown() => _world.Dispose();

		private const int Dept = ProtocolAiWorld.DepartmentId;

		#region No LMS anywhere

		[Test]
		public void None_of_the_protocol_ai_tools_or_protocol_practice_depends_on_an_LMS_type()
		{
			foreach (var type in new[] { typeof(ProtocolFlashcardService), typeof(ProtocolTrainingDraftService), typeof(ProtocolTranslationDraftService), typeof(KnowledgePracticeService),
				typeof(ProtocolFlashcardSupersedeHook) })
			{
				var parameters = type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
				parameters.Should().NotContain(t => t.Name.StartsWith("ILms", StringComparison.Ordinal) || t.Name.StartsWith("Lms", StringComparison.Ordinal) ||
					(t.Namespace ?? string.Empty) == "Resgrid.Model.Lms", type.Name + " needs only Enhanced AI (decision 2026-11-25)");
			}
		}

		[Test]
		public void Practice_cards_and_drafts_hold_no_writer_of_records_certifications_attempts_or_enrollments()
		{
			var forbidden = new[]
			{
				typeof(ILearningRecordWriter), typeof(ILearningRecordService), typeof(ICertificationService), typeof(IProtocolAcknowledgmentService),
				typeof(ILearningRecordsRepository), typeof(ITrainingService)
			};
			foreach (var type in new[] { typeof(ProtocolFlashcardService), typeof(ProtocolFlashcardSupersedeHook), typeof(ProtocolTranslationDraftService) })
				type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).Should().NotIntersectWith(forbidden, type.Name);
		}

		#endregion No LMS anywhere

		#region Flashcards

		private ProtocolFlashcardDraftRequest CardRequest(string user = ProtocolAiWorld.Author) => new ProtocolFlashcardDraftRequest
		{
			DepartmentId = Dept, UserId = user, Locale = "en", ProtocolId = ProtocolAiWorld.ProtocolId, Count = 5
		};

		private static object Card(string front, string back, string source) => new { front, back, source };

		private async Task<ProtocolFlashcardDraft> DraftCardsAsync()
		{
			var arrest = await _world.RefOfAsync("100 to 120");
			var defib = await _world.RefOfAsync("defibrillator");
			_world.ModelReturns(AiCourseStudioPrompt.CardsTool, new
			{
				cards = new object[]
				{
					Card("At what rate do you compress?", "100 to 120 per minute.", arrest),
					Card("When do you attach the defibrillator?", "As soon as it arrives.", defib),
					// A computed dose the section does not hold: left out.
					Card("How much epinephrine for a 50 kg patient?", "0.5 mg.", arrest),
					// A section that was not shown: left out.
					Card("What next?", "Rest.", "S9")
				}
			});
			return await _world.Flashcards.DraftAsync(CardRequest());
		}

		[Test]
		public async Task Flashcards_fail_closed_without_Enhanced_AI_and_never_send_no_AI_licensed_or_protected_content()
		{
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(false);
			(await _world.Flashcards.DraftAsync(CardRequest())).Outcome.Should().Be(ProtocolAiOutcomes.NotEntitled);
			(await _world.Flashcards.GetStatusAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId)).Available.Should().BeFalse();
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await _world.Flashcards.DraftAsync(CardRequest())).Outcome.Should().Be(ProtocolAiOutcomes.NotEntitled, "an unreadable entitlement is closed");
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(true);

			(await _world.Flashcards.DraftAsync(CardRequest(ProtocolAiWorld.Member))).Outcome.Should().Be(ProtocolAiOutcomes.NotAllowed, "only authors and approvers draft");

			_world.Protocol.ExcludeFromAi = true;
			(await _world.Flashcards.DraftAsync(CardRequest())).Outcome.Should().Be(ProtocolAiOutcomes.Excluded);
			_world.Protocol.ExcludeFromAi = false;
			_world.Protocol.IsLicensedContent = true;
			(await _world.Flashcards.DraftAsync(CardRequest())).Outcome.Should().Be(ProtocolAiOutcomes.Excluded);
			_world.Protocol.IsLicensedContent = false;

			_world.Protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Enabled);
			(await _world.Flashcards.DraftAsync(CardRequest())).Outcome.Should().Be(ProtocolAiOutcomes.KeptLocal);
			(await _world.Flashcards.GetStatusAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId)).Reason.Should().Be(ProtocolAiOutcomes.KeptLocal);
			_world.Protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);

			_world.Protocol.Status = (int)ProtocolStatuses.Draft;
			(await _world.Flashcards.DraftAsync(CardRequest())).Outcome.Should().Be(ProtocolAiOutcomes.NotFound, "only a published version in force");

			_world.NoModelCall();
			_world.Rows.Should().BeEmpty();
			_world.Cards.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Drafted_cards_cite_one_section_hold_only_its_numbers_and_are_saved_only_when_accepted_from_the_stored_draft()
		{
			var draft = await DraftCardsAsync();

			draft.Outcome.Should().Be(ProtocolAiOutcomes.Drafted);
			draft.Cards.Select(c => c.Front).Should().Equal("At what rate do you compress?", "When do you attach the defibrillator?");
			draft.Dropped.Should().Be(2);
			_world.Cards.Rows.Should().BeEmpty("a draft saves nothing but its audit row");
			_world.Rows.Should().ContainSingle().Which.PromptVersion.Should().Be(AiProtocolStudioPrompt.CardsVersion);
			_world.Usage.Verify(x => x.CompleteAsync(It.IsAny<Resgrid.Model.AdminAssist.AiUsageReservation>(), 1200, "Answered", It.IsAny<CancellationToken>()), Times.Once,
				"charged to the Enhanced AI meter like every model call");

			// Another member cannot accept this author's draft; an unknown exchange or no ticked card is refused.
			(await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 0 })).Outcome.Should().Be(ProtocolAiOutcomes.NotAllowed);
			(await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, Guid.NewGuid().ToString("D"), new[] { 0 })).Outcome.Should().Be(ProtocolAiOutcomes.NotFound);
			(await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, Array.Empty<int>())).Outcome.Should().Be(ProtocolAiOutcomes.Invalid);
			_world.Cards.Rows.Should().BeEmpty();

			var accepted = await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 1, 0, 7 });
			accepted.Outcome.Should().Be(ProtocolAiOutcomes.Created);
			accepted.Count.Should().Be(2);
			_world.Cards.Rows.Should().HaveCount(2).And.OnlyContain(c => c.ProtocolVersionId == ProtocolAiWorld.VersionId && c.IsActive && c.ExchangeId == draft.ExchangeId);
			_world.Cards.Rows.Select(c => c.Front).Should().Equal("At what rate do you compress?", "When do you attach the defibrillator?");
			_world.Cards.Rows.Should().OnlyContain(c => !string.IsNullOrEmpty(c.SourceAnchor), "every card cites its section");

			// Accepting twice adds nothing (the same fronts).
			(await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 0, 1 })).Count.Should().Be(0);
		}

		[Test]
		public async Task An_author_edit_keeps_the_card_true_to_its_section()
		{
			var draft = await DraftCardsAsync();
			await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 0 });
			var card = _world.Cards.Rows.Single();

			(await _world.Flashcards.SaveAsync(Dept, ProtocolAiWorld.Author, card.ProtocolFlashcardId, "Compression rate?", "Between 90 and 130.")).Outcome
				.Should().Be(ProtocolAiOutcomes.Refused, "numbers the section does not hold");
			(await _world.Flashcards.SaveAsync(Dept, ProtocolAiWorld.Member, card.ProtocolFlashcardId, "Compression rate?", "100 to 120.")).Outcome.Should().Be(ProtocolAiOutcomes.NotAllowed);
			(await _world.Flashcards.SaveAsync(Dept, ProtocolAiWorld.Author, card.ProtocolFlashcardId, "Compression rate?", "100 to 120 per minute.")).Succeeded.Should().BeTrue();
			_world.Cards.Rows.Single().Front.Should().Be("Compression rate?");
		}

		[Test]
		public async Task Members_practise_the_cards_of_the_version_in_force_and_a_new_version_flags_and_hides_them_until_reviewed()
		{
			var draft = await DraftCardsAsync();
			await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 0, 1 });

			var deck = await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId);
			deck.Should().HaveCount(2);
			deck.Should().OnlyContain(c => c.SourceUrl.StartsWith("/User/Protocols/View/40?versionId=401#", StringComparison.Ordinal));

			// The deck reads only: nothing is written while members practise.
			var writes = _world.Cards.Writes;
			await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId);
			_world.Cards.Writes.Should().Be(writes);

			// Gates on the deck: Enhanced AI off, a protocol the member may not read, or one now kept from AI shows nothing.
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(false);
			(await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId)).Should().BeEmpty();
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(true);
			_world.MemberMayRead = false;
			(await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId)).Should().BeEmpty();
			_world.MemberMayRead = true;
			_world.Protocol.ExcludeFromAi = true;
			(await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId)).Should().BeEmpty();
			_world.Protocol.ExcludeFromAi = false;

			// A newer version goes into force: the publish hook flags the cards and the deck hides them.
			_world.PublishNewVersion(402, ProtocolAiWorld.ProtocolHtml.Replace("follow its prompts", "follow its voice prompts"));
			var hook = new ProtocolFlashcardSupersedeHook(_world.Cards);
			await hook.OnVersionPublishedAsync(new ContentVersionPublished { DepartmentId = Dept, Kind = ContentVersionKinds.CourseVersion, ItemId = ProtocolAiWorld.ProtocolId, VersionId = 402 });
			_world.Cards.Rows.Should().OnlyContain(c => c.IsActive, "a course version is not this protocol");
			await hook.OnVersionPublishedAsync(new ContentVersionPublished { DepartmentId = Dept, Kind = ContentVersionKinds.ProtocolVersion, ItemId = ProtocolAiWorld.ProtocolId, VersionId = 402, PublishedOnUtc = DateTime.UtcNow });
			_world.Cards.Rows.Should().OnlyContain(c => c.Status == (int)ProtocolFlashcardStatuses.NeedsReview && c.FlagReason == (int)ProtocolFlashcardFlagReasons.SourceSuperseded);
			(await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId)).Should().BeEmpty("flagged cards are hidden until reviewed");

			// Review against the version in force: the rate card's section is unchanged and is shown again; the defibrillator card's section changed
			// its anchor-free text but still holds no number the card shows, so it is re-confirmed too.
			var rate = _world.Cards.Rows.Single(c => c.Front.StartsWith("At what rate", StringComparison.Ordinal));
			var reviewed = await _world.Flashcards.ReviewAsync(Dept, ProtocolAiWorld.Author, rate.ProtocolFlashcardId);
			reviewed.Succeeded.Should().BeTrue();
			reviewed.Card.ProtocolVersionId.Should().Be(402);
			reviewed.Card.ReviewedByUserId.Should().Be(ProtocolAiWorld.Author);
			(await _world.Flashcards.GetDeckAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId)).Should().ContainSingle(c => c.Id == rate.ProtocolFlashcardId);
		}

		[Test]
		public async Task A_card_whose_numbers_left_the_new_version_cannot_be_reviewed_back()
		{
			var draft = await DraftCardsAsync();
			await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 0 });
			_world.PublishNewVersion(402, ProtocolAiWorld.ProtocolHtml.Replace("100 to 120", "110 to 130"));
			await new ProtocolFlashcardSupersedeHook(_world.Cards).OnVersionPublishedAsync(new ContentVersionPublished
			{
				DepartmentId = Dept, Kind = ContentVersionKinds.ProtocolVersion, ItemId = ProtocolAiWorld.ProtocolId, VersionId = 402
			});

			var card = _world.Cards.Rows.Single();
			(await _world.Flashcards.ReviewAsync(Dept, ProtocolAiWorld.Author, card.ProtocolFlashcardId)).Outcome.Should().Be(ProtocolAiOutcomes.Refused);
			_world.Cards.Rows.Single().IsActive.Should().BeFalse();

			// An accept of the old draft after the new version went into force is stale.
			(await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 1 })).Outcome.Should().Be(ProtocolAiOutcomes.Stale);
		}

		[Test]
		public async Task The_authors_list_flags_cards_the_hook_missed()
		{
			var draft = await DraftCardsAsync();
			await _world.Flashcards.AcceptAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, new[] { 0 });
			_world.PublishNewVersion(402, ProtocolAiWorld.ProtocolHtml);

			var cards = await _world.Flashcards.GetCardsAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId);
			cards.Should().ContainSingle().Which.Status.Should().Be((int)ProtocolFlashcardStatuses.NeedsReview);
			(await _world.Flashcards.GetCardsAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId)).Should().BeEmpty("members do not manage cards");
		}

		#endregion Flashcards

		#region Training draft

		private ProtocolTrainingDraftRequest TrainingRequest(string user = ProtocolAiWorld.Author) => new ProtocolTrainingDraftRequest
		{
			DepartmentId = Dept, UserId = user, Locale = "en", ProtocolId = ProtocolAiWorld.ProtocolId, QuestionCount = 3
		};

		private async Task<ProtocolTrainingDraft> DraftTrainingAsync()
		{
			var arrest = await _world.RefOfAsync("100 to 120");
			var defib = await _world.RefOfAsync("defibrillator");
			_world.ModelReturns(AiProtocolStudioPrompt.TrainingTool, new
			{
				title = "Cardiac arrest refresher",
				summary = "The department's cardiac arrest protocol in two parts.",
				sections = new object[]
				{
					new { title = "Compressions and medication", intro = "Start compressions at once.", key_points = new[] { "100 to 120 per minute", "Epinephrine every 3 to 5 minutes" }, sources = new[] { arrest } },
					new { title = "Defibrillation", intro = "Attach the defibrillator as soon as it arrives.", key_points = new string[0], sources = new[] { defib } },
					// A number the cited section does not hold: left out.
					new { title = "Ventilation", intro = "Give 2 breaths after 30 compressions.", key_points = new string[0], sources = new[] { arrest } }
				},
				questions = new object[]
				{
					new { question = "At what rate do you compress?", options = new[] { "100 to 120 per minute", "As slowly as possible", "Only when tired" }, correct_index = 0,
						explanation = "The protocol sets 100 to 120 per minute.", sources = new[] { arrest } },
					// A computed dose: left out.
					new { question = "Epinephrine for 40 kg?", options = new[] { "0.4 mg", "1 mg", "None" }, correct_index = 0, explanation = "0.01 mg per kg.", sources = new[] { arrest } }
				}
			});
			return await _world.TrainingDrafts.DraftAsync(TrainingRequest());
		}

		[Test]
		public async Task The_training_draft_fails_closed_and_never_sends_licensed_or_no_AI_protocols()
		{
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(false);
			(await _world.TrainingDrafts.DraftAsync(TrainingRequest())).Outcome.Should().Be(ProtocolAiOutcomes.NotEntitled);
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(true);

			(await _world.TrainingDrafts.DraftAsync(TrainingRequest(ProtocolAiWorld.Member))).Outcome.Should().Be(ProtocolAiOutcomes.NotAllowed);
			_world.CanCreateTrainings = false;
			(await _world.TrainingDrafts.DraftAsync(TrainingRequest())).Outcome.Should().Be(ProtocolAiOutcomes.NotAllowed, "a training draft needs the right to create trainings");
			(await _world.TrainingDrafts.GetStatusAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId)).Available.Should().BeFalse();
			_world.CanCreateTrainings = true;

			_world.Protocol.IsLicensedContent = true;
			(await _world.TrainingDrafts.DraftAsync(TrainingRequest())).Outcome.Should().Be(ProtocolAiOutcomes.Excluded);
			_world.Protocol.IsLicensedContent = false;
			_world.Protocol.ExcludeFromAi = true;
			(await _world.TrainingDrafts.DraftAsync(TrainingRequest())).Outcome.Should().Be(ProtocolAiOutcomes.Excluded);
			_world.Protocol.ExcludeFromAi = false;

			_world.Protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Encrypting);
			(await _world.TrainingDrafts.DraftAsync(TrainingRequest())).Outcome.Should().Be(ProtocolAiOutcomes.KeptLocal);

			_world.NoModelCall();
			_world.Rows.Should().BeEmpty();
			_world.SavedTrainings.Should().BeEmpty();
		}

		[Test]
		public async Task A_training_is_drafted_with_cited_parts_and_valid_questions_and_created_unassigned_only_when_the_author_accepts()
		{
			var draft = await DraftTrainingAsync();

			draft.Outcome.Should().Be(ProtocolAiOutcomes.Drafted);
			draft.Sections.Select(s => s.Title).Should().Equal("Compressions and medication", "Defibrillation");
			draft.Questions.Should().ContainSingle().Which.Explanation.Should().Contain("100 to 120");
			draft.Dropped.Should().Be(2, "a part and a question with numbers the protocol does not hold");
			draft.Sections.Should().OnlyContain(s => s.Citations.Count == 1 && s.Citations[0].Url.Contains("versionId=401#"));
			_world.SavedTrainings.Should().BeEmpty("nothing is created before the author accepts");
			_world.Rows.Should().ContainSingle().Which.PromptVersion.Should().Be(AiProtocolStudioPrompt.TrainingVersion);

			// Another member's accept, or an unknown exchange, creates nothing.
			(await _world.TrainingDrafts.CreateAsync(Dept, ProtocolAiWorld.Member, ProtocolAiWorld.ProtocolId, draft.ExchangeId, null, "en")).Succeeded.Should().BeFalse();
			(await _world.TrainingDrafts.CreateAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, Guid.NewGuid().ToString("D"), null, "en")).Outcome
				.Should().Be(ProtocolAiOutcomes.NotFound);
			_world.SavedTrainings.Should().BeEmpty();

			var created = await _world.TrainingDrafts.CreateAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, "  CPR refresher ", "en");
			created.Outcome.Should().Be(ProtocolAiOutcomes.Created);
			created.Questions.Should().Be(1);

			var training = _world.SavedTrainings.Single();
			training.TrainingId.Should().Be(created.TrainingId);
			training.Name.Should().Be("CPR refresher");
			training.DepartmentId.Should().Be(Dept);
			training.CreatedByUserId.Should().Be(ProtocolAiWorld.Author);
			// A draft: never assigned or published.
			training.OpenToAllMembers.Should().BeFalse();
			training.AllowSelfEnroll.Should().BeFalse();
			training.Users.Should().BeEmpty();
			training.Audiences.Should().BeEmpty();
			training.UsersToAdd.Should().BeNullOrEmpty();
			training.GroupsToAdd.Should().BeNullOrEmpty();
			training.RolesToAdd.Should().BeNullOrEmpty();
			// The cited sections' own text and a citation line to the version and anchor; doses only as the protocol wrote them.
			training.TrainingText.Should().Contain("<blockquote>").And.Contain("Give epinephrine 1 mg IV every 3 to 5 minutes.").And.Contain("Cited:");
			training.TrainingText.Should().NotContain("0.4 mg").And.NotContain("2 breaths");
			training.Description.Should().Contain("Cardiac arrest 1.0");
			// A baseline single-choice quiz with one correct option.
			training.Questions.Should().ContainSingle();
			var question = training.Questions.Single();
			question.Type.Should().Be((int)TrainingQuestionTypes.SingleChoice);
			question.Answers.Count(a => a.Correct).Should().Be(1);
			question.Answers.Single(a => a.Correct).Answer.Should().Be("100 to 120 per minute");
			training.MinimumScore.Should().BeGreaterThan(0);
		}

		[Test]
		public async Task A_training_draft_of_a_version_no_longer_in_force_is_stale()
		{
			var draft = await DraftTrainingAsync();
			_world.PublishNewVersion(402, ProtocolAiWorld.ProtocolHtml);

			(await _world.TrainingDrafts.CreateAsync(Dept, ProtocolAiWorld.Author, ProtocolAiWorld.ProtocolId, draft.ExchangeId, null, "en")).Outcome.Should().Be(ProtocolAiOutcomes.Stale);
			_world.SavedTrainings.Should().BeEmpty();
		}

		[Test]
		public async Task A_training_draft_with_no_valid_part_is_refused_and_logged()
		{
			var arrest = await _world.RefOfAsync("100 to 120");
			_world.ModelReturns(AiProtocolStudioPrompt.TrainingTool, new
			{
				sections = new object[] { new { title = "Invented", intro = "Give 7 mg.", key_points = new string[0], sources = new[] { arrest } } }
			});

			var draft = await _world.TrainingDrafts.DraftAsync(TrainingRequest());

			draft.Outcome.Should().Be(ProtocolAiOutcomes.Refused);
			draft.Sections.Should().BeEmpty();
			_world.Rows.Should().ContainSingle().Which.Outcome.Should().Be(ProtocolAiOutcomes.Refused);
			_world.SavedTrainings.Should().BeEmpty();
		}

		#endregion Training draft

		#region Translation draft

		private const int TranslationId = 77;

		private ProtocolVersionTranslation _translation;

		/// <summary>A locked block version (heading, a paragraph with a link, a medication with doses, steps) and a German draft translation of it.</summary>
		private ProtocolContentDocument BlockVersion(string translatedHeading = null)
		{
			var source = ProtocolBlocks.Normalize(new ProtocolContentDocument
			{
				Schema = ProtocolContentDocument.BlockSchema,
				Blocks = new List<ProtocolContentBlock>
				{
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = "h1", Text = "Adult cardiac arrest", Level = 2 },
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Paragraph, Key = "p1", Html = "<p>Compress at 100 to 120 per minute. See <a href=\"https://example.org/cpr\">the guide</a>.</p>" },
					new ProtocolContentBlock
					{
						Type = ProtocolBlockTypes.Medication, Key = "m1",
						Medication = new ProtocolMedication { Drug = "Epinephrine", DosePerKg = "0.01 mg/kg", MaxSingleDose = "1 mg", Route = "IV", RepeatInterval = "3-5 min", Indication = "Cardiac arrest" }
					},
					new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "s1", Title = "Steps", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "i1", Html = "Check the pulse for 10 seconds." } } }
				}
			});
			var version = _world.Versions.Single();
			version.ContentJson = source.ToJson();
			version.ContentHtml = source.Render();

			var start = ProtocolContentDocument.Parse(source.ToJson(), null);
			if (translatedHeading != null)
				start.Blocks.First(b => b.Key == "h1").Text = translatedHeading;
			_translation = new ProtocolVersionTranslation
			{
				ProtocolVersionTranslationId = TranslationId, DepartmentId = Dept, DispatchProtocolId = ProtocolAiWorld.ProtocolId, ProtocolVersionId = ProtocolAiWorld.VersionId,
				Locale = "de", Status = (int)ProtocolTranslationStatuses.Draft, ContentJson = start.ToJson(), ContentHtml = start.Render(), RowVersion = 3
			};
			_world.Translations.Setup(x => x.GetAsync(Dept, TranslationId)).ReturnsAsync(() => _translation);
			return source;
		}

		/// <summary>The segments the model was shown (the JSON inside the SEGMENTS block).</summary>
		private static List<(string Id, string Kind, string Text)> Shown(LlmRequest request)
		{
			var user = request.Messages.Last().Content;
			var json = user.Substring(user.IndexOf("<<<SEGMENTS", StringComparison.Ordinal) + "<<<SEGMENTS".Length);
			json = json.Substring(0, json.IndexOf("SEGMENTS>>>", StringComparison.Ordinal)).Trim();
			using var document = JsonDocument.Parse(json);
			return document.RootElement.EnumerateArray().Select(e => (e.GetProperty("id").GetString(), e.GetProperty("kind").GetString(), e.GetProperty("text").GetString())).ToList();
		}

		/// <summary>A German "model": translates the known phrases, keeps markup; the pulse step changes its number (and must be refused).</summary>
		private void GermanModel()
		{
			string German(string text) => text
				.Replace("Adult cardiac arrest", "Herz-Kreislauf-Stillstand bei Erwachsenen")
				.Replace("Compress at 100 to 120 per minute. See ", "Drücken Sie 100 bis 120 Mal pro Minute. Siehe ")
				.Replace("the guide", "die Anleitung")
				.Replace("Cardiac arrest", "Herzstillstand")
				.Replace("Steps", "Schritte")
				.Replace("Check the pulse for 10 seconds.", "Prüfen Sie den Puls 15 Sekunden lang.");
			_world.Llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => _world.Requests.Add(r))
				.ReturnsAsync((LlmRequest r, CancellationToken _) =>
				{
					var segments = Shown(r).Select(s => new { id = s.Id, text = German(s.Text) }).ToList();
					return new LlmResult(null, new[] { new LlmToolCall("t1", AiProtocolStudioPrompt.TranslationTool, JsonSerializer.Serialize(new { segments })) }, 700, 400, "tool_calls");
				});
		}

		[Test]
		public async Task A_translation_draft_keeps_every_source_number_dose_link_and_the_structure_and_saves_nothing()
		{
			var source = BlockVersion();
			GermanModel();

			var draft = await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId);

			draft.Outcome.Should().Be(ProtocolAiOutcomes.Drafted);
			draft.Dropped.Should().Be(1, "the pulse step changed its number");
			draft.Remaining.Should().Be(0);
			// Doses, drug, route and interval are never segments: the model never saw them. An inline link is shown as markup the reply must keep.
			var shown = Shown(_world.Requests.Single());
			shown.Select(s => s.Text).Should().NotContain(t => t.Contains("0.01 mg/kg") || t.Contains("Epinephrine") || t.Contains("3-5 min") || t.Contains("1 mg"));
			shown.Should().HaveCount(5);
			_world.Requests.Single().Messages.First().Content.Should().Contain("German");

			var doc = draft.Document;
			doc.Blocks.Select(b => b.Key).Should().Equal(source.Blocks.Select(b => b.Key), "the structure is the source's");
			doc.Blocks.Select(b => b.Type).Should().Equal(source.Blocks.Select(b => b.Type));
			doc.Blocks.Single(b => b.Key == "h1").Text.Should().Be("Herz-Kreislauf-Stillstand bei Erwachsenen");
			var paragraph = doc.Blocks.Single(b => b.Key == "p1").Html;
			paragraph.Should().Contain("100 bis 120").And.Contain("https://example.org/cpr").And.Contain("die Anleitung");
			AiProtocolStudioPrompt.SameNumbers(source.Blocks.Single(b => b.Key == "p1").Html, paragraph, true).Should().BeTrue();
			var medication = doc.Blocks.Single(b => b.Key == "m1").Medication;
			medication.DosePerKg.Should().Be("0.01 mg/kg");
			medication.MaxSingleDose.Should().Be("1 mg");
			medication.RepeatInterval.Should().Be("3-5 min");
			medication.Indication.Should().Be("Herzstillstand");
			doc.Blocks.Single(b => b.Key == "s1").Title.Should().Be("Schritte");
			doc.Blocks.Single(b => b.Key == "s1").Items.Single().Html.Should().Contain("10 seconds", "a field whose number changed keeps the source wording");

			// Nothing is saved: the editor shows it and the author saves the draft, which still needs its approval.
			_world.Translations.Verify(x => x.SaveAsync(It.IsAny<ProtocolReader>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
			_world.Translations.Verify(x => x.ApproveAsync(It.IsAny<ProtocolReader>(), It.IsAny<int>(), It.IsAny<SignatureProof>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_translation.Status.Should().Be((int)ProtocolTranslationStatuses.Draft);
			_world.Rows.Should().ContainSingle().Which.PromptVersion.Should().Be(AiProtocolStudioPrompt.TranslationVersion);
		}

		[Test]
		public async Task Fields_the_author_already_translated_are_never_sent_or_replaced()
		{
			BlockVersion(translatedHeading: "Herzstillstand (Erwachsene)");
			GermanModel();

			var draft = await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId);

			Shown(_world.Requests.Single()).Select(s => s.Text).Should().NotContain("Adult cardiac arrest");
			draft.Document.Blocks.Single(b => b.Key == "h1").Text.Should().Be("Herzstillstand (Erwachsene)");
		}

		[Test]
		public async Task Translation_drafts_fail_closed_and_keep_protected_or_licensed_content_local()
		{
			BlockVersion();
			GermanModel();

			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(false);
			(await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId)).Outcome.Should().Be(ProtocolAiOutcomes.NotEntitled);
			_world.Knowledge.Setup(x => x.IsEntitledAsync(Dept)).ReturnsAsync(true);

			(await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Member, TranslationId)).Outcome.Should().Be(ProtocolAiOutcomes.NotAllowed);

			_world.Protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Enabled);
			(await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId)).Outcome.Should().Be(ProtocolAiOutcomes.KeptLocal, "Advanced Data Protection keeps it local");
			(await _world.TranslationDrafts.GetStatusAsync(Dept, ProtocolAiWorld.Author, TranslationId)).Reason.Should().Be(ProtocolAiOutcomes.KeptLocal);
			_world.Protection.Setup(x => x.GetStateAsync(Dept, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);

			_world.Protocol.IsLicensedContent = true;
			(await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId)).Outcome.Should().Be(ProtocolAiOutcomes.Excluded);
			_world.Protocol.IsLicensedContent = false;

			_translation.Status = (int)ProtocolTranslationStatuses.Approved;
			(await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId)).Outcome.Should().Be(ProtocolAiOutcomes.NotDraft, "an approved translation is not redrafted");
			_translation.Status = (int)ProtocolTranslationStatuses.Draft;
			_world.Versions.Single().Status = (int)ProtocolVersionStatuses.Draft;
			(await _world.TranslationDrafts.DraftAsync(Dept, ProtocolAiWorld.Author, TranslationId)).Outcome.Should().Be(ProtocolAiOutcomes.NotDraft, "only a locked version is translated");

			_world.NoModelCall();
			_world.Rows.Should().BeEmpty();
		}

		#endregion Translation draft
	}
}
