using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The page-text store default: no pages. File text extraction (PDF/DOCX, per version and attachment) is built by the protocols wave-2
	/// work; registering its <see cref="IKnowledgePageTextSource"/> after this one makes protocol, training and document files part of the
	/// corpus without any change here.
	/// </summary>
	public sealed class NullKnowledgePageTextSource : IKnowledgePageTextSource
	{
		public Task<IReadOnlyList<KnowledgePageText>> GetPagesAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, string versionId,
			CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KnowledgePageText>>(Array.Empty<KnowledgePageText>());
	}

	/// <summary>Department setting 139, read without going through a service (corpus sources must not depend on the answering service).</summary>
	public static class KnowledgeSettingsReader
	{
		public static async Task<KnowledgeSettings> ReadAsync(IDepartmentSettingsRepository settings, int departmentId)
		{
			if (departmentId <= 0 || settings == null)
				return new KnowledgeSettings();
			try
			{
				var row = await settings.GetDepartmentSettingByIdTypeAsync(departmentId, DepartmentSettingTypes.KnowledgeSettings);
				if (!string.IsNullOrWhiteSpace(row?.Setting))
					return (ObjectSerialization.Deserialize<KnowledgeSettings>(row.Setting) ?? new KnowledgeSettings()).Normalize();
			}
			catch (Exception ex)
			{
				// A corrupt blob means the defaults: Documents not indexed, no dosing link.
				Logging.LogException(ex, $"Knowledge settings for department {departmentId} could not be read.");
			}
			return new KnowledgeSettings();
		}
	}

	internal static class KnowledgeChunkFactory
	{
		public static List<KnowledgeChunk> FromSections(IEnumerable<KnowledgeChunker.Section> sections, Func<KnowledgeChunk> template)
		{
			var chunks = new List<KnowledgeChunk>();
			foreach (var section in sections ?? Enumerable.Empty<KnowledgeChunker.Section>())
			{
				var chunk = template();
				chunk.Anchor = section.Anchor;
				chunk.HeadingPath = section.HeadingPath;
				chunk.Text = section.Text;
				chunks.Add(chunk);
			}
			return chunks;
		}

		public static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
	}

	/// <summary>
	/// Protocols (§16.1): the published version in force of each protocol, by heading section, and its files by page. Excluded: drafts,
	/// in-review and approved-but-unpublished versions, retired and deleted protocols, versions not yet or no longer in effect, and protocols
	/// marked "no AI" or licensed (decision 14). Readers: the library's own rule (<see cref="IProtocolAccessService"/>). Approved translations of
	/// the version in force (P3) are indexed as locale variants of its chunks (round 5F): same item, version, anchors and audience, their own
	/// locale, flagged <see cref="KnowledgeAccessFlags.LocaleVariant"/>.
	/// </summary>
	public sealed class ProtocolKnowledgeSource : IKnowledgeCorpusSource
	{
		private static readonly KnowledgeSourceKinds[] SourceKinds = { KnowledgeSourceKinds.Protocol, KnowledgeSourceKinds.ProtocolFile };

		private readonly IKnowledgeIndexRepository _index;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IProtocolAudiencesRepository _audiences;
		private readonly IProtocolAccessService _access;
		private readonly IKnowledgePageTextSource _pages;
		private readonly IProtocolVersionTranslationsRepository _translations;
		private readonly Dictionary<int, List<DispatchProtocol>> _headers = new Dictionary<int, List<DispatchProtocol>>();

		public ProtocolKnowledgeSource(IKnowledgeIndexRepository index, IProtocolVersionsRepository versions, IProtocolAudiencesRepository audiences,
			IProtocolAccessService access, IKnowledgePageTextSource pages, IProtocolVersionTranslationsRepository translations)
		{
			_translations = translations;
			_index = index;
			_versions = versions;
			_audiences = audiences;
			_access = access;
			_pages = pages;
		}

		public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => SourceKinds;

		/// <summary>The department's protocol headers, read once per scope (one request, or one worker run that builds every item).</summary>
		private async Task<List<DispatchProtocol>> HeadersAsync(int departmentId, CancellationToken cancellationToken)
		{
			if (!_headers.TryGetValue(departmentId, out var headers))
			{
				headers = await _index.GetProtocolHeadersAsync(departmentId, cancellationToken) ?? new List<DispatchProtocol>();
				_headers[departmentId] = headers;
			}
			return headers;
		}

		/// <summary>
		/// The protocol may be indexed at all: published, not deleted, and allowed in AI by the licensing guard (P4 <see cref="ProtocolLicensing"/>:
		/// not excluded from AI, not attested licensed content).
		/// </summary>
		public static bool IsIndexable(DispatchProtocol protocol) =>
			protocol != null && protocol.Status == (int)ProtocolStatuses.Published && protocol.CurrentVersionId.HasValue && ProtocolLicensing.CanUseForAi(protocol);

		/// <summary>The version is the one in force right now.</summary>
		public static bool IsInForce(ProtocolVersion version, DateTime utcNow) =>
			version != null && version.Status == (int)ProtocolVersionStatuses.Published &&
			(!version.EffectiveOn.HasValue || version.EffectiveOn.Value <= utcNow) && (!version.ExpiresOn.HasValue || version.ExpiresOn.Value > utcNow);

		public async Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || !SourceKinds.Contains(kind))
				return Array.Empty<string>();
			return (await HeadersAsync(departmentId, cancellationToken))
				.Where(p => p.DepartmentId == departmentId && IsIndexable(p))
				.Select(p => p.DispatchProtocolId.ToString(CultureInfo.InvariantCulture)).ToList();
		}

		public async Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || !SourceKinds.Contains(kind) || !int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var protocolId))
				return null;

			var protocol = (await HeadersAsync(departmentId, cancellationToken))
				.FirstOrDefault(p => p.DispatchProtocolId == protocolId && p.DepartmentId == departmentId);
			if (!IsIndexable(protocol))
				return null;

			var version = (await _versions.GetByIdsAsync(departmentId, new[] { protocol.CurrentVersionId.Value }, true) ?? new List<ProtocolVersion>())
				.FirstOrDefault(v => v.ProtocolVersionId == protocol.CurrentVersionId.Value && v.DepartmentId == departmentId && v.DispatchProtocolId == protocolId);
			if (!IsInForce(version, DateTime.UtcNow))
				return null;

			var audience = (await _audiences.GetForProtocolAsync(departmentId, protocolId) ?? new List<ProtocolAudience>())
				.Where(a => a.DepartmentId == departmentId && a.DispatchProtocolId == protocolId).Cast<IContentAudienceRule>().ToList();

			var content = new KnowledgeItemContent { DepartmentId = departmentId, Kind = kind, ItemId = itemId };
			if (kind == KnowledgeSourceKinds.Protocol)
			{
				content.Chunks = BuildVersionChunks(departmentId, protocol, version, audience);
				content.Chunks.AddRange(await TranslationChunksAsync(departmentId, protocol, version, audience));
			}
			else
			{
				var pages = await _pages.GetPagesAsync(departmentId, KnowledgeSourceKinds.ProtocolFile, itemId, version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), cancellationToken);
				content.Chunks = KnowledgeChunkFactory.FromSections(KnowledgeChunker.SplitPages(pages, KnowledgeConfig.MaxChunkChars),
					() => Template(departmentId, KnowledgeSourceKinds.ProtocolFile, protocol, version, audience));
			}

			return content;
		}

		/// <summary>
		/// The locale variants of a version (round 5F): each approved translation made from the version's current content whose stored text
		/// still hashes to its approved hash, chunked like the source (block anchors line up), with the source's audience and access flags.
		/// </summary>
		private async Task<List<KnowledgeChunk>> TranslationChunksAsync(int departmentId, DispatchProtocol protocol, ProtocolVersion version, IReadOnlyCollection<IContentAudienceRule> audience)
		{
			var chunks = new List<KnowledgeChunk>();
			if (_translations == null || version == null || version.IsDocument)
				return chunks;

			var approved = (await _translations.GetForVersionAsync(departmentId, version.ProtocolVersionId, true) ?? new List<ProtocolVersionTranslation>())
				.Where(t => t != null && t.DepartmentId == departmentId && t.ProtocolVersionId == version.ProtocolVersionId && t.IsApproved &&
					string.Equals(t.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.ContentHtml) &&
					string.Equals(ProtocolContentHash.Compute(t.ContentHtml), t.ContentHash, StringComparison.Ordinal) &&
					ProtocolTranslationRules.NormalizeLocale(t.Locale) != null && !ProtocolTranslationRules.SameLanguage(t.Locale, version.Locale))
				.GroupBy(t => t.Locale, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(t => t.ApprovedOn).First())
				.OrderBy(t => t.Locale, StringComparer.OrdinalIgnoreCase);

			foreach (var translation in approved)
			{
				var variant = new ProtocolVersion
				{
					ProtocolVersionId = version.ProtocolVersionId,
					DepartmentId = version.DepartmentId,
					DispatchProtocolId = version.DispatchProtocolId,
					Major = version.Major,
					Minor = version.Minor,
					Status = version.Status,
					EffectiveOn = version.EffectiveOn,
					ExpiresOn = version.ExpiresOn,
					ContentJson = translation.ContentJson,
					ContentHtml = translation.ContentHtml,
					Locale = translation.Locale
				};
				foreach (var chunk in BuildVersionChunks(departmentId, protocol, variant, audience))
				{
					chunk.VersionLabel = KnowledgeChunkFactory.Truncate(version.Label, 32);
					chunk.Locale = KnowledgeChunkFactory.Truncate(translation.Locale, 16);
					chunk.AccessFlags |= (int)KnowledgeAccessFlags.LocaleVariant;
					chunks.Add(chunk);
				}
			}
			return chunks;
		}

		/// <summary>The chunks of one version's content (also used, unstored, for "Ask about this call's protocol" on a cited older version).</summary>
		public static List<KnowledgeChunk> BuildVersionChunks(int departmentId, DispatchProtocol protocol, ProtocolVersion version, IEnumerable<IContentAudienceRule> audience)
		{
			var document = ProtocolContentDocument.Parse(version?.ContentJson, version?.ContentHtml);
			if (document.IsBlockModel)
			{
				// P3 (K2 part, R-S1): a block version is chunked per block and heading section, citing the block anchors (b-{key}) its HTML carries.
				return KnowledgeChunkFactory.FromSections(KnowledgeChunker.SplitBlocks(document, KnowledgeConfig.MaxChunkChars),
					() => Template(departmentId, KnowledgeSourceKinds.Protocol, protocol, version, audience));
			}

			// The rendered html blocks are the text the reading view injects heading anchors into, so the anchors line up.
			var html = document.Render();
			if (string.IsNullOrWhiteSpace(html))
				html = version?.ContentHtml;
			return KnowledgeChunkFactory.FromSections(KnowledgeChunker.SplitHtml(html, KnowledgeConfig.MaxChunkChars),
				() => Template(departmentId, KnowledgeSourceKinds.Protocol, protocol, version, audience));
		}

		private static KnowledgeChunk Template(int departmentId, KnowledgeSourceKinds kind, DispatchProtocol protocol, ProtocolVersion version, IEnumerable<IContentAudienceRule> audience) =>
			new KnowledgeChunk
			{
				DepartmentId = departmentId,
				SourceKind = (int)kind,
				ItemId = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
				VersionId = version?.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
				VersionLabel = KnowledgeChunkFactory.Truncate(version?.Label, 32),
				Title = KnowledgeChunkFactory.Truncate(string.IsNullOrWhiteSpace(protocol.Code) ? protocol.Name : protocol.Code + " · " + protocol.Name, KnowledgeChunk.TitleMaxLength) ?? string.Empty,
				Locale = KnowledgeChunkFactory.Truncate(version?.Locale, 16),
				EffectiveOn = version?.EffectiveOn,
				ExpiresOn = version?.ExpiresOn,
				AudienceJson = KnowledgeAccessFilter.SerializeAudience(audience),
				AccessFlags = (int)(protocol.Kind == (int)ProtocolKinds.Dispatch ? KnowledgeAccessFlags.DispatchersRead : KnowledgeAccessFlags.None)
			};

		public async Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default)
		{
			if (reader == null || scope == null || !reader.CanViewProtocols || scope.DepartmentId <= 0)
				return;
			try
			{
				var protocolReader = await _access.GetReaderAsync(scope.DepartmentId, scope.UserId);
				if (protocolReader == null || !protocolReader.IsCurrentMember)
					return;
				scope.IsProtocolManager = protocolReader.IsManager;
				scope.IsDispatcher = protocolReader.IsDispatcher;
				scope.Facts ??= protocolReader.Facts;

				var headers = (await _index.GetProtocolHeadersAsync(scope.DepartmentId, cancellationToken) ?? new List<DispatchProtocol>())
					.Where(p => p.DepartmentId == scope.DepartmentId && !p.IsDeleted).ToList();
				var readable = (await _access.FilterReadableAsync(protocolReader, headers) ?? new List<DispatchProtocol>())
					.Select(p => p.DispatchProtocolId.ToString(CultureInfo.InvariantCulture)).ToList();
				scope.Allow(KnowledgeSourceKinds.Protocol, readable);
				scope.Allow(KnowledgeSourceKinds.ProtocolFile, readable);
			}
			catch (Exception ex)
			{
				// Unknown access is no access.
				Logging.LogException(ex, "Protocol knowledge access could not be read.");
			}
		}
	}

	/// <summary>
	/// Trainings (§16.1): a started, unarchived training's name, description, objectives and text by heading section, and its files by page.
	/// Never its quiz: questions, answer keys, attempts and learner data are not read here. Trainings marked "no AI" (M0370) are excluded.
	/// Readers: the Trainings pages' own rule (<see cref="TrainingManagementScope.CanOpen(Training)"/>: assignees, managers, open trainings).
	/// </summary>
	public sealed class TrainingKnowledgeSource : IKnowledgeCorpusSource
	{
		private static readonly KnowledgeSourceKinds[] SourceKinds = { KnowledgeSourceKinds.Training, KnowledgeSourceKinds.TrainingFile };

		private readonly ITrainingService _trainings;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentSettingsService _settings;
		private readonly IKnowledgePageTextSource _pages;

		public TrainingKnowledgeSource(ITrainingService trainings, IAuthorizationService authorization, IDepartmentSettingsService settings, IKnowledgePageTextSource pages)
		{
			_trainings = trainings;
			_authorization = authorization;
			_settings = settings;
			_pages = pages;
		}

		public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => SourceKinds;

		public static bool IsIndexable(Training training, int departmentId, DateTime utcNow) =>
			training != null && training.DepartmentId == departmentId && !training.IsArchived && training.HasStarted(utcNow) && !training.ExcludeFromAi;

		private async Task<bool> ModuleOnAsync(int departmentId)
		{
			var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId);
			return modules == null || !modules.TrainingDisabled;
		}

		public async Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || !SourceKinds.Contains(kind) || !await ModuleOnAsync(departmentId))
				return Array.Empty<string>();
			var now = DateTime.UtcNow;
			return (await _trainings.GetAllTrainingsForDepartmentAsync(departmentId) ?? new List<Training>())
				.Where(t => IsIndexable(t, departmentId, now))
				.Select(t => t.TrainingId.ToString(CultureInfo.InvariantCulture)).ToList();
		}

		public async Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || !SourceKinds.Contains(kind) || !int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var trainingId))
				return null;
			if (!await ModuleOnAsync(departmentId))
				return null;

			// The training row only: never its questions or answers.
			var training = await _trainings.GetTrainingRowByIdAsync(trainingId);
			if (!IsIndexable(training, departmentId, DateTime.UtcNow))
				return null;

			var content = new KnowledgeItemContent { DepartmentId = departmentId, Kind = kind, ItemId = itemId };
			if (kind == KnowledgeSourceKinds.Training)
			{
				var lead = string.Join(" ", new[] { StringHelpers.HtmlToPlainText(training.Description ?? string.Empty), training.Objectives ?? string.Empty }
					.Where(s => !string.IsNullOrWhiteSpace(s)));
				var sections = new List<KnowledgeChunker.Section>();
				if (!string.IsNullOrWhiteSpace(lead))
					sections.AddRange(KnowledgeChunker.Cut(lead, KnowledgeConfig.MaxChunkChars).Select(t => new KnowledgeChunker.Section { Text = t }));
				sections.AddRange(KnowledgeChunker.SplitHtml(training.TrainingText, KnowledgeConfig.MaxChunkChars));
				content.Chunks = KnowledgeChunkFactory.FromSections(sections, () => Template(departmentId, kind, training));
			}
			else
			{
				var pages = await _pages.GetPagesAsync(departmentId, KnowledgeSourceKinds.TrainingFile, itemId, VersionOf(training), cancellationToken);
				content.Chunks = KnowledgeChunkFactory.FromSections(KnowledgeChunker.SplitPages(pages, KnowledgeConfig.MaxChunkChars), () => Template(departmentId, kind, training));
			}

			return content;
		}

		/// <summary>Trainings are not versioned: their last change stands in for the version.</summary>
		public static string VersionOf(Training training) =>
			"r" + (training.UpdatedOn ?? training.CreatedOn).ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

		private static KnowledgeChunk Template(int departmentId, KnowledgeSourceKinds kind, Training training) => new KnowledgeChunk
		{
			DepartmentId = departmentId,
			SourceKind = (int)kind,
			ItemId = training.TrainingId.ToString(CultureInfo.InvariantCulture),
			VersionId = VersionOf(training),
			Title = KnowledgeChunkFactory.Truncate(training.Name, KnowledgeChunk.TitleMaxLength) ?? string.Empty,
			EffectiveOn = training.StartsOn,
			// Open to all members: everyone in the department; otherwise the live readable set (assignees and managers) decides.
			AudienceJson = training.OpenToAllMembers ? KnowledgeAccessFilter.SerializeAudience(new[] { new ContentAudienceRule(ContentAudienceKinds.Everyone) }) : null,
			AccessFlags = (int)(training.OpenToAllMembers ? KnowledgeAccessFlags.None : KnowledgeAccessFlags.AssigneesRead)
		};

		public async Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default)
		{
			if (reader == null || scope == null || !reader.CanViewTrainings || scope.DepartmentId <= 0)
				return;
			try
			{
				if (!await ModuleOnAsync(scope.DepartmentId))
					return;
				var management = await _authorization.GetTrainingManagementScopeAsync(scope.UserId, scope.DepartmentId);
				if (management == null)
					return;
				var now = DateTime.UtcNow;
				var readable = (await _trainings.GetAllTrainingsForDepartmentAsync(scope.DepartmentId) ?? new List<Training>())
					.Where(t => t.DepartmentId == scope.DepartmentId && management.CanOpen(t, now))
					.Select(t => t.TrainingId.ToString(CultureInfo.InvariantCulture)).ToList();
				scope.Allow(KnowledgeSourceKinds.Training, readable);
				scope.Allow(KnowledgeSourceKinds.TrainingFile, readable);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training knowledge access could not be read.");
			}
		}
	}

	/// <summary>
	/// The Resgrid Academy system corpus (§16.1): one chunk per lesson from <see cref="AcademyCatalog"/> (topic, lesson or walkthrough, the
	/// modules it teaches), indexed once under DepartmentId 0 and readable by every member of every department.
	/// </summary>
	public sealed class AcademyKnowledgeSource : IKnowledgeCorpusSource
	{
		public const string CorpusVersion = "academy-a0";
		private static readonly KnowledgeSourceKinds[] SourceKinds = { KnowledgeSourceKinds.Academy };
		private static readonly Regex CamelCase = new Regex("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => SourceKinds;

		public Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<string>>(departmentId == 0 && kind == KnowledgeSourceKinds.Academy ? AcademyCatalog.Lessons.Select(l => l.Id).ToList() : new List<string>());

		public Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			var lesson = departmentId == 0 && kind == KnowledgeSourceKinds.Academy ? AcademyCatalog.Find(itemId) : null;
			if (lesson == null)
				return Task.FromResult<KnowledgeItemContent>(null);

			return Task.FromResult(new KnowledgeItemContent
			{
				DepartmentId = 0,
				Kind = KnowledgeSourceKinds.Academy,
				ItemId = lesson.Id,
				Chunks = new List<KnowledgeChunk> { LessonChunk(lesson) }
			});
		}

		/// <summary>The lesson as retrievable text: its topic, what kind of lesson it is, and the modules (pages) it teaches.</summary>
		public static KnowledgeChunk LessonChunk(AcademyLesson lesson)
		{
			var modules = AcademyCatalog.ControllerLessons.Where(p => string.Equals(p.Value, lesson.Id, StringComparison.OrdinalIgnoreCase))
				.Select(p => CamelCase.Replace(p.Key, " ")).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.Ordinal).ToList();
			var text = lesson.Topic + ". Resgrid Academy " + (lesson.IsWalkthrough ? "walkthrough" : "lesson") + ", about " +
				Math.Round(lesson.Minutes).ToString(CultureInfo.InvariantCulture) + " minutes, on " + lesson.Topic.ToLowerInvariant() + " in Resgrid" +
				(modules.Count > 0 ? ": " + string.Join(", ", modules) + "." : ".");
			return new KnowledgeChunk
			{
				DepartmentId = 0,
				SourceKind = (int)KnowledgeSourceKinds.Academy,
				ItemId = lesson.Id,
				VersionId = CorpusVersion,
				Anchor = lesson.Id,
				Title = "Resgrid Academy: " + lesson.Topic,
				Text = text,
				Locale = "en"
			};
		}

		// The Academy is readable by every member; KnowledgeAccessFilter admits DepartmentId 0 Academy chunks without a readable set.
		public Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>
	/// The Documents module, only by department opt-in (setting 139, decision 14) and never while Advanced Data Protection is in any state but
	/// Disabled (document name, description and file are catalogued). Admins-only documents carry <see cref="KnowledgeAccessFlags.AdminOnly"/>
	/// and are never used for a member who is not a department administrator.
	/// </summary>
	public sealed class DocumentKnowledgeSource : IKnowledgeCorpusSource
	{
		private static readonly KnowledgeSourceKinds[] SourceKinds = { KnowledgeSourceKinds.Document };

		private readonly IKnowledgeIndexRepository _index;
		private readonly IDepartmentSettingsRepository _settingsRepository;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IKnowledgePageTextSource _pages;
		private readonly Dictionary<int, bool> _open = new Dictionary<int, bool>();
		private readonly Dictionary<int, List<KnowledgeDocumentHeader>> _documents = new Dictionary<int, List<KnowledgeDocumentHeader>>();

		public DocumentKnowledgeSource(IKnowledgeIndexRepository index, IDepartmentSettingsRepository settingsRepository, IDepartmentSettingsService settings,
			IDepartmentDataProtectionService protection, IKnowledgePageTextSource pages)
		{
			_index = index;
			_settingsRepository = settingsRepository;
			_settings = settings;
			_protection = protection;
			_pages = pages;
		}

		public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => SourceKinds;

		/// <summary>The department has opted in, the module is on and Advanced Data Protection is Disabled (an unreadable state is "no").</summary>
		public async Task<bool> IsOpenAsync(int departmentId)
		{
			if (!_open.TryGetValue(departmentId, out var open))
			{
				open = await ReadOpenAsync(departmentId);
				_open[departmentId] = open;
			}
			return open;
		}

		private async Task<List<KnowledgeDocumentHeader>> DocumentsAsync(int departmentId, CancellationToken cancellationToken)
		{
			if (!_documents.TryGetValue(departmentId, out var documents))
			{
				documents = await _index.GetDocumentHeadersAsync(departmentId, cancellationToken) ?? new List<KnowledgeDocumentHeader>();
				_documents[departmentId] = documents;
			}
			return documents;
		}

		private async Task<bool> ReadOpenAsync(int departmentId)
		{
			if (departmentId <= 0 || !(await KnowledgeSettingsReader.ReadAsync(_settingsRepository, departmentId)).IncludeDocuments)
				return false;
			try
			{
				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId);
				if (modules != null && modules.DocumentsDisabled)
					return false;
				return await _protection.GetStateAsync(departmentId, true) == DepartmentDataProtectionState.Disabled;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Document knowledge state could not be read for department {departmentId}.");
				return false;
			}
		}

		public static bool IsIndexable(KnowledgeDocumentHeader document, int departmentId, DateTime utcNow) =>
			document != null && document.DepartmentId == departmentId && !document.IsProtected && (!document.RemoveOn.HasValue || document.RemoveOn.Value > utcNow) &&
			!ProtectedDataEnvelope.HasEnvelopePrefix(document.Name) && !ProtectedDataEnvelope.HasEnvelopePrefix(document.Description);

		public async Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default)
		{
			if (kind != KnowledgeSourceKinds.Document || !await IsOpenAsync(departmentId))
				return Array.Empty<string>();
			var now = DateTime.UtcNow;
			return (await DocumentsAsync(departmentId, cancellationToken))
				.Where(d => IsIndexable(d, departmentId, now)).Select(d => d.DocumentId.ToString(CultureInfo.InvariantCulture)).ToList();
		}

		public async Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			if (kind != KnowledgeSourceKinds.Document || !int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var documentId) || !await IsOpenAsync(departmentId))
				return null;

			var document = (await DocumentsAsync(departmentId, cancellationToken)).FirstOrDefault(d => d.DocumentId == documentId);
			if (!IsIndexable(document, departmentId, DateTime.UtcNow))
				return null;

			var version = "d" + document.AddedOn.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
			KnowledgeChunk Template() => new KnowledgeChunk
			{
				DepartmentId = departmentId,
				SourceKind = (int)KnowledgeSourceKinds.Document,
				ItemId = itemId,
				VersionId = version,
				Title = KnowledgeChunkFactory.Truncate(document.Name, KnowledgeChunk.TitleMaxLength) ?? string.Empty,
				EffectiveOn = document.AddedOn,
				ExpiresOn = document.RemoveOn,
				AccessFlags = (int)(document.AdminsOnly ? KnowledgeAccessFlags.AdminOnly : KnowledgeAccessFlags.None)
			};

			var sections = new List<KnowledgeChunker.Section>();
			var lead = string.Join(" ", new[] { document.Category, StringHelpers.HtmlToPlainText(document.Description ?? string.Empty) }.Where(s => !string.IsNullOrWhiteSpace(s)));
			if (!string.IsNullOrWhiteSpace(lead))
				sections.AddRange(KnowledgeChunker.Cut(lead, KnowledgeConfig.MaxChunkChars).Select(t => new KnowledgeChunker.Section { Text = t }));
			sections.AddRange(KnowledgeChunker.SplitPages(await _pages.GetPagesAsync(departmentId, KnowledgeSourceKinds.Document, itemId, version, cancellationToken), KnowledgeConfig.MaxChunkChars));

			return new KnowledgeItemContent { DepartmentId = departmentId, Kind = kind, ItemId = itemId, Chunks = KnowledgeChunkFactory.FromSections(sections, Template) };
		}

		public async Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default)
		{
			if (reader == null || scope == null || !reader.CanViewDocuments || !await IsOpenAsync(scope.DepartmentId))
				return;
			try
			{
				var now = DateTime.UtcNow;
				scope.Allow(KnowledgeSourceKinds.Document, (await DocumentsAsync(scope.DepartmentId, cancellationToken))
					.Where(d => IsIndexable(d, scope.DepartmentId, now) && (!d.AdminsOnly || scope.IsDepartmentAdmin))
					.Select(d => d.DocumentId.ToString(CultureInfo.InvariantCulture)));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Document knowledge access could not be read.");
			}
		}
	}
}
