using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>Course origins in memory (one per course).</summary>
	public sealed class FakeLmsCourseOrigins : MemRepo<LmsCourseOrigin>, ILmsCourseOriginsRepository
	{
		public Task<LmsCourseOrigin> GetForCourseAsync(int departmentId, int lmsCourseId) =>
			Task.FromResult(One(o => o.DepartmentId == departmentId && o.LmsCourseId == lmsCourseId));

		public Task<List<LmsCourseOrigin>> GetForDepartmentAsync(int departmentId) => Task.FromResult(Out(Rows.Where(o => o.DepartmentId == departmentId)));
	}

	/// <summary>Course flashcards in memory, with the supersede update's semantics and a count of every write.</summary>
	public sealed class FakeLmsFlashcards : MemRepo<LmsFlashcard>, ILmsFlashcardsRepository
	{
		public int Writes;

		public override Task<LmsFlashcard> InsertAsync(LmsFlashcard entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			Writes++;
			return base.InsertAsync(entity, cancellationToken, firstLevelOnly);
		}

		public Task<List<LmsFlashcard>> GetForCourseAsync(int departmentId, int lmsCourseId) =>
			Task.FromResult(Out(Rows.Where(c => c.DepartmentId == departmentId && c.LmsCourseId == lmsCourseId).OrderBy(c => c.SortOrder).ThenBy(c => c.LmsFlashcardId)));

		public Task<LmsFlashcard> GetByIdForDepartmentAsync(int departmentId, int lmsFlashcardId) =>
			Task.FromResult(One(c => c.DepartmentId == departmentId && c.LmsFlashcardId == lmsFlashcardId));

		public Task<int> FlagOtherVersionsAsync(int departmentId, int sourceKind, int sourceItemId, int versionInForceId, int reason, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			Writes++;
			var flagged = 0;
			foreach (var card in Rows.Where(c => c.DepartmentId == departmentId && c.SourceKind == sourceKind && c.SourceItemId == sourceItemId &&
				c.SourceVersionId != versionInForceId && c.Status == (int)LmsFlashcardStatuses.Active))
			{
				card.Status = (int)LmsFlashcardStatuses.NeedsReview;
				card.FlagReason = reason;
				card.FlaggedOn = utcNow;
				flagged++;
			}
			return Task.FromResult(flagged);
		}

		public Task<int> FlagCardsAsync(int departmentId, IEnumerable<int> lmsFlashcardIds, int reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			Writes++;
			var ids = new HashSet<int>(lmsFlashcardIds ?? Enumerable.Empty<int>());
			var flagged = 0;
			foreach (var card in Rows.Where(c => c.DepartmentId == departmentId && ids.Contains(c.LmsFlashcardId) && c.Status == (int)LmsFlashcardStatuses.Active))
			{
				card.Status = (int)LmsFlashcardStatuses.NeedsReview;
				card.FlagReason = reason;
				card.FlaggedOn = utcNow;
				flagged++;
			}
			return Task.FromResult(flagged);
		}
	}

	/// <summary>
	/// The T5 course studio wired to the T2 in-memory LMS world (the real course and question bank services), with mocks for the AI plumbing
	/// (no live model: the LLM client is a mock) and the protocol library. One published protocol (40, version 401) of HTML sections.
	/// </summary>
	public sealed class CourseStudioWorld : IDisposable
	{
		public const int DepartmentId = LmsWorld.DepartmentId;
		public const int ProtocolId = 40;
		public const int VersionId = 401;

		public const string ProtocolHtml = "<h2>Adult cardiac arrest</h2><p>Start compressions at 100 to 120 per minute.</p><p>Give epinephrine 1 mg IV every 3 to 5 minutes.</p>" +
			"<h2>Defibrillation</h2><p>Attach the defibrillator as soon as it arrives and follow its prompts.</p>";

		private readonly (string, string, string, string, string, string) _saved;

		public readonly LmsWorld Lms = new LmsWorld();
		public readonly FakeLmsCourseOrigins Origins = new FakeLmsCourseOrigins();
		public readonly FakeLmsFlashcards Cards = new FakeLmsFlashcards();
		public readonly Mock<IAiKnowledgeService> Knowledge = new Mock<IAiKnowledgeService>();
		public readonly Mock<ILmsGateService> Gate = new Mock<ILmsGateService>();
		public readonly Mock<IDepartmentDataProtectionService> Protection = new Mock<IDepartmentDataProtectionService>();
		public readonly Mock<IProtocolAccessService> ProtocolAccess = new Mock<IProtocolAccessService>();
		public readonly Mock<IProtocolLibraryService> Library = new Mock<IProtocolLibraryService>();
		public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
		public readonly Mock<IAdminAssistConversationProtection> ConversationProtection = new Mock<IAdminAssistConversationProtection>();
		public readonly Mock<IAiBackgroundAdmission> Admission = new Mock<IAiBackgroundAdmission>();
		public readonly Mock<IAiUsageMeter> Usage = new Mock<IAiUsageMeter>();
		public readonly Mock<ILlmClient> Llm = new Mock<ILlmClient>();
		public readonly Mock<IKnowledgeGapsRepository> Generations = new Mock<IKnowledgeGapsRepository>();
		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
		public readonly List<AiGenerationRow> Rows = new List<AiGenerationRow>();
		public readonly List<LlmRequest> Requests = new List<LlmRequest>();

		public DispatchProtocol Protocol;
		public readonly List<ProtocolVersion> Versions = new List<ProtocolVersion>();
		public ProtocolReader Reader;

		public readonly LmsCourseDraftService Drafts;
		public readonly LmsFlashcardService Flashcards;
		public readonly LmsStarterCourseService Starters;

		public CourseStudioWorld()
		{
			_saved = (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds);
			AiConfig.Endpoint = "https://ai.example.test/v1/chat/completions";
			AiConfig.ApiKey = "test-key";
			AiConfig.Model = "Qwen/Qwen3-8B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.SelfHostedDepartmentIds = "";

			Knowledge.Setup(x => x.IsEntitledAsync(DepartmentId)).ReturnsAsync(true);
			Gate.Setup(x => x.GetActorAsync(DepartmentId, It.IsAny<string>()))
				.ReturnsAsync((int d, string user) => string.Equals(user, LmsWorld.Admin, StringComparison.OrdinalIgnoreCase) ? Lms.AdminActor : Lms.Actor(user));
			// Round 10 A7: the learner deck needs the member's LMS seat (the LMS world seats every member by default).
			Gate.Setup(x => x.HoldsLearnerSeatAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((int d, string user) => Lms.SeatedMembers.Contains(user));
			Protection.Setup(x => x.GetStateAsync(DepartmentId, true)).ReturnsAsync(DepartmentDataProtectionState.Disabled);

			Protocol = new DispatchProtocol
			{
				DispatchProtocolId = ProtocolId, DepartmentId = DepartmentId, Name = "Cardiac arrest", Status = (int)ProtocolStatuses.Published, CurrentVersionId = VersionId
			};
			Versions.Add(new ProtocolVersion
			{
				ProtocolVersionId = VersionId, DepartmentId = DepartmentId, DispatchProtocolId = ProtocolId, Major = 1, Minor = 0, Status = (int)ProtocolVersionStatuses.Published,
				ContentHtml = ProtocolHtml
			});
			Reader = new ProtocolReader { DepartmentId = DepartmentId, UserId = LmsWorld.Admin, CanAuthor = true, IsCurrentMember = true };
			ProtocolAccess.Setup(x => x.GetReaderAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync(() => Reader);
			ProtocolAccess.Setup(x => x.GetReadableAsync(DepartmentId, It.IsAny<string>(), ProtocolId)).ReturnsAsync(() => Protocol);
			ProtocolAccess.Setup(x => x.FilterReadableAsync(It.IsAny<ProtocolReader>(), It.IsAny<IEnumerable<DispatchProtocol>>()))
				.ReturnsAsync((ProtocolReader r, IEnumerable<DispatchProtocol> list) => list.ToList());
			Protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => new List<DispatchProtocol> { Protocol });
			Library.Setup(x => x.GetVersionAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((int d, int id) => Versions.FirstOrDefault(v => v.ProtocolVersionId == id));
			Library.Setup(x => x.GetVersionsAsync(DepartmentId, ProtocolId, It.IsAny<bool>())).ReturnsAsync(() => Versions.ToList());

			// The audit store: rows kept in memory, "encrypted" with a prefix, read back as the stored content.
			Generations.Setup(x => x.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.Callback((AiGenerationRow r, CancellationToken _) => Rows.Add(r)).Returns(Task.CompletedTask);
			Generations.Setup(x => x.GetGenerationAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string user, string id, CancellationToken _) =>
					Rows.FirstOrDefault(r => r.Id == id && string.Equals(r.UserId, user, StringComparison.OrdinalIgnoreCase)));
			ConversationProtection.Setup(x => x.ProtectAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.Callback((AdminAssistActor _, AiGenerationRow row, CancellationToken __) => row.Content = "enc:" + row.Content).Returns(Task.CompletedTask);
			ConversationProtection.Setup(x => x.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((AdminAssistActor _, AiGenerationRow row, CancellationToken __) => JsonSerializer.Deserialize<AskStoredContent>(row.Content.Substring("enc:".Length)));

			Admission.Setup(x => x.ReserveBackgroundAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync((int d, string f, string t, DateTime n, int tokens, int limit, CancellationToken ct, int? p) => new AiUsageReservation("r1", DepartmentId, "background", tokens, DateTime.UtcNow.AddMinutes(2)));
			Usage.Setup(x => x.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);

			Drafts = new LmsCourseDraftService(Knowledge.Object, Gate.Object, Protection.Object, ProtocolAccess.Object, Library.Object, Protocols.Object,
				Mock.Of<IKnowledgePageTextSource>(), Mock.Of<IProtocolTextExtractor>(), Lms.CourseService, Lms.BankService, Origins, ConversationProtection.Object,
				Admission.Object, Usage.Object, new Lazy<ILlmClient>(() => Llm.Object), Generations.Object, Events.Object, TimeProvider.System);
			Flashcards = new LmsFlashcardService(Knowledge.Object, Gate.Object, Protection.Object, ProtocolAccess.Object, Library.Object, Mock.Of<IKnowledgePageTextSource>(),
				Lms.CourseService, Origins, Cards, Lms.Enrollments, ConversationProtection.Object, Admission.Object, Usage.Object, new Lazy<ILlmClient>(() => Llm.Object),
				Generations.Object, Events.Object, TimeProvider.System);
			Starters = new LmsStarterCourseService(Lms.CourseService, Lms.BankService, Origins, Events.Object);

			// The supersede hook on the real course service (protocol publishing is the library's; tests call the hook directly).
			Lms.PublishedHooks.Add(new LmsFlashcardSupersedeHook(Cards));
		}

		/// <summary>The next model call returns <paramref name="arguments"/> as a call of <paramref name="tool"/>.</summary>
		public void ModelReturns(string tool, object arguments) =>
			Llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => Requests.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", tool, JsonSerializer.Serialize(arguments)) }, 900, 300, "tool_calls"));

		public void NoModelCall() => Llm.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		/// <summary>Submits, approves (L2) and publishes a draft course version on the real course service.</summary>
		public async Task<LmsCourseVersion> PublishAsync(int versionId, bool major = false)
		{
			var admin = Lms.AdminActor;
			var submitted = await Lms.CourseService.SubmitAsync(admin, versionId, "Change summary");
			if (!submitted.Succeeded) throw new InvalidOperationException("submit " + submitted.Error + " " + submitted.LifecycleError);
			var approved = await Lms.CourseService.ApproveAsync(admin, versionId, "ok", new SignatureProof { UserId = LmsWorld.Admin, StepUpVerifiedOnUtc = DateTime.UtcNow });
			if (!approved.Succeeded) throw new InvalidOperationException("approve " + approved.Error + " " + approved.LifecycleError);
			var published = await Lms.CourseService.PublishAsync(admin, versionId, major, null, LmsMajorChangePolicies.Nothing, null);
			if (!published.Succeeded) throw new InvalidOperationException("publish " + published.Error + " " + published.LifecycleError);
			return published.Version;
		}

		/// <summary>The live questions per bank, for the course validator.</summary>
		public IReadOnlyDictionary<int, int> LiveQuestionsByBank() =>
			Lms.Questions.Rows.Where(q => !q.IsRetired).GroupBy(q => q.LmsQuestionBankId).ToDictionary(g => g.Key, g => g.Count());

		public void Dispose()
		{
			(AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds) = _saved;
		}
	}
}
