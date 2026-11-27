using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The protocol library and lifecycle (P1), the shared content core (§12.1). The identity (DispatchProtocols) keeps the
	/// triggers, questions and the text in force; content lives in versions. Only a draft's content changes; a version leaves
	/// draft by submit, is approved step by step with L2 signatures bound to its content hash, and is published now or on its
	/// effective date (worker 84), when DispatchProtocols.ProtocolText becomes its text, so every older reader (v4, the older apps,
	/// the P0 call snapshots) sees exactly the published content.
	/// </summary>
	public partial class ProtocolLibraryService : IProtocolLibraryService
	{
		public const int MaxRecent = 8;
		public const int WorkerBatch = 200;

		private readonly IProtocolsService _protocolsService;
		private readonly IDispatchProtocolRepository _protocols;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IProtocolVersionApprovalsRepository _approvals;
		private readonly IProtocolAudiencesRepository _audiences;
		private readonly IProtocolCommentsRepository _comments;
		private readonly IProtocolCategoriesRepository _categories;
		private readonly IProtocolLibraryRepository _library;
		private readonly ISignatureService _signatures;
		private readonly IDepartmentSettingsRepository _settingsRepository;
		private readonly IDepartmentSettingsService _settingsService;
		private readonly IMemberPinnedItemsRepository _pins;
		private readonly IDepartmentsService _departmentsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly Lazy<ICallsService> _callsService;
		private readonly Lazy<IShiftsService> _shiftsService;
		private readonly Lazy<ICommunicationService> _communicationService;
		private readonly Lazy<IDocumentNumberingService> _numbering;
		private readonly Lazy<Model.Services.ISearchProjectionService> _searchProjections;
		private readonly IKnowledgeIndexQueue _knowledgeQueue;
		private readonly IProtocolVersionFilesRepository _files;
		private readonly IProtocolFileTextsRepository _fileTexts;
		private readonly Lazy<IProtocolAcknowledgmentService> _acknowledgments;
		private readonly Lazy<IProtocolNotificationService> _notifications;
		private readonly IContentAttributionsRepository _attributions;
		private readonly IDispatchProtocolAttachmentRepository _attachmentHashes;
		private readonly IProtocolVersionTranslationsRepository _translations;
		private readonly IEnumerable<IContentVersionPublishedHook> _publishedHooks;
		private readonly IUserProfileService _userProfiles;
		private readonly IContentSetCopyLocks _setLocks;

		public ProtocolLibraryService(IProtocolsService protocolsService, IDispatchProtocolRepository protocols, IProtocolVersionsRepository versions,
			IProtocolVersionApprovalsRepository approvals, IProtocolAudiencesRepository audiences, IProtocolCommentsRepository comments,
			IProtocolCategoriesRepository categories, IProtocolLibraryRepository library, ISignatureService signatures,
			IDepartmentSettingsRepository settingsRepository, IDepartmentSettingsService settingsService, IMemberPinnedItemsRepository pins,
			IDepartmentsService departmentsService, IPersonnelRolesService personnelRolesService, IDepartmentGroupsService departmentGroupsService,
			IEventAggregator eventAggregator, IUnitOfWork unitOfWork, Lazy<ICallsService> callsService, Lazy<IShiftsService> shiftsService,
			Lazy<ICommunicationService> communicationService, Lazy<IDocumentNumberingService> numbering,
			Lazy<Model.Services.ISearchProjectionService> searchProjections, IKnowledgeIndexQueue knowledgeQueue, IProtocolVersionFilesRepository files, IProtocolFileTextsRepository fileTexts,
			Lazy<IProtocolAcknowledgmentService> acknowledgments, Lazy<IProtocolNotificationService> notifications,
			IContentAttributionsRepository attributions, IDispatchProtocolAttachmentRepository attachmentHashes, IProtocolVersionTranslationsRepository translations,
			IEnumerable<IContentVersionPublishedHook> publishedHooks, IUserProfileService userProfiles, IContentSetCopyLocks setLocks)
		{
			_userProfiles = userProfiles;
			_setLocks = setLocks;
			_protocolsService = protocolsService;
			_protocols = protocols;
			_versions = versions;
			_approvals = approvals;
			_audiences = audiences;
			_comments = comments;
			_categories = categories;
			_library = library;
			_signatures = signatures;
			_settingsRepository = settingsRepository;
			_settingsService = settingsService;
			_pins = pins;
			_departmentsService = departmentsService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_callsService = callsService;
			_shiftsService = shiftsService;
			_communicationService = communicationService;
			_numbering = numbering;
			_searchProjections = searchProjections;
			_knowledgeQueue = knowledgeQueue;
			_files = files;
			_fileTexts = fileTexts;
			_acknowledgments = acknowledgments;
			_notifications = notifications;
			_attributions = attributions;
			_attachmentHashes = attachmentHashes;
			_translations = translations;
			_publishedHooks = publishedHooks ?? Enumerable.Empty<IContentVersionPublishedHook>();
		}

		#region Reading

		public async Task<List<ProtocolCategory>> GetCategoriesAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<ProtocolCategory>();
			return (await _categories.GetForDepartmentAsync(departmentId) ?? new List<ProtocolCategory>()).Where(c => c.DepartmentId == departmentId).ToList();
		}

		public async Task<List<ProtocolVersion>> GetVersionsAsync(int departmentId, int protocolId, bool withContent = false)
		{
			if (departmentId <= 0 || protocolId <= 0)
				return new List<ProtocolVersion>();
			return (await _versions.GetForProtocolAsync(departmentId, protocolId, withContent) ?? new List<ProtocolVersion>())
				.Where(v => v.DepartmentId == departmentId && v.DispatchProtocolId == protocolId).ToList();
		}

		public async Task<ProtocolVersion> GetVersionAsync(int departmentId, int protocolVersionId)
		{
			if (departmentId <= 0 || protocolVersionId <= 0)
				return null;
			var version = await _versions.GetByIdForDepartmentAsync(departmentId, protocolVersionId);
			return version != null && version.DepartmentId == departmentId ? version : null;
		}

		public async Task<List<ProtocolVersion>> GetDepartmentVersionHeadersAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<ProtocolVersion>();
			return (await _versions.GetHeadersForDepartmentAsync(departmentId) ?? new List<ProtocolVersion>()).Where(v => v.DepartmentId == departmentId).ToList();
		}

		public async Task<ProtocolVersion> GetOpenDraftAsync(int departmentId, int protocolId)
		{
			var open = (await GetVersionsAsync(departmentId, protocolId, true))
				.Where(IsOpen)
				.OrderByDescending(v => v.ProtocolVersionId)
				.FirstOrDefault();
			return open;
		}

		private static bool IsOpen(ProtocolVersion v) =>
			v.Status == (int)ProtocolVersionStatuses.Draft || v.Status == (int)ProtocolVersionStatuses.InReview || v.Status == (int)ProtocolVersionStatuses.Approved;

		public async Task<List<ProtocolAudience>> GetAudienceAsync(int departmentId, int protocolId)
		{
			if (departmentId <= 0 || protocolId <= 0)
				return new List<ProtocolAudience>();
			return (await _audiences.GetForProtocolAsync(departmentId, protocolId) ?? new List<ProtocolAudience>()).Where(a => a.DepartmentId == departmentId).ToList();
		}

		public async Task<List<ProtocolAudience>> GetDepartmentAudiencesAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<ProtocolAudience>();
			return (await _audiences.GetForDepartmentAsync(departmentId) ?? new List<ProtocolAudience>()).Where(a => a.DepartmentId == departmentId).ToList();
		}

		public async Task<List<ProtocolVersionApproval>> GetApprovalsAsync(int departmentId, int protocolVersionId)
		{
			if (departmentId <= 0 || protocolVersionId <= 0)
				return new List<ProtocolVersionApproval>();
			return (await _approvals.GetForVersionAsync(departmentId, protocolVersionId) ?? new List<ProtocolVersionApproval>()).Where(a => a.DepartmentId == departmentId).ToList();
		}

		public async Task<List<ProtocolComment>> GetCommentsAsync(int departmentId, int protocolId)
		{
			if (departmentId <= 0 || protocolId <= 0)
				return new List<ProtocolComment>();
			return (await _comments.GetForProtocolAsync(departmentId, protocolId) ?? new List<ProtocolComment>()).Where(c => c.DepartmentId == departmentId).ToList();
		}

		public async Task<List<ProtocolReviewItem>> GetReviewQueueAsync(ProtocolReader reader)
		{
			var items = new List<ProtocolReviewItem>();
			if (reader == null || !reader.IsManager)
				return items;

			var pending = await _approvals.GetPendingForDepartmentAsync(reader.DepartmentId) ?? new List<ProtocolVersionApproval>();
			if (pending.Count == 0)
				return items;

			var versionIds = pending.Select(p => p.ProtocolVersionId).Distinct().ToList();
			var versions = (await _versions.GetByIdsAsync(reader.DepartmentId, versionIds) ?? new List<ProtocolVersion>())
				.Where(v => v.Status == (int)ProtocolVersionStatuses.InReview).ToDictionary(v => v.ProtocolVersionId);
			var allSteps = (await _approvals.GetForVersionsAsync(reader.DepartmentId, versions.Keys) ?? new List<ProtocolVersionApproval>()).ToLookup(a => a.ProtocolVersionId);
			var roleIds = await RoleIdsAsync(reader);

			foreach (var version in versions.Values.OrderBy(v => v.SubmittedOn ?? v.CreatedOn))
			{
				var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
				if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
					continue;

				var round = CurrentRound(allSteps[version.ProtocolVersionId]);
				var step = round.FirstOrDefault(a => a.Decision == (int)ProtocolApprovalDecisions.Pending);
				items.Add(new ProtocolReviewItem
				{
					Protocol = protocol,
					Version = version,
					Step = step,
					StepsTotal = round.Count,
					CanDecide = step != null && ProtocolAccessRules.CanDecide(reader, step, roleIds, round),
					OpenComments = await _comments.CountOpenReviewCommentsAsync(reader.DepartmentId, version.ProtocolVersionId)
				});
			}

			return items;
		}

		private static List<ProtocolVersionApproval> CurrentRound(IEnumerable<ProtocolVersionApproval> steps)
		{
			var list = (steps ?? Enumerable.Empty<ProtocolVersionApproval>()).ToList();
			if (list.Count == 0)
				return list;
			var round = list.Max(a => a.Round);
			return list.Where(a => a.Round == round).OrderBy(a => a.StepOrder).ThenBy(a => a.ProtocolVersionApprovalId).ToList();
		}

		public async Task<ProtocolDiffResult> CompareAsync(int departmentId, int fromVersionId, int toVersionId)
		{
			var from = await GetVersionAsync(departmentId, fromVersionId);
			var to = await GetVersionAsync(departmentId, toVersionId);
			if (to == null)
				return null;
			if (from != null && from.DispatchProtocolId != to.DispatchProtocolId)
				return null;
			return ProtocolVersionDiff.Compare(from?.ContentHtml, to.ContentHtml);
		}

		public async Task<ProtocolLibrarySettings> GetSettingsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new ProtocolLibrarySettings();

			try
			{
				var setting = await _settingsRepository.GetDepartmentSettingByIdTypeAsync(departmentId, DepartmentSettingTypes.ProtocolLibrarySettings);
				if (!string.IsNullOrWhiteSpace(setting?.Setting))
					return (ObjectSerialization.Deserialize<ProtocolLibrarySettings>(setting.Setting) ?? new ProtocolLibrarySettings()).Normalize();
			}
			catch (Exception ex)
			{
				// A corrupt blob means the floors and "keep everything", never a failed approval.
				Logging.LogException(ex, $"Protocol library settings for department {departmentId} could not be read.");
			}

			return new ProtocolLibrarySettings();
		}

		public async Task<ProtocolLibrarySettings> SaveSettingsAsync(int departmentId, ProtocolLibrarySettings settings, string userId, CancellationToken cancellationToken = default)
		{
			var before = await GetSettingsAsync(departmentId);
			var normalized = (settings ?? new ProtocolLibrarySettings()).Normalize();
			await _settingsService.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(normalized), DepartmentSettingTypes.ProtocolLibrarySettings, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ProtocolLibrarySettingsChanged, JsonConvert.SerializeObject(before), JsonConvert.SerializeObject(normalized));
			return normalized;
		}

		public async Task<List<ProtocolCallUsage>> GetRecentCallsAsync(int departmentId, int protocolId, int take = 20)
		{
			if (departmentId <= 0 || protocolId <= 0)
				return new List<ProtocolCallUsage>();
			return await _library.GetRecentCallsAsync(departmentId, protocolId, take) ?? new List<ProtocolCallUsage>();
		}

		#endregion Reading

		#region Authoring

		public async Task<ProtocolLifecycleResult> CreateAsync(ProtocolCreateRequest request, CancellationToken cancellationToken = default)
		{
			var protocol = request?.Protocol;
			if (protocol == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var now = DateTime.UtcNow;
			var settings = await GetSettingsAsync(request.DepartmentId);

			protocol.DispatchProtocolId = 0;
			protocol.DepartmentId = request.DepartmentId;
			protocol.Kind = ProtocolKindRules.IsKnown(protocol.Kind) ? protocol.Kind : (int)ProtocolKinds.Dispatch;
			protocol.Status = (int)ProtocolStatuses.Draft;
			protocol.CurrentVersionId = null;
			protocol.RowVersion = 0;
			protocol.ReviewReminderStage = 0;
			protocol.NextReviewOn = null;
			protocol.LastReviewedOn = null;
			// The text in force is the published version's; a protocol never published has none.
			protocol.ProtocolText = null;
			var requestedOwner = protocol.OwnerUserId;
			protocol.OwnerUserId = request.UserId;
			if (!string.IsNullOrWhiteSpace(requestedOwner) && !string.Equals(requestedOwner, request.UserId, StringComparison.OrdinalIgnoreCase))
			{
				// The id as the department stores it (pickers can hand back an upper-cased one), and only a current member.
				var owner = await _departmentsService.GetDepartmentMemberAsync(requestedOwner, request.DepartmentId, false);
				if (DepartmentMemberStateHelper.IsCurrentMember(owner, request.DepartmentId))
					protocol.OwnerUserId = owner.UserId;
			}
			protocol.Tags = NormalizeTags(protocol.Tags);
			protocol.ReviewIntervalDays = NormalizeInterval(protocol.ReviewIntervalDays ?? (settings.DefaultReviewIntervalDays > 0 ? settings.DefaultReviewIntervalDays : (int?)null));
			protocol.CreatedOn = protocol.CreatedOn == default ? now : protocol.CreatedOn;
			protocol.CreatedByUserId ??= request.UserId;
			protocol.UpdatedOn = now;
			protocol.UpdatedByUserId = request.UserId;

			if (protocol.CategoryId.HasValue && await _categories.GetByIdForDepartmentAsync(request.DepartmentId, protocol.CategoryId.Value) == null)
				protocol.CategoryId = null;

			if (!ProtocolKindRules.TriggersApply(protocol.Kind))
			{
				// Only Dispatch and Procedure protocols are matched to calls; their triggers and key questions mean nothing elsewhere.
				protocol.Triggers = new List<DispatchProtocolTrigger>();
				protocol.Questions = new List<DispatchProtocolQuestion>();
			}

			if (string.IsNullOrWhiteSpace(protocol.Number))
			{
				try
				{
					protocol.Number = await _numbering.Value.TakeCustomNumberAsync(request.DepartmentId, DocumentNumberKinds.Protocol, now, cancellationToken);
				}
				catch (Exception ex)
				{
					// A number pattern problem never stops a protocol being written; the author can type one.
					Logging.LogException(ex, "Protocol number could not be issued.");
				}
			}
			else
			{
				protocol.Number = Truncate(protocol.Number.Trim(), DispatchProtocol.NumberMaxLength);
			}

			var saved = await _protocolsService.SaveProtocolAsync(protocol, cancellationToken);

			// P3: a block document from the block editor, or the creation text converted to blocks; v4, import and copy keep html.
			var document = !string.IsNullOrWhiteSpace(request.ContentJson)
				? ProtocolBlocks.Normalize(ProtocolContentDocument.Parse(request.ContentJson, null))
				: request.AsBlocks ? ProtocolBlocks.ConvertFromHtml(StringHelpers.SanitizeHtmlForStorage(request.ContentHtml ?? string.Empty)) : ProtocolContentDocument.FromHtml(request.ContentHtml);
			// P4: a copied or imported graph is kept only on a Dispatch protocol, normalized.
			document.Graph = saved.Kind == (int)ProtocolKinds.Dispatch ? ProtocolDecisionGraphs.Normalize(document.Graph) : null;
			var html = RenderContent(document, settings.ContentLocaleFor(new ProtocolVersion { Locale = NormalizeLocale(request.Locale) }));
			var draft = await _versions.InsertAsync(new ProtocolVersion
			{
				DepartmentId = request.DepartmentId,
				DispatchProtocolId = saved.DispatchProtocolId,
				Status = (int)ProtocolVersionStatuses.Draft,
				ContentJson = document.ToJson(),
				ContentHtml = html,
				ContentHash = ProtocolContentHash.Compute(html),
				RequiresAcknowledgment = request.RequiresAcknowledgment ?? ProtocolKindRules.AcknowledgmentByDefault(saved.Kind),
				Locale = NormalizeLocale(request.Locale),
				CreatedOn = now,
				CreatedByUserId = request.UserId,
				UpdatedOn = now,
				UpdatedByUserId = request.UserId
			}, cancellationToken, true);

			// P5: content from the template library, the community exchange or an attributed file keeps its attribution on the version.
			if (request.Attribution != null)
				await KeepAttributionAsync(request.Attribution, draft, request.UserId, now, cancellationToken);

			var rules = ContentAudienceMatcher.Normalize(request.Audience);
			if (rules.Count > 0 && rules.All(r => r.Kind != (int)ContentAudienceKinds.Everyone))
				await _audiences.ReplaceForProtocolAsync(request.DepartmentId, saved.DispatchProtocolId, rules.Select(r => Audience(request.DepartmentId, saved.DispatchProtocolId, r)).ToList(), cancellationToken);

			Audit(request.DepartmentId, request.UserId, AuditLogTypes.ProtocolCreated, null, Snapshot(saved, draft));
			return new ProtocolLifecycleResult { Protocol = saved, Version = draft };
		}

		public async Task<ProtocolLifecycleResult> StartDraftAsync(ProtocolReader reader, int protocolId, CancellationToken cancellationToken = default)
		{
			var (protocol, error) = await AuthorableAsync(reader, protocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);
			if (await ManagedBySetAsync(reader.DepartmentId, protocolId))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ManagedBySet, Protocol = protocol };

			var open = await GetOpenDraftAsync(reader.DepartmentId, protocolId);
			if (open != null)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.DraftExists, Version = open, Protocol = protocol };

			var current = protocol.CurrentVersionId.HasValue ? await GetVersionAsync(reader.DepartmentId, protocol.CurrentVersionId.Value) : null;
			var now = DateTime.UtcNow;
			var html = current?.ContentHtml ?? string.Empty;
			var draft = await _versions.InsertAsync(new ProtocolVersion
			{
				DepartmentId = reader.DepartmentId,
				DispatchProtocolId = protocolId,
				Status = (int)ProtocolVersionStatuses.Draft,
				ContentJson = current?.ContentJson ?? ProtocolContentDocument.FromStoredHtml(html).ToJson(),
				ContentHtml = html,
				// A document-backed draft carries its file's hash (the file is copied below).
				ContentHash = current != null && current.IsDocument ? current.ContentHash : ProtocolContentHash.Compute(html),
				RequiresAcknowledgment = current?.RequiresAcknowledgment ?? ProtocolKindRules.AcknowledgmentByDefault(protocol.Kind),
				Locale = current?.Locale,
				ContentKind = current?.ContentKind ?? (int)ProtocolVersionContentKinds.Html,
				CreatedOn = now,
				CreatedByUserId = reader.UserId,
				UpdatedOn = now,
				UpdatedByUserId = reader.UserId
			}, cancellationToken, true);

			if (current != null && current.IsDocument)
			{
				// A draft of a document-backed protocol starts with the same file (and its text) until a new upload replaces it.
				var copied = await _files.CopyToVersionAsync(reader.DepartmentId, current.ProtocolVersionId, draft.ProtocolVersionId, protocolId, reader.UserId, now, cancellationToken);
				if (copied != null)
				{
					var text = await _fileTexts.GetForSourceAsync(reader.DepartmentId, (int)ProtocolFileSourceKinds.VersionFile,
						(await _files.GetMetadataForVersionAsync(reader.DepartmentId, current.ProtocolVersionId))?.ProtocolVersionFileId ?? 0);
					if (text != null)
						await _fileTexts.CopyAsync(text, copied.ProtocolVersionFileId, draft.ProtocolVersionId, now, cancellationToken);
					draft.ContentHash = copied.Sha256;
				}
			}

			// P5: "import and modify" keeps the attribution: the new draft carries the one of the version it was started from.
			if (current != null)
				await KeepAttributionAsync(await GetAttributionAsync(reader.DepartmentId, current.ProtocolVersionId), draft, reader.UserId, now, cancellationToken);

			return new ProtocolLifecycleResult { Protocol = protocol, Version = draft };
		}

		public async Task<ProtocolLifecycleResult> SaveDraftAsync(ProtocolReader reader, int protocolId, ProtocolDraftInput input, CancellationToken cancellationToken = default)
		{
			if (input == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var (protocol, error) = await AuthorableAsync(reader, protocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);
			if (await ManagedBySetAsync(reader.DepartmentId, protocolId))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ManagedBySet, Protocol = protocol };

			var draft = await GetOpenDraftAsync(reader.DepartmentId, protocolId);
			if (draft == null)
			{
				var started = await StartDraftAsync(reader, protocolId, cancellationToken);
				if (!started.Succeeded)
					return started;
				draft = started.Version;
				input.ExpectedRowVersion = draft.RowVersion;
			}

			if (!ProtocolLifecycle.IsContentEditable(draft.Status))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotDraft, Version = draft, Protocol = protocol };

			if (draft.RowVersion != input.ExpectedRowVersion)
				return Stale(draft, protocol);

			// P4: a decision graph is content of an authored Dispatch protocol only.
			var postedGraph = input.DecisionGraphJson == null ? null : ProtocolDecisionGraphs.Normalize(ProtocolDecisionGraph.Parse(input.DecisionGraphJson));
			if (postedGraph != null && (draft.IsDocument || protocol.Kind != (int)ProtocolKinds.Dispatch))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.DecisionGraphNotAllowed, Version = draft, Protocol = protocol };

			if (!draft.IsDocument)
			{
				// P3: the block editor sends its document; the html editor (and older v4 clients) send html.
				var document = !string.IsNullOrWhiteSpace(input.ContentJson)
					? ProtocolBlocks.Normalize(ProtocolContentDocument.Parse(input.ContentJson, null))
					: ProtocolContentDocument.FromHtml(ProtocolDecisionGraphs.StripRendered(input.ContentHtml));
				// P4: the graph is the posted one when the editor sent one (blank removes it), else the draft's own, carried over: editors that
				// do not edit graphs (the html editor, v4, autosave without the builder) never drop it. Drafts may hold an unfinished graph;
				// submit refuses one that does not validate.
				document.Graph = protocol.Kind != (int)ProtocolKinds.Dispatch ? null
					: input.DecisionGraphJson != null ? postedGraph
					: ProtocolContentDocument.Parse(draft.ContentJson, null).Graph;
				var settings = await GetSettingsAsync(reader.DepartmentId);
				var html = RenderContent(document, settings.ContentLocaleFor(new ProtocolVersion { Locale = NormalizeLocale(input.Locale) }));
				draft.ContentJson = document.ToJson();
				draft.ContentHtml = html;
				draft.ContentHash = ProtocolContentHash.Compute(html);
			}
			// A document-backed draft's content is its file (replaced by a new upload); the editor saves only the rest.
			draft.ChangeSummary = Truncate(input.ChangeSummary?.Trim(), ProtocolVersion.ChangeSummaryMaxLength);
			draft.RequiresAcknowledgment = input.RequiresAcknowledgment;
			draft.Locale = NormalizeLocale(input.Locale);
			draft.UpdatedOn = DateTime.UtcNow;
			draft.UpdatedByUserId = reader.UserId;

			if (!await _versions.TryUpdateAsync(draft, input.ExpectedRowVersion, cancellationToken))
				return Stale(await GetVersionAsync(reader.DepartmentId, draft.ProtocolVersionId) ?? draft, protocol);

			return new ProtocolLifecycleResult { Protocol = protocol, Version = draft };
		}

		public async Task<ProtocolLifecycleResult> ConvertDraftToBlocksAsync(ProtocolReader reader, int protocolId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			var (protocol, error) = await AuthorableAsync(reader, protocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);
			if (await ManagedBySetAsync(reader.DepartmentId, protocolId))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ManagedBySet, Protocol = protocol };

			var draft = await GetOpenDraftAsync(reader.DepartmentId, protocolId);
			if (draft == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);
			if (!ProtocolLifecycle.IsContentEditable(draft.Status) || draft.IsDocument)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotDraft, Version = draft, Protocol = protocol };
			if (draft.RowVersion != expectedRowVersion)
				return Stale(draft, protocol);

			var source = ProtocolContentDocument.Parse(draft.ContentJson, draft.ContentHtml);
			if (source.IsBlockModel)
				return new ProtocolLifecycleResult { Protocol = protocol, Version = draft };

			var before = draft.ContentHash;
			var document = source.ConvertToBlocks();
			var settings = await GetSettingsAsync(reader.DepartmentId);
			var html = RenderContent(document, settings.ContentLocaleFor(draft));
			draft.ContentJson = document.ToJson();
			draft.ContentHtml = html;
			draft.ContentHash = ProtocolContentHash.Compute(html);
			draft.UpdatedOn = DateTime.UtcNow;
			draft.UpdatedByUserId = reader.UserId;

			if (!await _versions.TryUpdateAsync(draft, expectedRowVersion, cancellationToken))
				return Stale(await GetVersionAsync(reader.DepartmentId, draft.ProtocolVersionId) ?? draft, protocol);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolContentConverted,
				JsonConvert.SerializeObject(new { ProtocolId = protocolId, VersionId = draft.ProtocolVersionId, ContentHash = before, Schema = source.Schema }),
				JsonConvert.SerializeObject(new { ProtocolId = protocolId, VersionId = draft.ProtocolVersionId, draft.ContentHash, Schema = document.Schema, Blocks = document.Blocks.Count }));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = draft };
		}

		private static ProtocolLifecycleResult Stale(ProtocolVersion stored, DispatchProtocol protocol) => new ProtocolLifecycleResult
		{
			Error = ProtocolLifecycleErrors.StaleCopy,
			Version = stored,
			Protocol = protocol,
			ConflictUserId = stored?.UpdatedByUserId,
			ConflictOn = stored?.UpdatedOn
		};

		public async Task<ProtocolLifecycleResult> UpdateLibraryFieldsAsync(ProtocolReader reader, int protocolId, ProtocolLibraryFields fields, long expectedRowVersion,
			CancellationToken cancellationToken = default)
		{
			if (fields == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var (protocol, error) = await AuthorableAsync(reader, protocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);

			if (!await _library.TryBumpRowVersionAsync(reader.DepartmentId, protocolId, expectedRowVersion, cancellationToken))
				return new ProtocolLifecycleResult
				{
					Error = ProtocolLifecycleErrors.StaleCopy,
					Protocol = protocol,
					ConflictUserId = protocol.UpdatedByUserId,
					ConflictOn = protocol.UpdatedOn
				};

			// Re-read after the bump: the row now carries this save's version and nothing written before it is lost.
			protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			var now = DateTime.UtcNow;

			if (ProtocolKindRules.IsAuthorable(fields.Kind) && !protocol.IsBulletin)
				protocol.Kind = fields.Kind;

			protocol.CategoryId = fields.CategoryId.HasValue && await _categories.GetByIdForDepartmentAsync(reader.DepartmentId, fields.CategoryId.Value) != null ? fields.CategoryId : null;
			protocol.Number = string.IsNullOrWhiteSpace(fields.Number) ? protocol.Number : Truncate(fields.Number.Trim(), DispatchProtocol.NumberMaxLength);
			protocol.Tags = NormalizeTags(fields.Tags);
			protocol.ExcludeFromAi = fields.ExcludeFromAi;
			protocol.IsLicensedContent = fields.IsLicensedContent;

			// A legal hold is a records decision: only approvers place or lift it.
			if (reader.MayApprove)
				protocol.LegalHold = fields.LegalHold;

			if (!string.IsNullOrWhiteSpace(fields.OwnerUserId) && !string.Equals(fields.OwnerUserId, protocol.OwnerUserId, StringComparison.OrdinalIgnoreCase))
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(fields.OwnerUserId, reader.DepartmentId, false);
				if (DepartmentMemberStateHelper.IsCurrentMember(member, reader.DepartmentId))
					protocol.OwnerUserId = member.UserId;
			}

			var interval = NormalizeInterval(fields.ReviewIntervalDays);
			if (interval != protocol.ReviewIntervalDays)
			{
				protocol.ReviewIntervalDays = interval;
				if (protocol.IsPublished)
				{
					protocol.NextReviewOn = ProtocolLifecycle.NextReview(protocol.LastReviewedOn ?? now, interval);
					protocol.ReviewReminderStage = 0;
				}
			}

			protocol.UpdatedOn = now;
			protocol.UpdatedByUserId = reader.UserId;
			await _protocols.SaveOrUpdateAsync(protocol, cancellationToken, true);
			await AfterLibraryChangeAsync(protocol, cancellationToken);

			return new ProtocolLifecycleResult { Protocol = protocol };
		}

		public async Task<ProtocolLifecycleResult> SetAudienceAsync(ProtocolReader reader, int protocolId, IEnumerable<IContentAudienceRule> rules, CancellationToken cancellationToken = default)
		{
			var (protocol, error) = await AuthorableAsync(reader, protocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);

			var normalized = ContentAudienceMatcher.Normalize(rules);
			// "Everyone" is the absence of rules: the whole department.
			var rows = normalized.Any(r => r.Kind == (int)ContentAudienceKinds.Everyone)
				? new List<ProtocolAudience>()
				: normalized.Select(r => Audience(reader.DepartmentId, protocolId, r)).ToList();

			await _audiences.ReplaceForProtocolAsync(reader.DepartmentId, protocolId, rows, cancellationToken);
			protocol.Audiences = rows;
			await AfterLibraryChangeAsync(protocol, cancellationToken);
			// P2 (R-P3): members who entered the audience owe the version in force; open rows of those who left are withdrawn.
			await AcknowledgmentHookAsync(async () =>
			{
				var outcome = await _acknowledgments.Value.ReevaluateProtocolAsync(reader.DepartmentId, protocolId, DateTime.UtcNow, cancellationToken);
				return outcome.Item1 + outcome.Item2;
			}, null, cancellationToken);
			return new ProtocolLifecycleResult { Protocol = protocol };
		}

		public async Task<ProtocolLifecycleResult> DiscardDraftAsync(ProtocolReader reader, int protocolVersionId, CancellationToken cancellationToken = default)
		{
			var version = await GetVersionAsync(reader?.DepartmentId ?? 0, protocolVersionId);
			if (version == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			var (protocol, error) = await AuthorableAsync(reader, version.DispatchProtocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);

			// R-P10: anything that was ever submitted (it has approval steps) or published is kept; only an unsubmitted working
			// copy goes.
			if (ProtocolLifecycle.IsRetained(version.Status) || (await GetApprovalsAsync(reader.DepartmentId, protocolVersionId)).Count > 0)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.RetentionProtected, Version = version, Protocol = protocol };

			// A discarded document draft takes its file and the file's text with it (they reference the version).
			var file = await _files.GetMetadataForVersionAsync(reader.DepartmentId, protocolVersionId);
			if (file != null)
			{
				await _fileTexts.DeleteForSourceAsync(reader.DepartmentId, (int)ProtocolFileSourceKinds.VersionFile, file.ProtocolVersionFileId, cancellationToken);
				await _files.DeleteForVersionAsync(reader.DepartmentId, protocolVersionId, cancellationToken);
			}

			await _versions.DeleteAsync(version, cancellationToken);
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
		}

		public async Task<ProtocolLifecycleResult> CopyAsync(ProtocolReader reader, int protocolId, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayAuthor)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var source = await _protocolsService.GetProtocolByIdAsync(protocolId);
			if (source == null || source.DepartmentId != reader.DepartmentId)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			var sourceVersion = source.CurrentVersionId.HasValue ? await GetVersionAsync(reader.DepartmentId, source.CurrentVersionId.Value) : null;
			sourceVersion ??= await GetOpenDraftAsync(reader.DepartmentId, protocolId);
			var content = sourceVersion?.ContentHtml;
			var audience = await GetAudienceAsync(reader.DepartmentId, protocolId);

			var copy = new DispatchProtocol
			{
				Name = Truncate(source.Name, 100),
				Code = ProtocolRules.NormalizeCode(source.Code),
				Description = source.Description,
				MinimumWeight = source.MinimumWeight,
				Kind = source.Kind,
				CategoryId = source.CategoryId,
				Tags = source.Tags,
				ReviewIntervalDays = source.ReviewIntervalDays,
				ExcludeFromAi = source.ExcludeFromAi,
				IsLicensedContent = source.IsLicensedContent,
				Triggers = (source.Triggers ?? new List<DispatchProtocolTrigger>()).Select(t => new DispatchProtocolTrigger
				{
					Type = t.Type, StartsOn = t.StartsOn, EndsOn = t.EndsOn, Priority = t.Priority, CallType = t.CallType, CallTypeId = t.CallTypeId
				}).ToList(),
				Questions = (source.Questions ?? new List<DispatchProtocolQuestion>()).Select(q => new DispatchProtocolQuestion
				{
					Question = q.Question,
					Answers = (q.Answers ?? new List<DispatchProtocolQuestionAnswer>()).Select(a => new DispatchProtocolQuestionAnswer { Answer = a.Answer, Weight = a.Weight }).ToList()
				}).ToList(),
				Attachments = new List<DispatchProtocolAttachment>()
			};

			var created = await CreateAsync(new ProtocolCreateRequest
			{
				DepartmentId = reader.DepartmentId,
				Protocol = copy,
				ContentHtml = content,
				// P3: a block version is copied as blocks (its keys, and so its anchors, carry over).
				ContentJson = sourceVersion != null && !sourceVersion.IsDocument && (ProtocolContentDocument.Parse(sourceVersion.ContentJson, null).IsBlockModel
					|| ProtocolContentDocument.Parse(sourceVersion.ContentJson, null).HasGraph) ? sourceVersion.ContentJson : null,
				UserId = reader.UserId,
				Audience = audience.Select(a => new ContentAudienceRule { Kind = a.Kind, RefId = a.RefId }).ToList(),
				// P5: a copy of attributed content stays attributed.
				Attribution = sourceVersion == null ? null : await GetAttributionAsync(reader.DepartmentId, sourceVersion.ProtocolVersionId)
			}, cancellationToken);

			if (created.Succeeded && sourceVersion != null && sourceVersion.IsDocument)
			{
				// A copy of a document-backed protocol starts from the same file (server-side copy) and its text.
				var file = await _files.CopyToVersionAsync(reader.DepartmentId, sourceVersion.ProtocolVersionId, created.Version.ProtocolVersionId,
					created.Protocol.DispatchProtocolId, reader.UserId, DateTime.UtcNow, cancellationToken);
				if (file != null)
				{
					var expected = created.Version.RowVersion;
					created.Version.ContentKind = (int)ProtocolVersionContentKinds.Document;
					created.Version.ContentHash = file.Sha256;
					await _versions.TryUpdateAsync(created.Version, expected, cancellationToken);
					var sourceFile = await _files.GetMetadataForVersionAsync(reader.DepartmentId, sourceVersion.ProtocolVersionId);
					var text = sourceFile == null ? null : await _fileTexts.GetForSourceAsync(reader.DepartmentId, (int)ProtocolFileSourceKinds.VersionFile, sourceFile.ProtocolVersionFileId);
					if (text != null)
					{
						text.DispatchProtocolId = created.Protocol.DispatchProtocolId;
						await _fileTexts.CopyAsync(text, file.ProtocolVersionFileId, created.Version.ProtocolVersionId, DateTime.UtcNow, cancellationToken);
					}
				}
			}

			return created;
		}

		#endregion Authoring

		#region Review and publish

		public async Task<ProtocolLifecycleResult> SubmitAsync(ProtocolReader reader, int protocolVersionId, string changeSummary, CancellationToken cancellationToken = default)
		{
			var version = await GetVersionAsync(reader?.DepartmentId ?? 0, protocolVersionId);
			if (version == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			var (protocol, error) = await AuthorableAsync(reader, version.DispatchProtocolId);
			if (error != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(error);
			if (await ManagedBySetAsync(reader.DepartmentId, version.DispatchProtocolId))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ManagedBySet, Version = version, Protocol = protocol };

			if (!ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.InReview))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotDraft, Version = version, Protocol = protocol };

			if (version.IsDocument)
			{
				// A document-backed version is its file: there must be one, and it must still hash to the version's ContentHash.
				var file = await _files.GetMetadataForVersionAsync(reader.DepartmentId, protocolVersionId);
				if (file == null)
					return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.FileRequired, Version = version, Protocol = protocol };
			}
			else if (string.IsNullOrWhiteSpace(StringHelpers.HtmlToPlainText(version.ContentHtml ?? string.Empty)))
			{
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.EmptyContent, Version = version, Protocol = protocol };
			}

			// P4: a decision graph goes to review only when it validates (one first step, every path to an outcome, no loops). This is the server's
			// only gate: at dispatch an invalid graph is treated as no graph, so a version published with one would silently lose its case flow.
			if (!version.IsDocument && ProtocolDecisionGraphs.Validate(ProtocolContentDocument.Parse(version.ContentJson, null).Graph).Count > 0)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.DecisionGraphInvalid, Version = version, Protocol = protocol };

			var category = protocol.CategoryId.HasValue ? await _categories.GetByIdForDepartmentAsync(reader.DepartmentId, protocol.CategoryId.Value) : null;
			var steps = category?.ApprovalSteps ?? new List<ProtocolApprovalStep>();
			if (steps.Count == 0)
				steps = new List<ProtocolApprovalStep> { new ProtocolApprovalStep { Order = 1 } };

			var existing = await GetApprovalsAsync(reader.DepartmentId, protocolVersionId);
			var round = existing.Count == 0 ? 1 : existing.Max(a => a.Round) + 1;
			var now = DateTime.UtcNow;
			var expected = version.RowVersion;

			version.Status = (int)ProtocolVersionStatuses.InReview;
			version.SubmittedOn = now;
			version.SubmittedByUserId = reader.UserId;
			if (!string.IsNullOrWhiteSpace(changeSummary))
				version.ChangeSummary = Truncate(changeSummary.Trim(), ProtocolVersion.ChangeSummaryMaxLength);

			ProtocolVersionApproval firstStep = null;
			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				{
					if (owns) _unitOfWork?.DiscardChanges();
					return Stale(await GetVersionAsync(reader.DepartmentId, protocolVersionId), protocol);
				}

				var order = 0;
				foreach (var step in steps.OrderBy(s => s.Order))
				{
					order++;
					var inserted = await _approvals.InsertAsync(new ProtocolVersionApproval
					{
						DepartmentId = reader.DepartmentId,
						ProtocolVersionId = protocolVersionId,
						Round = round,
						StepOrder = order,
						ApproverUserId = string.IsNullOrWhiteSpace(step.ApproverUserId) ? null : step.ApproverUserId,
						ApproverRoleId = step.ApproverRoleId,
						Decision = (int)ProtocolApprovalDecisions.Pending,
						CreatedOn = now
					}, cancellationToken, true);
					firstStep ??= inserted;
				}

				if (owns) _unitOfWork?.CommitChanges();
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolVersionSubmitted, null, Snapshot(protocol, version));
			await NotifyApproversAsync(protocol, version, firstStep, cancellationToken);
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
		}

		/// <summary>
		/// P1 open item closed in P2: the approvers of the step now open hear about it directly (like the review reminders). After the
		/// commit, and never fatal: the version is in the review queue whether or not a notice went out.
		/// </summary>
		private async Task NotifyApproversAsync(DispatchProtocol protocol, ProtocolVersion version, ProtocolVersionApproval step, CancellationToken cancellationToken)
		{
			if (step == null || _notifications?.Value == null)
				return;
			try
			{
				await _notifications.Value.NotifyApproversAsync(protocol.DepartmentId, protocol, version, step, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Approvers of protocol version {version?.ProtocolVersionId} could not be notified.");
			}
		}

		public async Task<ProtocolLifecycleResult> WithdrawAsync(ProtocolReader reader, int protocolVersionId, CancellationToken cancellationToken = default)
		{
			var version = await GetVersionAsync(reader?.DepartmentId ?? 0, protocolVersionId);
			if (version == null || reader == null || !reader.IsManager)
				return ProtocolLifecycleResult.Fail(version == null ? ProtocolLifecycleErrors.NotFound : ProtocolLifecycleErrors.NotAuthorized);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			var submitter = string.Equals(version.SubmittedByUserId, reader.UserId, StringComparison.OrdinalIgnoreCase);
			if (!submitter && !reader.MayApprove)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			if (!ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.Draft))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotInReview, Version = version, Protocol = protocol };

			var round = (await GetApprovalsAsync(reader.DepartmentId, protocolVersionId)).Select(a => a.Round).DefaultIfEmpty(0).Max();
			var expected = version.RowVersion;
			version.Status = (int)ProtocolVersionStatuses.Draft;
			version.ApprovedOn = null;
			version.ApprovedByUserId = null;
			version.EffectiveOn = null;

			if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				return Stale(await GetVersionAsync(reader.DepartmentId, protocolVersionId), protocol);

			await _approvals.WithdrawPendingAsync(reader.DepartmentId, protocolVersionId, round, DateTime.UtcNow, cancellationToken);
			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolVersionWithdrawn, null, Snapshot(protocol, version));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
		}

		public async Task<ProtocolLifecycleResult> ApproveAsync(ProtocolReader reader, int protocolVersionId, string comment, SignatureProof proof, string statementText = null,
			CancellationToken cancellationToken = default)
		{
			var (protocol, version, round, step, error) = await DecidableAsync(reader, protocolVersionId);
			if (error != ProtocolLifecycleErrors.None)
				return new ProtocolLifecycleResult { Error = error, Protocol = protocol, Version = version };

			if (await _comments.CountOpenReviewCommentsAsync(reader.DepartmentId, protocolVersionId) > 0)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.OpenComments, Protocol = protocol, Version = version };

			// The signature binds to the stored hash; the hash must still be the content's (a changed row or file fails here, not later).
			if (!string.Equals(await CurrentHashAsync(version), version.ContentHash, StringComparison.Ordinal))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ArtifactChanged, Protocol = protocol, Version = version };

			if (proof == null || !string.Equals(proof.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.SignatureRequired, Protocol = protocol, Version = version };

			var settings = await GetSettingsAsync(reader.DepartmentId);
			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = reader.DepartmentId,
				Use = SignatureUse.ProtocolApproval,
				SubjectId = protocolVersionId.ToString(CultureInfo.InvariantCulture),
				RevisionId = "round-" + step.Round.ToString(CultureInfo.InvariantCulture) + "/step-" + step.StepOrder.ToString(CultureInfo.InvariantCulture),
				ArtifactSha256 = version.ContentHash,
				StatementText = string.IsNullOrWhiteSpace(statementText) ? ProtocolSignatureStatements.Approval(protocol, version, step.StepOrder, round.Count) : statementText,
				RequirePasskey = settings.RequirePasskeyForApproval
			}, proof, cancellationToken);

			if (!signed.Succeeded)
				return new ProtocolLifecycleResult { Error = MapSignature(signed.Outcome), Protocol = protocol, Version = version };

			step.Decision = (int)ProtocolApprovalDecisions.Approved;
			step.DecidedByUserId = reader.UserId;
			step.DecidedOn = DateTime.UtcNow;
			step.Comment = Truncate(comment?.Trim(), 2000);
			step.SignatureRecordId = signed.Record.SignatureRecordId;

			if (!await _approvals.TryDecideAsync(step, cancellationToken))
			{
				// Another approver decided this step first: the signature stands for nothing, so it is voided, not left dangling.
				await _signatures.VoidAsync(reader.DepartmentId, signed.Record.SignatureRecordId, reader.UserId, "The approval step was decided by someone else first.", cancellationToken);
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.AlreadyDecided, Protocol = protocol, Version = version };
			}

			var lastStep = round.All(a => a.ProtocolVersionApprovalId == step.ProtocolVersionApprovalId || a.Decision == (int)ProtocolApprovalDecisions.Approved);
			if (!lastStep)
			{
				// The next open step's approvers hear that it is their turn.
				var next = round.Where(a => a.ProtocolVersionApprovalId != step.ProtocolVersionApprovalId && a.Decision == (int)ProtocolApprovalDecisions.Pending)
					.OrderBy(a => a.StepOrder).FirstOrDefault();
				await NotifyApproversAsync(protocol, version, next, cancellationToken);
			}
			else
			{
				// The last step moves the version to Approved. A concurrent write (an autosave of a field, another decision) bumps
				// the row version: re-read and try again while it is still in review, so it never stays InReview with every step approved.
				for (var attempt = 0; attempt < 3 && version.Status == (int)ProtocolVersionStatuses.InReview; attempt++)
				{
					var expected = version.RowVersion;
					version.Status = (int)ProtocolVersionStatuses.Approved;
					version.ApprovedOn = step.DecidedOn;
					version.ApprovedByUserId = reader.UserId;
					if (await _versions.TryUpdateAsync(version, expected, cancellationToken))
						break;

					var fresh = await GetVersionAsync(reader.DepartmentId, protocolVersionId);
					if (fresh == null)
						break;
					version = fresh;
				}
			}

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolVersionApproved, null, Snapshot(protocol, version));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version, Signature = signed.Record };
		}

		private static ProtocolLifecycleErrors MapSignature(SignatureOutcomes outcome)
		{
			switch (outcome)
			{
				case SignatureOutcomes.PasskeyRequired: return ProtocolLifecycleErrors.PasskeyRequired;
				case SignatureOutcomes.ArtifactMismatch: return ProtocolLifecycleErrors.ArtifactChanged;
				case SignatureOutcomes.InvalidRequest: return ProtocolLifecycleErrors.InvalidInput;
				default: return ProtocolLifecycleErrors.SignatureRequired;
			}
		}

		public async Task<ProtocolLifecycleResult> RejectAsync(ProtocolReader reader, int protocolVersionId, string comment, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(comment))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var (protocol, version, round, step, error) = await DecidableAsync(reader, protocolVersionId);
			if (error != ProtocolLifecycleErrors.None)
				return new ProtocolLifecycleResult { Error = error, Protocol = protocol, Version = version };

			var now = DateTime.UtcNow;
			step.Decision = (int)ProtocolApprovalDecisions.Rejected;
			step.DecidedByUserId = reader.UserId;
			step.DecidedOn = now;
			step.Comment = Truncate(comment.Trim(), 2000);

			if (!await _approvals.TryDecideAsync(step, cancellationToken))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.AlreadyDecided, Protocol = protocol, Version = version };

			await _approvals.WithdrawPendingAsync(reader.DepartmentId, protocolVersionId, step.Round, now, cancellationToken);

			var expected = version.RowVersion;
			version.Status = (int)ProtocolVersionStatuses.Draft;
			await _versions.TryUpdateAsync(version, expected, cancellationToken);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolVersionRejected, null, Snapshot(protocol, version));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
		}

		/// <summary>The version under review, its current round and the open step the reader may decide.</summary>
		private async Task<(DispatchProtocol, ProtocolVersion, List<ProtocolVersionApproval>, ProtocolVersionApproval, ProtocolLifecycleErrors)> DecidableAsync(ProtocolReader reader, int protocolVersionId)
		{
			if (reader == null || !reader.MayApprove)
				return (null, null, null, null, ProtocolLifecycleErrors.NotAuthorized);

			var version = await GetVersionAsync(reader.DepartmentId, protocolVersionId);
			if (version == null)
				return (null, null, null, null, ProtocolLifecycleErrors.NotFound);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return (null, null, null, null, ProtocolLifecycleErrors.NotFound);

			if (version.Status != (int)ProtocolVersionStatuses.InReview)
				return (protocol, version, null, null, ProtocolLifecycleErrors.NotInReview);

			var round = CurrentRound(await GetApprovalsAsync(reader.DepartmentId, protocolVersionId));
			var step = round.FirstOrDefault(a => a.Decision == (int)ProtocolApprovalDecisions.Pending);
			if (step == null)
				return (protocol, version, round, null, ProtocolLifecycleErrors.NotInReview);

			if (round.Any(a => a.Decision == (int)ProtocolApprovalDecisions.Approved && string.Equals(a.DecidedByUserId, reader.UserId, StringComparison.OrdinalIgnoreCase)))
				return (protocol, version, round, step, ProtocolLifecycleErrors.AlreadyDecided);

			if (!ProtocolAccessRules.CanDecide(reader, step, await RoleIdsAsync(reader), round))
				return (protocol, version, round, step, ProtocolLifecycleErrors.NotAuthorized);

			return (protocol, version, round, step, ProtocolLifecycleErrors.None);
		}

		public async Task<ProtocolLifecycleResult> PublishAsync(ProtocolReader reader, int protocolVersionId, bool major, string changeSummary, DateTime? effectiveOnUtc,
			DateTime? expiresOnUtc, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var version = await GetVersionAsync(reader.DepartmentId, protocolVersionId);
			if (version == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			if (version.Status != (int)ProtocolVersionStatuses.Approved)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotApproved, Protocol = protocol, Version = version };

			var summary = string.IsNullOrWhiteSpace(changeSummary) ? version.ChangeSummary : changeSummary.Trim();
			if (string.IsNullOrWhiteSpace(summary))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ChangeSummaryRequired, Protocol = protocol, Version = version };

			var now = DateTime.UtcNow;
			var effective = effectiveOnUtc.HasValue ? DateTime.SpecifyKind(effectiveOnUtc.Value, DateTimeKind.Utc) : now;
			if (effective < now.AddMinutes(-5))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.EffectiveDateInPast, Protocol = protocol, Version = version };
			var expires = expiresOnUtc.HasValue ? DateTime.SpecifyKind(expiresOnUtc.Value, DateTimeKind.Utc) : (DateTime?)null;
			if (expires.HasValue && expires.Value <= effective)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.InvalidInput, Protocol = protocol, Version = version };

			// Every approval of the last round must still match the content it signed.
			if (!await ApprovalsStillMatchAsync(reader.DepartmentId, version))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ArtifactChanged, Protocol = protocol, Version = version };

			var current = protocol.CurrentVersionId.HasValue ? await GetVersionAsync(reader.DepartmentId, protocol.CurrentVersionId.Value) : null;
			var (nextMajor, nextMinor) = ProtocolLifecycle.NextNumber(current?.Status == (int)ProtocolVersionStatuses.Published ? current : LastPublished(await GetVersionsAsync(reader.DepartmentId, protocol.DispatchProtocolId)), major);
			version.Major = nextMajor;
			version.Minor = nextMinor;
			version.ChangeSummary = Truncate(summary, ProtocolVersion.ChangeSummaryMaxLength);
			version.ExpiresOn = expires;
			// P2 quiz-gated acknowledgment: the protocol's linked training is frozen into the version it gates.
			if (version.RequiresAcknowledgment && !version.QuizTrainingId.HasValue && protocol.AckTrainingId.HasValue)
				version.QuizTrainingId = protocol.AckTrainingId;

			if (effective > now.AddMinutes(1))
			{
				// Scheduled: stays Approved with its effective date and number until worker 84 activates it.
				var expectedScheduled = version.RowVersion;
				version.EffectiveOn = effective;
				version.PublishedByUserId = reader.UserId;
				if (!await _versions.TryUpdateAsync(version, expectedScheduled, cancellationToken))
					return Stale(await GetVersionAsync(reader.DepartmentId, protocolVersionId), protocol);

				Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolVersionScheduled, null, Snapshot(protocol, version));
				// A version scheduled for later can be acknowledged from publish; it is due on its effective date.
				await AcknowledgmentHookAsync(() => _acknowledgments.Value.AssignVersionAsync(protocol, version, now, cancellationToken), version, cancellationToken);
				return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
			}

			var published = await ActivateAsync(protocol, version, current, reader.UserId, now, cancellationToken);
			if (!published.Succeeded)
				return published;

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolVersionPublished, null, Snapshot(published.Protocol, published.Version));
			ContentWorkflowContext.Publish(_eventAggregator, ProtocolWorkflowEvent.For(published.Protocol, published.Version, ProtocolWorkflowChanges.Published, reader.UserId, now));
			return published;
		}

		private static ProtocolVersion LastPublished(IEnumerable<ProtocolVersion> versions) =>
			(versions ?? Enumerable.Empty<ProtocolVersion>())
				.Where(v => v.Major > 0 && (v.Status == (int)ProtocolVersionStatuses.Published || v.Status == (int)ProtocolVersionStatuses.Superseded || v.Status == (int)ProtocolVersionStatuses.Retired))
				.OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).FirstOrDefault();

		/// <summary>
		/// The version's hash recomputed from what is stored now: the sanitized HTML, or the primary file's bytes for a
		/// document-backed version (null when its file is gone). Never read from the stored hash column.
		/// </summary>
		private Task<string> CurrentHashAsync(ProtocolVersion version) => ProtocolVersionHashing.CurrentHashAsync(version, _files);

		private async Task<bool> ApprovalsStillMatchAsync(int departmentId, ProtocolVersion version)
		{
			var hash = await CurrentHashAsync(version);
			if (string.IsNullOrEmpty(hash) || !string.Equals(hash, version.ContentHash, StringComparison.Ordinal))
				return false;

			var round = CurrentRound(await GetApprovalsAsync(departmentId, version.ProtocolVersionId));
			if (round.Count == 0 || round.Any(a => a.Decision != (int)ProtocolApprovalDecisions.Approved || !a.SignatureRecordId.HasValue))
				return false;

			foreach (var step in round)
			{
				var signature = await _signatures.GetByIdAsync(departmentId, step.SignatureRecordId.Value);
				var verification = _signatures.Verify(signature, hash);
				if (verification == null || !verification.IsValid || signature.Level < (int)SignatureLevels.StepUp)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Puts an approved version in force: it becomes Published, the version in force before it Superseded, and the protocol's
		/// text, status, current version and review dates follow, in one transaction. Caches and the search projection follow
		/// after the commit.
		/// </summary>
		private async Task<ProtocolLifecycleResult> ActivateAsync(DispatchProtocol protocol, ProtocolVersion version, ProtocolVersion current, string userId,
			DateTime now, CancellationToken cancellationToken)
		{
			var owns = _unitOfWork?.Transaction == null;
			_unitOfWork?.CreateOrGetConnection();
			try
			{
				var expected = version.RowVersion;
				version.Status = (int)ProtocolVersionStatuses.Published;
				version.EffectiveOn = version.EffectiveOn.HasValue && version.EffectiveOn.Value <= now ? version.EffectiveOn : now;
				version.PublishedOn = now;
				version.PublishedByUserId ??= userId;
				if (!await _versions.TryUpdateAsync(version, expected, cancellationToken))
				{
					if (owns) _unitOfWork?.DiscardChanges();
					return Stale(await GetVersionAsync(protocol.DepartmentId, version.ProtocolVersionId), protocol);
				}

				if (current != null && current.ProtocolVersionId != version.ProtocolVersionId && current.Status == (int)ProtocolVersionStatuses.Published)
				{
					var expectedCurrent = current.RowVersion;
					current.Status = (int)ProtocolVersionStatuses.Superseded;
					current.SupersededOn = now;
					await _versions.TryUpdateAsync(current, expectedCurrent, cancellationToken);
				}

				var fresh = await _protocolsService.GetProtocolHeaderByIdAsync(protocol.DispatchProtocolId) ?? protocol;
				fresh.CurrentVersionId = version.ProtocolVersionId;
				fresh.Status = (int)ProtocolStatuses.Published;
				// The text every older reader sees is exactly the published version's (and its P0 snapshot hash).
				fresh.ProtocolText = version.ContentHtml;
				fresh.LastReviewedOn = now;
				fresh.NextReviewOn = ProtocolLifecycle.NextReview(now, fresh.ReviewIntervalDays);
				fresh.ReviewReminderStage = 0;
				fresh.RowVersion += 1;
				fresh.UpdatedOn = now;
				fresh.UpdatedByUserId = userId ?? fresh.UpdatedByUserId;
				await _protocols.SaveOrUpdateAsync(fresh, cancellationToken, true);

				if (owns) _unitOfWork?.CommitChanges();
				protocol = fresh;
			}
			catch
			{
				if (owns) _unitOfWork?.DiscardChanges();
				throw;
			}

			await AfterLibraryChangeAsync(protocol, cancellationToken);
			// P2: open acknowledgments of earlier versions are superseded; the audience owes this one (a minor carries signed ones).
			await AcknowledgmentHookAsync(() => _acknowledgments.Value.OnVersionActivatedAsync(protocol, version, now, cancellationToken), version, cancellationToken);
			// T5: content drafted from an earlier version (course flashcards) is flagged for review; never fatal to the publish.
			await PublishedHooksAsync(new ContentVersionPublished
			{
				DepartmentId = protocol.DepartmentId, Kind = ContentVersionKinds.ProtocolVersion, ItemId = protocol.DispatchProtocolId, VersionId = version.ProtocolVersionId,
				VersionLabel = version.Label, PublishedOnUtc = now
			}, cancellationToken);
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
		}

		/// <summary>Acknowledgment work after a lifecycle commit: never fatal to the lifecycle step (worker 83/86 catch up).</summary>
		private async Task AcknowledgmentHookAsync(Func<Task<int>> work, ProtocolVersion version, CancellationToken cancellationToken)
		{
			if (_acknowledgments?.Value == null)
				return;
			try
			{
				await work();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Acknowledgments of protocol version {version?.ProtocolVersionId} could not be assigned.");
			}
		}

		/// <summary>The registered <see cref="IContentVersionPublishedHook"/>s after a version went into force: each one's failure is logged, never fatal.</summary>
		private async Task PublishedHooksAsync(ContentVersionPublished published, CancellationToken cancellationToken)
		{
			foreach (var hook in _publishedHooks)
			{
				try
				{
					await hook.OnVersionPublishedAsync(published, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"A publish hook failed for protocol version {published.VersionId}.");
				}
			}
		}

		public async Task<ProtocolLifecycleResult> RecordReviewAsync(ProtocolReader reader, int protocolId, SignatureProof proof, string statementText = null,
			CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.IsCurrentMember)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			// The owner keeps it current; approvers (and admins) may stamp it too.
			var owner = string.Equals(protocol.OwnerUserId, reader.UserId, StringComparison.OrdinalIgnoreCase);
			if (!owner && !reader.MayApprove)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			if (!protocol.IsPublished)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotPublished, Protocol = protocol };

			// The stamp is the reader's own signature: a proof built for anyone else is no proof (as for approvals).
			if (proof == null || !string.Equals(proof.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.SignatureRequired, Protocol = protocol };

			var version = await GetVersionAsync(reader.DepartmentId, protocol.CurrentVersionId.Value);
			if (version == null || version.Status != (int)ProtocolVersionStatuses.Published)
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotPublished, Protocol = protocol };

			if (!string.Equals(await CurrentHashAsync(version), version.ContentHash, StringComparison.Ordinal))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.ArtifactChanged, Protocol = protocol, Version = version };

			var settings = await GetSettingsAsync(reader.DepartmentId);
			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = reader.DepartmentId,
				Use = SignatureUse.ProtocolReviewStamp,
				SubjectId = version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
				RevisionId = version.Label,
				ArtifactSha256 = version.ContentHash,
				StatementText = string.IsNullOrWhiteSpace(statementText) ? ProtocolSignatureStatements.Review(protocol, version) : statementText,
				RequirePasskey = settings.RequirePasskeyForApproval
			}, proof, cancellationToken);

			if (!signed.Succeeded)
				return new ProtocolLifecycleResult { Error = MapSignature(signed.Outcome), Protocol = protocol, Version = version };

			// No new version and no re-acknowledgment: only the review dates move (R-P9).
			var now = DateTime.UtcNow;
			protocol.LastReviewedOn = now;
			protocol.NextReviewOn = ProtocolLifecycle.NextReview(now, protocol.ReviewIntervalDays);
			protocol.ReviewReminderStage = 0;
			protocol.RowVersion += 1;
			protocol.UpdatedOn = now;
			protocol.UpdatedByUserId = reader.UserId;
			await _protocols.SaveOrUpdateAsync(protocol, cancellationToken, true);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolReviewRecorded, null, Snapshot(protocol, version));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version, Signature = signed.Record };
		}

		public async Task<ProtocolLifecycleResult> RetireAsync(ProtocolReader reader, int protocolId, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			// A priority bulletin taken out of force by hand is withdrawn (the bulletin composer's Withdraw comes through here too).
			return await RetireCoreAsync(protocol, reader.UserId, DateTime.UtcNow, AuditLogTypes.ProtocolRetired, cancellationToken,
				protocol.IsBulletin ? ProtocolWorkflowChanges.BulletinWithdrawn : ProtocolWorkflowChanges.Retired);
		}

		private async Task<ProtocolLifecycleResult> RetireCoreAsync(DispatchProtocol protocol, string userId, DateTime now, AuditLogTypes audit, CancellationToken cancellationToken,
			ProtocolWorkflowChanges workflowChange)
		{
			// The workflow event fires once, for what was in force: the version's compare-and-set decides between two workers or requests.
			var wasPublished = protocol.Status == (int)ProtocolStatuses.Published;
			var versionInForce = false;
			var versionRetired = false;
			ProtocolVersion version = null;
			if (protocol.CurrentVersionId.HasValue)
			{
				version = await GetVersionAsync(protocol.DepartmentId, protocol.CurrentVersionId.Value);
				if (version != null && version.Status == (int)ProtocolVersionStatuses.Published)
				{
					versionInForce = true;
					var expected = version.RowVersion;
					version.Status = (int)ProtocolVersionStatuses.Retired;
					version.RetiredOn = now;
					versionRetired = await _versions.TryUpdateAsync(version, expected, cancellationToken);
				}
			}

			protocol.Status = (int)ProtocolStatuses.Retired;
			protocol.NextReviewOn = null;
			protocol.ReviewReminderStage = 0;
			protocol.RowVersion += 1;
			protocol.UpdatedOn = now;
			protocol.UpdatedByUserId = userId ?? protocol.UpdatedByUserId;
			await _protocols.SaveOrUpdateAsync(protocol, cancellationToken, true);
			await AfterLibraryChangeAsync(protocol, cancellationToken);
			// Nothing in force is owed any more: open acknowledgments are withdrawn, signed ones stay.
			await AcknowledgmentHookAsync(() => _acknowledgments.Value.OnProtocolRetiredAsync(protocol, now, cancellationToken), version, cancellationToken);

			Audit(protocol.DepartmentId, userId, audit, null, Snapshot(protocol, version));
			if (wasPublished && (!versionInForce || versionRetired))
				ContentWorkflowContext.Publish(_eventAggregator, ProtocolWorkflowEvent.For(protocol, version, workflowChange, userId, now));
			return new ProtocolLifecycleResult { Protocol = protocol, Version = version };
		}

		#endregion Review and publish

		#region Comments

		public async Task<ProtocolComment> AddCommentAsync(ProtocolReader reader, int protocolId, int? protocolVersionId, ProtocolCommentKinds kind, string anchor, string body,
			int? parentCommentId, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.IsCurrentMember || string.IsNullOrWhiteSpace(body) || !Enum.IsDefined(typeof(ProtocolCommentKinds), kind))
				return null;

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId)
				return null;

			// Review comments are the reviewers' and authors'; suggestions and questions come from anyone who can read it.
			if (kind == ProtocolCommentKinds.ReviewComment && !reader.IsManager)
				return null;
			if (!reader.IsManager && !ProtocolAccessRules.CanRead(reader, protocol, await GetAudienceAsync(reader.DepartmentId, protocolId)))
				return null;

			if (protocolVersionId.HasValue)
			{
				var version = await GetVersionAsync(reader.DepartmentId, protocolVersionId.Value);
				if (version == null || version.DispatchProtocolId != protocolId)
					return null;
			}

			ProtocolComment parent = null;
			if (parentCommentId.HasValue)
			{
				parent = await _comments.GetByIdForDepartmentAsync(reader.DepartmentId, parentCommentId.Value);
				if (parent == null || parent.DispatchProtocolId != protocolId)
					return null;
				// Members reply only in their own threads; managers in any.
				if (!reader.IsManager && !string.Equals(parent.AuthorUserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
					return null;
				kind = ProtocolCommentKinds.Reply;
			}
			else if (kind == ProtocolCommentKinds.Reply)
			{
				return null;
			}

			var cleanAnchor = string.IsNullOrWhiteSpace(anchor) ? null : new string(anchor.Trim().Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(ProtocolComment.AnchorMaxLength).ToArray());

			return await _comments.InsertAsync(new ProtocolComment
			{
				DepartmentId = reader.DepartmentId,
				DispatchProtocolId = protocolId,
				ProtocolVersionId = parent?.ProtocolVersionId ?? protocolVersionId,
				ParentCommentId = parent?.ProtocolCommentId,
				Kind = (int)kind,
				Anchor = parent?.Anchor ?? (string.IsNullOrEmpty(cleanAnchor) ? null : cleanAnchor),
				Body = Truncate(StringHelpers.HtmlToPlainText(body)?.Trim() ?? body.Trim(), ProtocolComment.BodyMaxLength),
				AuthorUserId = reader.UserId,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);
		}

		public async Task<ProtocolComment> ResolveCommentAsync(ProtocolReader reader, int protocolCommentId, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.IsCurrentMember)
				return null;

			var comment = await _comments.GetByIdForDepartmentAsync(reader.DepartmentId, protocolCommentId);
			if (comment == null || comment.DepartmentId != reader.DepartmentId)
				return null;

			var mine = string.Equals(comment.AuthorUserId, reader.UserId, StringComparison.OrdinalIgnoreCase);
			if (!reader.IsManager && !mine)
				return null;

			if (comment.ResolvedOn.HasValue)
				return comment;

			comment.ResolvedOn = DateTime.UtcNow;
			comment.ResolvedByUserId = reader.UserId;
			return await _comments.SaveOrUpdateAsync(comment, cancellationToken, true);
		}

		#endregion Comments

		#region Categories and member conveniences

		public async Task<ProtocolCategory> SaveCategoryAsync(int departmentId, ProtocolCategory category, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || category == null || string.IsNullOrWhiteSpace(category.Name))
				return null;

			var all = await GetCategoriesAsync(departmentId);
			ProtocolCategory target;
			if (category.ProtocolCategoryId > 0)
			{
				target = all.FirstOrDefault(c => c.ProtocolCategoryId == category.ProtocolCategoryId);
				if (target == null)
					return null;
			}
			else
			{
				target = new ProtocolCategory { DepartmentId = departmentId, CreatedOn = DateTime.UtcNow, CreatedByUserId = userId };
			}

			// A parent must be the department's and must not be the category itself or one of its descendants.
			int? parent = category.ParentCategoryId;
			if (parent.HasValue)
			{
				var cursor = all.FirstOrDefault(c => c.ProtocolCategoryId == parent.Value);
				var guard = 0;
				while (cursor != null && guard++ < 50)
				{
					if (target.ProtocolCategoryId > 0 && cursor.ProtocolCategoryId == target.ProtocolCategoryId)
					{
						parent = null;
						break;
					}
					cursor = cursor.ParentCategoryId.HasValue ? all.FirstOrDefault(c => c.ProtocolCategoryId == cursor.ParentCategoryId.Value) : null;
				}
				if (parent.HasValue && all.All(c => c.ProtocolCategoryId != parent.Value))
					parent = null;
			}

			target.ParentCategoryId = parent;
			target.Name = Truncate(category.Name.Trim(), ProtocolCategory.NameMaxLength);
			target.Kind = category.Kind.HasValue && ProtocolKindRules.IsKnown(category.Kind.Value) ? category.Kind : null;
			target.SortOrder = category.SortOrder;
			target.ApprovalSteps = (category.ApprovalSteps ?? new List<ProtocolApprovalStep>())
				.Where(s => s != null && (!string.IsNullOrWhiteSpace(s.ApproverUserId) || s.ApproverRoleId.HasValue))
				.Select((s, i) => new ProtocolApprovalStep { Order = i + 1, ApproverUserId = string.IsNullOrWhiteSpace(s.ApproverUserId) ? null : s.ApproverUserId, ApproverRoleId = string.IsNullOrWhiteSpace(s.ApproverUserId) ? s.ApproverRoleId : null })
				.Take(10)
				.ToList();

			var saved = await _categories.SaveOrUpdateAsync(target, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.ProtocolCategoryChanged, null, JsonConvert.SerializeObject(new { saved.ProtocolCategoryId, saved.Name, saved.ParentCategoryId, saved.Kind, saved.ApprovalStepsJson }));
			return saved;
		}

		public async Task<bool> DeleteCategoryAsync(int departmentId, int protocolCategoryId, string userId, CancellationToken cancellationToken = default)
		{
			var all = await GetCategoriesAsync(departmentId);
			var category = all.FirstOrDefault(c => c.ProtocolCategoryId == protocolCategoryId);
			if (category == null || all.Any(c => c.ParentCategoryId == protocolCategoryId))
				return false;

			var used = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>()).Any(p => p.CategoryId == protocolCategoryId);
			if (used)
				return false;

			await _categories.DeleteAsync(category, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ProtocolCategoryChanged, JsonConvert.SerializeObject(new { category.ProtocolCategoryId, category.Name }), null);
			return true;
		}

		public async Task SetFavoriteAsync(int departmentId, string userId, int protocolId, bool favorite, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolId <= 0)
				return;

			var refId = protocolId.ToString(CultureInfo.InvariantCulture);
			await _pins.DeleteForMemberAsync(departmentId, userId, (int)MemberPinnedItemKinds.ProtocolFavorite, refId, cancellationToken);
			if (favorite)
				await _pins.InsertAsync(new MemberPinnedItem { DepartmentId = departmentId, UserId = userId, Kind = (int)MemberPinnedItemKinds.ProtocolFavorite, RefId = refId, PinnedOn = DateTime.UtcNow }, cancellationToken, true);
		}

		public async Task RecordViewAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolId <= 0)
				return;

			try
			{
				var refId = protocolId.ToString(CultureInfo.InvariantCulture);
				await _pins.DeleteForMemberAsync(departmentId, userId, (int)MemberPinnedItemKinds.ProtocolRecent, refId, cancellationToken);
				await _pins.InsertAsync(new MemberPinnedItem { DepartmentId = departmentId, UserId = userId, Kind = (int)MemberPinnedItemKinds.ProtocolRecent, RefId = refId, PinnedOn = DateTime.UtcNow }, cancellationToken, true);

				var recent = (await _pins.GetForMemberAsync(departmentId, userId) ?? new List<MemberPinnedItem>())
					.Where(p => p.Kind == (int)MemberPinnedItemKinds.ProtocolRecent).OrderByDescending(p => p.PinnedOn).Skip(MaxRecent).ToList();
				foreach (var old in recent)
					await _pins.DeleteForMemberAsync(departmentId, userId, (int)MemberPinnedItemKinds.ProtocolRecent, old.RefId, cancellationToken);
			}
			catch (Exception ex)
			{
				// "Recent" is a convenience; reading the protocol must never fail over it.
				Logging.LogException(ex, "Recent protocol view could not be recorded.");
			}
		}

		public async Task<(List<int> Favorites, List<int> Recent)> GetFavoritesAndRecentAsync(int departmentId, string userId)
		{
			var pins = await _pins.GetForMemberAsync(departmentId, userId) ?? new List<MemberPinnedItem>();
			List<int> Ids(MemberPinnedItemKinds kind) => pins.Where(p => p.Kind == (int)kind).OrderByDescending(p => p.PinnedOn)
				.Select(p => int.TryParse(p.RefId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
			return (Ids(MemberPinnedItemKinds.ProtocolFavorite), Ids(MemberPinnedItemKinds.ProtocolRecent));
		}

		#endregion Categories and member conveniences

		#region Exchange

		public async Task<ProtocolExchangeDocument> ExportAsync(ProtocolReader reader, IEnumerable<int> protocolIds)
		{
			var document = new ProtocolExchangeDocument { ExportedOn = DateTime.UtcNow };
			if (reader == null || !reader.IsManager)
				return document;

			var wanted = new HashSet<int>(protocolIds ?? Enumerable.Empty<int>());
			var protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(reader.DepartmentId) ?? new List<DispatchProtocol>())
				.Where(p => wanted.Count == 0 || wanted.Contains(p.DispatchProtocolId)).ToList();
			var categories = (await GetCategoriesAsync(reader.DepartmentId)).ToDictionary(c => c.ProtocolCategoryId);
			var priorities = await _callsService.Value.GetCallPrioritiesForDepartmentAsync(reader.DepartmentId) ?? new List<DepartmentCallPriority>();
			var audiences = (await GetDepartmentAudiencesAsync(reader.DepartmentId)).ToLookup(a => a.DispatchProtocolId);
			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(reader.DepartmentId) ?? new List<DepartmentGroup>()).ToDictionary(g => g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), g => g.Name);
			var roles = (await _personnelRolesService.GetRolesForDepartmentAsync(reader.DepartmentId) ?? new List<PersonnelRole>()).ToDictionary(r => r.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), r => r.Name);
			var shifts = (await _shiftsService.Value.GetAllShiftsByDepartmentAsync(reader.DepartmentId) ?? new List<Shift>()).ToDictionary(s => s.ShiftId.ToString(CultureInfo.InvariantCulture), s => s.Name);
			// Version 2 (P5): history, attribution, approved translations and the attachments manifest, each read once for the department.
			var headers = (await _versions.GetHeadersForDepartmentAsync(reader.DepartmentId) ?? new List<ProtocolVersion>()).Where(v => v.DepartmentId == reader.DepartmentId).ToLookup(v => v.DispatchProtocolId);
			var attachmentHashes = (await _attachmentHashes.GetAttachmentHashesByDepartmentIdAsync(reader.DepartmentId) ?? Enumerable.Empty<DispatchProtocolAttachment>()).ToLookup(a => a.DispatchProtocolId);
			document.Schema = ProtocolExchangeDocument.SchemaId;

			foreach (var protocol in protocols)
			{
				var version = protocol.CurrentVersionId.HasValue ? await GetVersionAsync(reader.DepartmentId, protocol.CurrentVersionId.Value) : await GetOpenDraftAsync(reader.DepartmentId, protocol.DispatchProtocolId);
				var attribution = version == null ? null : await GetAttributionAsync(reader.DepartmentId, version.ProtocolVersionId);
				var translations = version == null ? new List<ProtocolVersionTranslation>() : (await _translations.GetForVersionAsync(reader.DepartmentId, version.ProtocolVersionId, true) ?? new List<ProtocolVersionTranslation>())
					.Where(t => t.DepartmentId == reader.DepartmentId && t.IsApproved && string.Equals(t.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase)).ToList();
				var history = headers[protocol.DispatchProtocolId].Where(v => v.Status == (int)ProtocolVersionStatuses.Published || v.Status == (int)ProtocolVersionStatuses.Superseded ||
					v.Status == (int)ProtocolVersionStatuses.Retired).OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor).ThenByDescending(v => v.ProtocolVersionId).ToList();
				var files = new List<ProtocolExchangeAttachment>();
				if (version != null && version.IsDocument)
				{
					var primary = await _files.GetMetadataForVersionAsync(reader.DepartmentId, version.ProtocolVersionId);
					if (primary != null)
						files.Add(new ProtocolExchangeAttachment { Kind = "document", FileName = primary.FileName, ContentType = primary.ContentType, Size = primary.Size, Sha256 = primary.Sha256 });
				}
				files.AddRange(attachmentHashes[protocol.DispatchProtocolId].Select(a => new ProtocolExchangeAttachment
				{
					Kind = "attachment", FileName = a.FileName, ContentType = a.FileType, Size = a.Size, Sha256 = a.Sha256
				}));
				document.Protocols.Add(new ProtocolExchangeItem
				{
					Attribution = attribution == null ? null : new ProtocolExchangeAttribution
					{
						Source = attribution.SourceName, Author = attribution.Author, License = attribution.License, LicenseUrl = attribution.LicenseUrl, SourceVersion = attribution.SourceVersion
					},
					Locale = version?.Locale,
					EffectiveOn = version?.EffectiveOn,
					ContentHash = version?.ContentHash,
					History = history.Count == 0 ? null : history.Select(v => new ProtocolExchangeVersion
					{
						Label = v.Label, Status = ((ProtocolVersionStatuses)v.Status).ToString(), EffectiveOn = v.EffectiveOn, SupersededOn = v.SupersededOn ?? v.RetiredOn,
						ChangeSummary = v.ChangeSummary, ContentHash = v.ContentHash
					}).ToList(),
					Translations = translations.Count == 0 ? null : translations.Select(t => new ProtocolExchangeTranslation
					{
						Locale = t.Locale, Content = ProtocolContentDocument.Parse(t.ContentJson, t.ContentHtml), ContentHash = t.ContentHash, SourceContentHash = t.SourceContentHash
					}).ToList(),
					Attachments = files.Count == 0 ? null : files,
					Code = protocol.Code,
					Name = protocol.Name,
					Kind = ((ProtocolKinds)protocol.Kind).ToString(),
					Number = protocol.Number,
					Category = protocol.CategoryId.HasValue && categories.TryGetValue(protocol.CategoryId.Value, out var category) ? category.Name : null,
					Tags = protocol.Tags,
					Description = protocol.Description,
					MinimumWeight = protocol.MinimumWeight,
					RequiresAcknowledgment = version?.RequiresAcknowledgment ?? false,
					ReviewIntervalDays = protocol.ReviewIntervalDays,
					ExcludeFromAi = protocol.ExcludeFromAi,
					IsLicensedContent = protocol.IsLicensedContent,
					VersionLabel = version?.Label,
					Content = ProtocolContentDocument.Parse(version?.ContentJson, version?.ContentHtml ?? protocol.ProtocolText),
					Triggers = (protocol.Triggers ?? new List<DispatchProtocolTrigger>()).Select(t => new ProtocolExchangeTrigger
					{
						Type = t.Type,
						Priority = t.Priority.HasValue ? priorities.FirstOrDefault(p => p.DepartmentCallPriorityId == t.Priority.Value)?.Name : null,
						CallType = t.CallType,
						StartsOn = t.StartsOn,
						EndsOn = t.EndsOn
					}).ToList(),
					Questions = (protocol.Questions ?? new List<DispatchProtocolQuestion>()).OrderBy(q => q.DispatchProtocolQuestionId).Select(q => new ProtocolExchangeQuestion
					{
						Question = q.Question,
						Answers = (q.Answers ?? new List<DispatchProtocolQuestionAnswer>()).OrderBy(a => a.DispatchProtocolQuestionAnswerId).Select(a => new ProtocolExchangeAnswer { Answer = a.Answer, Weight = a.Weight }).ToList()
					}).ToList(),
					Audience = audiences[protocol.DispatchProtocolId].Select(a => new ProtocolExchangeAudience
					{
						Kind = ((ContentAudienceKinds)a.Kind).ToString(),
						Ref = (ContentAudienceKinds)a.Kind switch
						{
							ContentAudienceKinds.Group => groups.TryGetValue(a.RefId ?? "", out var g) ? g : null,
							ContentAudienceKinds.Role => roles.TryGetValue(a.RefId ?? "", out var r) ? r : null,
							ContentAudienceKinds.Shift => shifts.TryGetValue(a.RefId ?? "", out var s) ? s : null,
							_ => null
						}
					}).Where(a => a.Ref != null).ToList()
				});
			}

			return document;
		}

		public Task<ProtocolImportResult> ImportAsync(ProtocolReader reader, ProtocolExchangeDocument document, CancellationToken cancellationToken = default) =>
			ImportCoreAsync(reader, document, false, cancellationToken);

		public Task<ProtocolImportResult> PreviewImportAsync(ProtocolReader reader, ProtocolExchangeDocument document) =>
			ImportCoreAsync(reader, document, true, CancellationToken.None);

		private async Task<ProtocolImportResult> ImportCoreAsync(ProtocolReader reader, ProtocolExchangeDocument document, bool dryRun, CancellationToken cancellationToken)
		{
			var result = new ProtocolImportResult { DryRun = dryRun };
			if (reader == null || !reader.MayAuthor)
			{
				result.Error = "not_authorized";
				return result;
			}

			if (document == null || !string.Equals(document.Format, ProtocolExchangeDocument.FormatName, StringComparison.Ordinal) || !ProtocolExchangeDocument.IsSupportedVersion(document.Version))
			{
				result.Error = "unknown_format";
				return result;
			}

			var items = (document.Protocols ?? new List<ProtocolExchangeItem>()).Where(i => i != null).ToList();
			if (items.Count > ProtocolExchangeDocument.MaxProtocolsPerImport)
			{
				result.Error = "too_many";
				return result;
			}

			var priorities = await _callsService.Value.GetCallPrioritiesForDepartmentAsync(reader.DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.Value.GetCallTypesForDepartmentAsync(reader.DepartmentId) ?? new List<CallType>();
			var categories = await GetCategoriesAsync(reader.DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(reader.DepartmentId) ?? new List<DepartmentGroup>();
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(reader.DepartmentId) ?? new List<PersonnelRole>();
			var shifts = await _shiftsService.Value.GetAllShiftsByDepartmentAsync(reader.DepartmentId) ?? new List<Shift>();
			bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.CurrentCultureIgnoreCase);

			foreach (var item in items)
			{
				var label = string.IsNullOrWhiteSpace(item.Code) ? item.Name : item.Code;
				var kind = Enum.TryParse<ProtocolKinds>(item.Kind ?? "Dispatch", true, out var parsedKind) && ProtocolKindRules.IsAuthorable((int)parsedKind) ? (int)parsedKind : (int)ProtocolKinds.Dispatch;
				var protocol = new DispatchProtocol
				{
					Name = Truncate(item.Name?.Trim(), 100),
					Code = ProtocolRules.NormalizeCode(item.Code),
					Description = item.Description,
					MinimumWeight = Math.Clamp(item.MinimumWeight, 0, 1000),
					Kind = kind,
					Number = item.Number,
					Tags = item.Tags,
					ReviewIntervalDays = item.ReviewIntervalDays,
					ExcludeFromAi = item.ExcludeFromAi,
					IsLicensedContent = item.IsLicensedContent,
					CategoryId = categories.FirstOrDefault(c => Same(c.Name, item.Category))?.ProtocolCategoryId,
					Triggers = new List<DispatchProtocolTrigger>(),
					Questions = (item.Questions ?? new List<ProtocolExchangeQuestion>()).Where(q => q != null && !string.IsNullOrWhiteSpace(q.Question)).Select(q => new DispatchProtocolQuestion
					{
						Question = q.Question.Trim(),
						Answers = (q.Answers ?? new List<ProtocolExchangeAnswer>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Answer)).Select(a => new DispatchProtocolQuestionAnswer { Answer = a.Answer.Trim(), Weight = a.Weight }).ToList()
					}).ToList(),
					Attachments = new List<DispatchProtocolAttachment>()
				};

				if (!string.IsNullOrWhiteSpace(item.Category) && !protocol.CategoryId.HasValue)
					result.Warnings.Add(label + ": category \"" + item.Category + "\" not found");

				foreach (var trigger in item.Triggers ?? new List<ProtocolExchangeTrigger>())
				{
					if (trigger == null || !Enum.IsDefined(typeof(ProtocolTriggerTypes), trigger.Type))
						continue;
					var priority = string.IsNullOrWhiteSpace(trigger.Priority) ? null : priorities.FirstOrDefault(p => !p.IsDeleted && Same(p.Name, trigger.Priority));
					var type = string.IsNullOrWhiteSpace(trigger.CallType) ? null : types.FirstOrDefault(t => Same(t.Type, trigger.CallType));
					var needsPriority = trigger.Type == (int)ProtocolTriggerTypes.CallPriorty || trigger.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;
					var needsType = trigger.Type == (int)ProtocolTriggerTypes.CallType || trigger.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;
					if ((needsPriority && priority == null) || (needsType && !string.IsNullOrWhiteSpace(trigger.CallType) && type == null))
					{
						result.Warnings.Add(label + ": a trigger names a priority or call type this department does not have");
						continue;
					}
					protocol.Triggers.Add(new DispatchProtocolTrigger
					{
						Type = trigger.Type,
						Priority = needsPriority ? priority?.DepartmentCallPriorityId : null,
						CallTypeId = needsType ? type?.CallTypeId : null,
						CallType = needsType ? type?.Type : null,
						StartsOn = trigger.StartsOn,
						EndsOn = trigger.EndsOn
					});
				}

				var errors = ProtocolRules.Validate(protocol);
				if (errors.Count > 0)
				{
					result.Skipped.Add(label + ": " + string.Join(", ", errors));
					continue;
				}

				var audience = new List<ContentAudienceRule>();
				foreach (var rule in item.Audience ?? new List<ProtocolExchangeAudience>())
				{
					if (rule == null || !Enum.TryParse<ContentAudienceKinds>(rule.Kind, true, out var audienceKind))
						continue;
					string refId = audienceKind switch
					{
						ContentAudienceKinds.Everyone => null,
						ContentAudienceKinds.Group => groups.FirstOrDefault(g => Same(g.Name, rule.Ref))?.DepartmentGroupId.ToString(CultureInfo.InvariantCulture),
						ContentAudienceKinds.Role => roles.FirstOrDefault(r => Same(r.Name, rule.Ref))?.PersonnelRoleId.ToString(CultureInfo.InvariantCulture),
						ContentAudienceKinds.Shift => shifts.FirstOrDefault(s => Same(s.Name, rule.Ref))?.ShiftId.ToString(CultureInfo.InvariantCulture),
						_ => null
					};
					if (audienceKind != ContentAudienceKinds.Everyone && refId == null)
					{
						result.Warnings.Add(label + ": audience \"" + rule.Ref + "\" not found");
						continue;
					}
					audience.Add(new ContentAudienceRule(audienceKind, refId));
				}

				if (dryRun)
				{
					// The preview stops here: everything above (format, matching, validation) ran exactly as the import will.
					result.Created++;
					result.Names.Add(label);
					continue;
				}

				var created = await CreateAsync(new ProtocolCreateRequest
				{
					DepartmentId = reader.DepartmentId,
					Protocol = protocol,
					ContentHtml = item.Content?.Render(),
					// P3: an exported block document is imported as blocks.
					ContentJson = item.Content != null && (item.Content.IsBlockModel || item.Content.HasGraph) ? item.Content.ToJson() : null,
					RequiresAcknowledgment = item.RequiresAcknowledgment,
					UserId = reader.UserId,
					Audience = audience,
					Locale = item.Locale,
					// Version 2 (P5): the file's attribution is kept on the imported version.
					Attribution = AttributionFrom(item.Attribution, document, reader.DepartmentId)
				}, cancellationToken);

				if (created.Succeeded)
				{
					result.Created++;
					result.Names.Add(label);
					result.CreatedProtocolIds.Add(created.Protocol.DispatchProtocolId);
					if (item.Translations != null && item.Translations.Count > 0)
						result.Warnings.Add(label + ": " + item.Translations.Count.ToString(CultureInfo.InvariantCulture) + " translation(s) not imported; translate the version after it is published");
				}
				else
				{
					result.Skipped.Add(label + ": " + created.Error);
				}
			}

			if (!dryRun)
				Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolsImported, null, JsonConvert.SerializeObject(new { result.Created, Skipped = result.Skipped.Count, Warnings = result.Warnings.Count }));
			return result;
		}

		public async Task<ContentAttribution> GetAttributionAsync(int departmentId, int protocolVersionId)
		{
			if (departmentId <= 0 || protocolVersionId <= 0)
				return null;
			try
			{
				return await _attributions.GetForSubjectAsync(departmentId, ContentAttributionSubjects.ProtocolVersion, protocolVersionId);
			}
			catch (Exception ex)
			{
				// Attribution is shown beside the content; a read failure never stops the content being read.
				Logging.LogException(ex, "Protocol attribution could not be read.");
				return null;
			}
		}

		/// <summary>The attribution a version 2 item carries, as the row the imported version keeps (null without a source).</summary>
		private static ContentAttribution AttributionFrom(ProtocolExchangeAttribution attribution, ProtocolExchangeDocument document, int departmentId)
		{
			if (attribution == null || string.IsNullOrWhiteSpace(attribution.Source))
				return null;
			return new ContentAttribution
			{
				DepartmentId = departmentId,
				SubjectKind = (int)ContentAttributionSubjects.ProtocolVersion,
				SourceKind = (int)(document.AttributionSource ?? ContentAttributionSources.FileImport),
				SourceRef = document.AttributionSourceRef,
				SourceName = attribution.Source,
				Author = attribution.Author,
				License = attribution.License,
				LicenseUrl = attribution.LicenseUrl,
				SourceVersion = attribution.SourceVersion
			};
		}

		/// <summary>Writes a copy of <paramref name="attribution"/> for <paramref name="version"/>; never fails the step that called it.</summary>
		private async Task KeepAttributionAsync(ContentAttribution attribution, ProtocolVersion version, string userId, DateTime now, CancellationToken cancellationToken)
		{
			if (attribution == null || version == null || version.ProtocolVersionId <= 0)
				return;
			try
			{
				var copy = attribution.CopyFor(ContentAttributionSubjects.ProtocolVersion, version.ProtocolVersionId, userId, now);
				copy.DepartmentId = version.DepartmentId;
				await _attributions.SaveForSubjectAsync(copy, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protocol attribution could not be kept on the version.");
			}
		}

		#endregion Exchange

		#region Worker 84

		public async Task<int> ActivateDueVersionsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var activated = 0;
			foreach (var due in await _versions.GetDueActivationsAsync(utcNow, WorkerBatch) ?? new List<ProtocolVersion>())
			{
				try
				{
					var version = await GetVersionAsync(due.DepartmentId, due.ProtocolVersionId);
					var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
					if (version == null || protocol == null || protocol.DepartmentId != version.DepartmentId || protocol.IsDeleted || version.Status != (int)ProtocolVersionStatuses.Approved)
						continue;

					if (!await ApprovalsStillMatchAsync(version.DepartmentId, version))
					{
						// Content no longer matches its approvals: it goes back for review rather than into force.
						var expected = version.RowVersion;
						version.Status = (int)ProtocolVersionStatuses.Draft;
						version.EffectiveOn = null;
						await _versions.TryUpdateAsync(version, expected, cancellationToken);
						continue;
					}

					var current = protocol.CurrentVersionId.HasValue ? await GetVersionAsync(protocol.DepartmentId, protocol.CurrentVersionId.Value) : null;
					// Numbers were chosen at scheduling; a version published since then moves this one past it, keeping major or minor.
					var basis = current?.Status == (int)ProtocolVersionStatuses.Published ? current : LastPublished(await GetVersionsAsync(protocol.DepartmentId, protocol.DispatchProtocolId));
					if (basis != null && (version.Major < basis.Major || (version.Major == basis.Major && version.Minor <= basis.Minor)))
						(version.Major, version.Minor) = ProtocolLifecycle.NextNumber(basis, version.Minor == 0);

					var result = await ActivateAsync(protocol, version, current, version.PublishedByUserId, utcNow, cancellationToken);
					if (result.Succeeded)
					{
						activated++;
						Audit(protocol.DepartmentId, version.PublishedByUserId, AuditLogTypes.ProtocolVersionPublished, null, Snapshot(result.Protocol, result.Version));
						ContentWorkflowContext.Publish(_eventAggregator, ProtocolWorkflowEvent.For(result.Protocol, result.Version, ProtocolWorkflowChanges.Published,
							version.PublishedByUserId, utcNow));
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Scheduled protocol version {due.ProtocolVersionId} could not be published.");
				}
			}

			return activated;
		}

		public async Task<int> ExpireDueVersionsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var expired = 0;
			foreach (var due in await _versions.GetDueExpirationsAsync(utcNow, WorkerBatch) ?? new List<ProtocolVersion>())
			{
				try
				{
					var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(due.DispatchProtocolId);
					if (protocol == null || protocol.DepartmentId != due.DepartmentId)
						continue;

					if (protocol.CurrentVersionId == due.ProtocolVersionId && protocol.Status == (int)ProtocolStatuses.Published)
					{
						await RetireCoreAsync(protocol, null, utcNow, AuditLogTypes.ProtocolRetired, cancellationToken, ProtocolWorkflowChanges.Expired);
						expired++;
					}
					else
					{
						var version = await GetVersionAsync(due.DepartmentId, due.ProtocolVersionId);
						if (version != null && version.Status == (int)ProtocolVersionStatuses.Published)
						{
							var expected = version.RowVersion;
							version.Status = (int)ProtocolVersionStatuses.Retired;
							version.RetiredOn = utcNow;
							if (await _versions.TryUpdateAsync(version, expected, cancellationToken))
							{
								expired++;
								ContentWorkflowContext.Publish(_eventAggregator, ProtocolWorkflowEvent.For(protocol, version, ProtocolWorkflowChanges.Expired, null, utcNow));
							}
						}
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Protocol version {due.ProtocolVersionId} could not be expired.");
				}
			}

			return expired;
		}

		public async Task<int> SendReviewRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			var due = await _library.GetReviewRemindersDueAsync(utcNow, WorkerBatch) ?? new List<ProtocolReviewDue>();
			foreach (var department in due.GroupBy(d => d.DepartmentId))
			{
				try
				{
					var dept = await _departmentsService.GetDepartmentByIdAsync(department.Key, false);
					if (dept == null)
						continue;

					var active = await _departmentsService.GetActiveMemberUserIdsAsync(department.Key) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					var canTransmit = ConfigHelper.CanTransmit(department.Key);
					string departmentNumber = null;
					ContentNoticeCultures cultures = null;

					foreach (var item in department)
					{
						var stage = ProtocolReviewReminders.DueStage(item.NextReviewOn, utcNow);
						if ((int)stage <= item.ReviewReminderStage)
							continue;

						// Claim the stage first: a second worker run (or node) never sends the same reminder twice.
						if (!await _library.MarkReviewReminderAsync(item.DepartmentId, item.DispatchProtocolId, item.NextReviewOn, (int)stage, cancellationToken))
							continue;

						// The claimed stage is the department's workflow event too (once per stage); an owner who left is not named.
						var ownerActive = !string.IsNullOrWhiteSpace(item.OwnerUserId) && active.Contains(item.OwnerUserId);
						ContentWorkflowContext.Publish(_eventAggregator, new ProtocolWorkflowEvent
						{
							DepartmentId = item.DepartmentId,
							Change = stage == ProtocolReviewReminderStages.Overdue ? ProtocolWorkflowChanges.ReviewOverdue : ProtocolWorkflowChanges.ReviewDue,
							ProtocolId = item.DispatchProtocolId,
							ProtocolCode = item.Code,
							ProtocolName = item.Name,
							NextReviewOn = item.NextReviewOn,
							ReviewStage = stage.ToString(),
							OwnerUserId = ownerActive ? item.OwnerUserId : null,
							OccurredOn = utcNow
						});

						// The owner, or the department's active admins when the owner left or was never set (inactive-member policy).
						var recipients = !string.IsNullOrWhiteSpace(item.OwnerUserId) && active.Contains(item.OwnerUserId)
							? new List<string> { item.OwnerUserId }
							: (await _departmentsService.GetActiveAdminsForDepartmentAsync(department.Key) ?? new List<Model.Identity.IdentityUser>()).Select(a => a.Id).ToList();

						if (!canTransmit || recipients.Count == 0)
							continue;

						departmentNumber ??= await _settingsService.GetTextToCallNumberForDepartmentAsync(department.Key);
						cultures ??= new ContentNoticeCultures(_userProfiles, dept);

						// Each recipient reads it in their own language, the date in the department's zone written their way (round 9C).
						foreach (var userId in recipients.Distinct(StringComparer.OrdinalIgnoreCase))
						{
							var (culture, profile) = await cultures.ForMemberAsync(userId);
							var notice = ReviewReminderNotice(item.Code, item.Name, item.NextReviewOn, stage, dept, culture);
							await _communicationService.Value.SendNotificationAsync(userId, department.Key, notice.Message, departmentNumber, dept, notice.Title, profile);
							sent++;
						}
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Protocol review reminders failed for department {department.Key}.");
				}
			}

			return sent;
		}

		/// <summary>The review due / overdue reminder in <paramref name="culture"/>, the date in the department's zone (round 9C).</summary>
		public static ContentNoticeDelivery.Notice ReviewReminderNotice(string code, string name, DateTime nextReviewOnUtc, ProtocolReviewReminderStages stage,
			Department department, CultureInfo culture)
		{
			var due = ContentNoticeCultures.LocalDate(nextReviewOnUtc, department, culture);
			return stage == ProtocolReviewReminderStages.Overdue
				? new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.ReviewOverdue.Title", culture), ContentNoticeText.Format("Protocol.ReviewOverdue.Message", culture, code, name, due))
				: new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Protocol.ReviewDue.Title", culture), ContentNoticeText.Format("Protocol.ReviewDue.Message", culture, code, name, due));
		}

		public async Task<int> PurgeExpiredRetentionAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var purged = 0;
			foreach (var departmentId in await _library.GetDepartmentsWithRetentionWorkAsync(WorkerBatch) ?? new List<int>())
			{
				try
				{
					var settings = await GetSettingsAsync(departmentId);
					if (settings.LegalHold || settings.Retention == null || settings.Retention.Count == 0)
						continue;

					var shortest = settings.Retention.Min(r => r.KeepDays);
					var candidates = await _versions.GetPurgeCandidatesAsync(departmentId, utcNow.AddDays(-shortest), WorkerBatch) ?? new List<ProtocolVersion>();
					var kinds = new Dictionary<int, int>();
					foreach (var version in candidates)
					{
						if (!kinds.TryGetValue(version.DispatchProtocolId, out var kind))
						{
							var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
							kind = protocol == null || protocol.LegalHold ? -1 : protocol.Kind;
							kinds[version.DispatchProtocolId] = kind;
						}

						var keep = kind < 0 ? null : settings.KeepDaysFor(kind);
						var ended = version.SupersededOn ?? version.RetiredOn;
						if (!keep.HasValue || !ended.HasValue || ended.Value >= utcNow.AddDays(-keep.Value))
							continue;

						purged += await _versions.PurgeAsync(departmentId, version.ProtocolVersionId, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Protocol retention purge failed for department {departmentId}.");
				}
			}

			return purged;
		}

		#endregion Worker 84

		#region Helpers

		private async Task<(DispatchProtocol, ProtocolLifecycleErrors)> AuthorableAsync(ProtocolReader reader, int protocolId)
		{
			if (reader == null || !reader.MayAuthor)
				return (null, ProtocolLifecycleErrors.NotAuthorized);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return (null, ProtocolLifecycleErrors.NotFound);

			return (protocol, ProtocolLifecycleErrors.None);
		}

		private async Task<List<int>> RoleIdsAsync(ProtocolReader reader)
		{
			if (reader?.Facts?.RoleIds != null && reader.Facts.RoleIds.Count > 0)
				return reader.Facts.RoleIds.ToList();
			return ((await _personnelRolesService.GetRolesForUserAsync(reader.UserId, reader.DepartmentId)) ?? new List<PersonnelRole>())
				.Where(r => r.DepartmentId == reader.DepartmentId).Select(r => r.PersonnelRoleId).ToList();
		}

		/// <summary>
		/// Caches (matching set, offline reference), the search projection and the knowledge index (K1: publish, retire, audience and
		/// AI-flag changes reindex the protocol and its files) after a library change.
		/// </summary>
		private async Task AfterLibraryChangeAsync(DispatchProtocol protocol, CancellationToken cancellationToken)
		{
			await _protocolsService.InvalidateProtocolCachesAsync(protocol.DepartmentId);
			if (_searchProjections?.Value != null)
				await _searchProjections.Value.ProjectProtocolAsync(protocol, cancellationToken);
			var id = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture);
			await _knowledgeQueue.EnqueueAsync(protocol.DepartmentId, KnowledgeSourceKinds.Protocol, id, cancellationToken);
			await _knowledgeQueue.EnqueueAsync(protocol.DepartmentId, KnowledgeSourceKinds.ProtocolFile, id, cancellationToken);
		}

		private static ProtocolAudience Audience(int departmentId, int protocolId, IContentAudienceRule rule) =>
			new ProtocolAudience { DepartmentId = departmentId, DispatchProtocolId = protocolId, Kind = rule.Kind, RefId = rule.RefId };

		private static int? NormalizeInterval(int? days) =>
			days.HasValue && days.Value > 0 ? Math.Min(days.Value, ProtocolLibrarySettings.MaxReviewIntervalDays) : (int?)null;

		private static string NormalizeTags(string tags)
		{
			if (string.IsNullOrWhiteSpace(tags))
				return null;
			var list = tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0)
				.Distinct(StringComparer.CurrentCultureIgnoreCase).Take(25).ToList();
			var joined = string.Join(", ", list);
			return joined.Length == 0 ? null : Truncate(joined, DispatchProtocol.TagsMaxLength);
		}

		private static string NormalizeLocale(string locale)
		{
			if (string.IsNullOrWhiteSpace(locale))
				return null;
			var clean = new string(locale.Trim().Where(c => char.IsLetterOrDigit(c) || c == '-').Take(16).ToArray());
			return clean.Length == 0 ? null : clean;
		}

		private static string Truncate(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
				return value;
			return value.Length > max ? value.Substring(0, max) : value;
		}

		private static string Snapshot(DispatchProtocol protocol, ProtocolVersion version) => JsonConvert.SerializeObject(new
		{
			ProtocolId = protocol?.DispatchProtocolId,
			protocol?.Code,
			protocol?.Name,
			protocol?.Kind,
			protocol?.Status,
			protocol?.CurrentVersionId,
			VersionId = version?.ProtocolVersionId,
			VersionStatus = version?.Status,
			Version = version?.Label,
			version?.ContentHash,
			version?.EffectiveOn
		});

		/// <summary>The version's HTML in its content language: block labels, and the P4 case flow's labels when it has a graph.</summary>
		private static string RenderContent(ProtocolContentDocument document, string locale)
		{
			document.GraphLabels = ProtocolBlockLabelsProvider.GraphFor(locale);
			return document.Render(ProtocolBlockLabelsProvider.For(locale));
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}

		#endregion Helpers
	}
}
