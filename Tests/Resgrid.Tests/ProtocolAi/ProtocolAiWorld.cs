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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.ProtocolAi
{
	/// <summary>Protocol flashcards in memory, with the supersede update's semantics and a count of every write.</summary>
	public sealed class FakeProtocolFlashcards : MemRepo<ProtocolFlashcard>, IProtocolFlashcardsRepository
	{
		public int Writes;

		public override Task<ProtocolFlashcard> InsertAsync(ProtocolFlashcard entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			Writes++;
			return base.InsertAsync(entity, cancellationToken, firstLevelOnly);
		}

		public Task<List<ProtocolFlashcard>> GetForProtocolAsync(int departmentId, int dispatchProtocolId) =>
			Task.FromResult(Out(Rows.Where(c => c.DepartmentId == departmentId && c.DispatchProtocolId == dispatchProtocolId).OrderBy(c => c.SortOrder).ThenBy(c => c.ProtocolFlashcardId)));

		public Task<ProtocolFlashcard> GetByIdForDepartmentAsync(int departmentId, int protocolFlashcardId) =>
			Task.FromResult(One(c => c.DepartmentId == departmentId && c.ProtocolFlashcardId == protocolFlashcardId));

		public Task<int> FlagOtherVersionsAsync(int departmentId, int dispatchProtocolId, int versionInForceId, int reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			Writes++;
			var flagged = 0;
			foreach (var card in Rows.Where(c => c.DepartmentId == departmentId && c.DispatchProtocolId == dispatchProtocolId && c.ProtocolVersionId != versionInForceId &&
				c.Status == (int)ProtocolFlashcardStatuses.Active))
			{
				card.Status = (int)ProtocolFlashcardStatuses.NeedsReview;
				card.FlagReason = reason;
				card.FlaggedOn = utcNow;
				flagged++;
			}
			return Task.FromResult(flagged);
		}

		public Task<int> FlagCardsAsync(int departmentId, IEnumerable<int> protocolFlashcardIds, int reason, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			Writes++;
			var ids = new HashSet<int>(protocolFlashcardIds ?? Enumerable.Empty<int>());
			var flagged = 0;
			foreach (var card in Rows.Where(c => c.DepartmentId == departmentId && ids.Contains(c.ProtocolFlashcardId) && c.Status == (int)ProtocolFlashcardStatuses.Active))
			{
				card.Status = (int)ProtocolFlashcardStatuses.NeedsReview;
				card.FlagReason = reason;
				card.FlaggedOn = utcNow;
				flagged++;
			}
			return Task.FromResult(flagged);
		}
	}

	/// <summary>
	/// The protocol AI tools (round 7A) over mocks: Enhanced AI's knowledge entitlement, the protocol library and read rule, the AiGenerations
	/// audit store (rows kept in memory, "encrypted" with a prefix), the shared inference slot and budget, and a mocked LLM client (no live
	/// model). One published html protocol (40, version 401) with two heading sections. No LMS type is wired anywhere.
	/// </summary>
	public sealed class ProtocolAiWorld : IDisposable
	{
		public const int DepartmentId = 12;
		public const int ProtocolId = 40;
		public const int VersionId = 401;
		public const string Author = "author-1";
		public const string Member = "member-1";

		public const string ProtocolHtml = "<h2>Adult cardiac arrest</h2><p>Start compressions at 100 to 120 per minute.</p><p>Give epinephrine 1 mg IV every 3 to 5 minutes.</p>" +
			"<h2>Defibrillation</h2><p>Attach the defibrillator as soon as it arrives and follow its prompts.</p>";

		private readonly (string, string, string, string, string, string) _saved;

		public readonly Mock<IAiKnowledgeService> Knowledge = new Mock<IAiKnowledgeService>();
		public readonly Mock<IDepartmentDataProtectionService> Protection = new Mock<IDepartmentDataProtectionService>();
		public readonly Mock<IProtocolAccessService> Access = new Mock<IProtocolAccessService>();
		public readonly Mock<IProtocolLibraryService> Library = new Mock<IProtocolLibraryService>();
		public readonly Mock<IAuthorizationService> Authorization = new Mock<IAuthorizationService>();
		public readonly Mock<ITrainingService> Trainings = new Mock<ITrainingService>();
		public readonly Mock<IProtocolTranslationService> Translations = new Mock<IProtocolTranslationService>();
		public readonly Mock<IAdminAssistConversationProtection> ConversationProtection = new Mock<IAdminAssistConversationProtection>();
		public readonly Mock<IAiBackgroundAdmission> Admission = new Mock<IAiBackgroundAdmission>();
		public readonly Mock<IAiUsageMeter> Usage = new Mock<IAiUsageMeter>();
		public readonly Mock<ILlmClient> Llm = new Mock<ILlmClient>();
		public readonly Mock<IKnowledgeGapsRepository> Generations = new Mock<IKnowledgeGapsRepository>();
		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
		public readonly FakeProtocolFlashcards Cards = new FakeProtocolFlashcards();
		public readonly List<AiGenerationRow> Rows = new List<AiGenerationRow>();
		public readonly List<LlmRequest> Requests = new List<LlmRequest>();
		public readonly List<Training> SavedTrainings = new List<Training>();

		public DispatchProtocol Protocol;
		public readonly List<ProtocolVersion> Versions = new List<ProtocolVersion>();
		public bool MemberMayRead = true;
		public bool CanCreateTrainings = true;

		public readonly ProtocolFlashcardService Flashcards;
		public readonly ProtocolTrainingDraftService TrainingDrafts;
		public readonly ProtocolTranslationDraftService TranslationDrafts;

		public ProtocolAiWorld()
		{
			_saved = (AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds);
			AiConfig.Endpoint = "https://ai.example.test/v1/chat/completions";
			AiConfig.ApiKey = "test-key";
			AiConfig.Model = "Qwen/Qwen3-8B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.SelfHostedDepartmentIds = "";

			Knowledge.Setup(x => x.IsEntitledAsync(DepartmentId)).ReturnsAsync(true);
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

			Access.Setup(x => x.GetReaderAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((int d, string user) => new ProtocolReader
			{
				DepartmentId = DepartmentId, UserId = user, IsCurrentMember = true, CanAuthor = string.Equals(user, Author, StringComparison.OrdinalIgnoreCase)
			});
			Access.Setup(x => x.GetReadableAsync(DepartmentId, It.IsAny<string>(), ProtocolId))
				.ReturnsAsync((int d, string user, int id) => string.Equals(user, Author, StringComparison.OrdinalIgnoreCase) || MemberMayRead ? Protocol : null);
			Library.Setup(x => x.GetVersionAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync((int d, int id) => Versions.FirstOrDefault(v => v.ProtocolVersionId == id));
			Library.Setup(x => x.GetVersionsAsync(DepartmentId, ProtocolId, It.IsAny<bool>())).ReturnsAsync(() => Versions.ToList());

			Authorization.Setup(x => x.GetTrainingManagementScopeAsync(It.IsAny<string>(), DepartmentId))
				.ReturnsAsync((string user, int d) => new TrainingManagementScope { UserId = user, DepartmentId = DepartmentId, ManagesOwn = CanCreateTrainings });
			Trainings.Setup(x => x.SaveAsync(It.IsAny<Training>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Training t, CancellationToken _) => { t.TrainingId = 900 + SavedTrainings.Count; SavedTrainings.Add(t); return t; });

			// The audit store: rows kept in memory, "encrypted" with a prefix, read back as the stored content (only by the user who drafted).
			Generations.Setup(x => x.InsertGenerationAsync(It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.Callback((AiGenerationRow r, CancellationToken _) => Rows.Add(r)).Returns(Task.CompletedTask);
			Generations.Setup(x => x.GetGenerationAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string user, string id, CancellationToken _) => Rows.FirstOrDefault(r => r.Id == id && string.Equals(r.UserId, user, StringComparison.OrdinalIgnoreCase)));
			ConversationProtection.Setup(x => x.ProtectAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.Callback((AdminAssistActor _, AiGenerationRow row, CancellationToken __) => row.Content = "enc:" + row.Content).Returns(Task.CompletedTask);
			ConversationProtection.Setup(x => x.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((AdminAssistActor _, AiGenerationRow row, CancellationToken __) => JsonSerializer.Deserialize<AskStoredContent>(row.Content.Substring("enc:".Length)));

			Admission.Setup(x => x.ReserveBackgroundAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
				It.IsAny<CancellationToken>(), It.IsAny<int?>()))
				.ReturnsAsync((int d, string f, string t, DateTime n, int tokens, int limit, CancellationToken ct, int? p) => new AiUsageReservation("r1", DepartmentId, "background", tokens, DateTime.UtcNow.AddMinutes(2)));
			Usage.Setup(x => x.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);

			var client = new Lazy<ILlmClient>(() => Llm.Object);
			Flashcards = new ProtocolFlashcardService(Knowledge.Object, Protection.Object, Access.Object, Library.Object, Mock.Of<IKnowledgePageTextSource>(), Cards,
				ConversationProtection.Object, Admission.Object, Usage.Object, client, Generations.Object, Events.Object, TimeProvider.System);
			TrainingDrafts = new ProtocolTrainingDraftService(Knowledge.Object, Protection.Object, Access.Object, Library.Object, Mock.Of<IKnowledgePageTextSource>(),
				Authorization.Object, Trainings.Object, ConversationProtection.Object, Admission.Object, Usage.Object, client, Generations.Object, Events.Object, TimeProvider.System);
			TranslationDrafts = new ProtocolTranslationDraftService(Knowledge.Object, Protection.Object, Access.Object, Library.Object, Translations.Object,
				ConversationProtection.Object, Admission.Object, Usage.Object, client, Generations.Object, TimeProvider.System);
		}

		/// <summary>The next model call returns <paramref name="arguments"/> as a call of <paramref name="tool"/>.</summary>
		public void ModelReturns(string tool, object arguments) =>
			Llm.Setup(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
				.Callback((LlmRequest r, CancellationToken _) => Requests.Add(r))
				.ReturnsAsync(new LlmResult(null, new[] { new LlmToolCall("t1", tool, JsonSerializer.Serialize(arguments)) }, 900, 300, "tool_calls"));

		public void NoModelCall() => Llm.Verify(x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		/// <summary>The S-number the drafter gives the section of the version in force holding <paramref name="text"/>.</summary>
		public async Task<string> RefOfAsync(string text)
		{
			var version = Versions.First(v => v.ProtocolVersionId == Protocol.CurrentVersionId);
			var source = await ProtocolAiSources.FromVersionAsync(null, DepartmentId, Protocol, version, CancellationToken.None);
			var index = source.Sections.FindIndex(s => s.Text.Contains(text));
			if (index < 0) throw new InvalidOperationException("No section holds " + text);
			return "S" + (index + 1);
		}

		/// <summary>Publishes a new version (in force) of the protocol with <paramref name="html"/>, as the library would.</summary>
		public ProtocolVersion PublishNewVersion(int versionId, string html)
		{
			foreach (var old in Versions.Where(v => v.Status == (int)ProtocolVersionStatuses.Published))
				old.Status = (int)ProtocolVersionStatuses.Superseded;
			var version = new ProtocolVersion
			{
				ProtocolVersionId = versionId, DepartmentId = DepartmentId, DispatchProtocolId = ProtocolId, Major = 2, Minor = 0, Status = (int)ProtocolVersionStatuses.Published,
				ContentHtml = html
			};
			Versions.Add(version);
			Protocol.CurrentVersionId = versionId;
			return version;
		}

		public void Dispose()
		{
			(AiConfig.Endpoint, AiConfig.ApiKey, AiConfig.Model, AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.SelfHostedDepartmentIds) = _saved;
		}
	}
}
