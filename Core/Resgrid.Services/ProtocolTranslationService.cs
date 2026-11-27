using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Content translations (P3): a locale variant of a locked version, written by an author and approved at L2 by an approver, bound to the
	/// translation's own hash. The source version, its hash and its signatures are never touched. Readers get the approved translation in
	/// their language when it was made from the version's current content, else the source (the department's language). Approving or
	/// withdrawing a translation re-projects the protocol into Unified Search (its text joins the protocol's row, audience-filtered as the
	/// source) and re-indexes it for K1 (approved translations are locale variants of the version's chunks; round 5F).
	/// </summary>
	public class ProtocolTranslationService : IProtocolTranslationService
	{
		private readonly IProtocolVersionTranslationsRepository _translations;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolAccessService _access;
		private readonly ISignatureService _signatures;
		private readonly IEventAggregator _eventAggregator;
		private readonly IProtocolsService _protocolsService;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly IKnowledgeIndexQueue _knowledgeQueue;

		public ProtocolTranslationService(IProtocolVersionTranslationsRepository translations, IProtocolLibraryService library, IProtocolAccessService access,
			ISignatureService signatures, IEventAggregator eventAggregator, IProtocolsService protocolsService, Lazy<ISearchProjectionService> searchProjections,
			IKnowledgeIndexQueue knowledgeQueue)
		{
			_protocolsService = protocolsService;
			_searchProjections = searchProjections;
			_knowledgeQueue = knowledgeQueue;
			_translations = translations;
			_library = library;
			_access = access;
			_signatures = signatures;
			_eventAggregator = eventAggregator;
		}

		public async Task<List<ProtocolVersionTranslation>> GetForVersionAsync(int departmentId, int protocolVersionId)
		{
			if (departmentId <= 0 || protocolVersionId <= 0)
				return new List<ProtocolVersionTranslation>();
			return (await _translations.GetForVersionAsync(departmentId, protocolVersionId) ?? new List<ProtocolVersionTranslation>())
				.Where(t => t.DepartmentId == departmentId && t.ProtocolVersionId == protocolVersionId).ToList();
		}

		public async Task<ProtocolVersionTranslation> GetAsync(int departmentId, int protocolVersionTranslationId)
		{
			if (departmentId <= 0 || protocolVersionTranslationId <= 0)
				return null;
			var translation = await _translations.GetByIdForDepartmentAsync(departmentId, protocolVersionTranslationId);
			return translation != null && translation.DepartmentId == departmentId ? translation : null;
		}

		public async Task<ProtocolTranslationResult> StartAsync(ProtocolReader reader, int protocolVersionId, string locale, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayAuthor)
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.NotAuthorized);

			var version = await _library.GetVersionAsync(reader.DepartmentId, protocolVersionId);
			if (version == null || await _access.GetReadableAsync(reader.DepartmentId, reader.UserId, version.DispatchProtocolId) == null)
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.NotFound);
			if (ProtocolLifecycle.IsContentEditable(version.Status) || version.IsDocument || version.Status == (int)ProtocolVersionStatuses.Legacy)
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.SourceNotLocked);

			var settings = await _library.GetSettingsAsync(reader.DepartmentId);
			var clean = ProtocolTranslationRules.NormalizeLocale(locale);
			if (clean == null || ProtocolTranslationRules.SameLanguage(clean, settings.ContentLocaleFor(version)))
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.InvalidLocale);

			var existing = await GetForVersionAsync(reader.DepartmentId, protocolVersionId);
			if (existing.Any(t => string.Equals(t.Locale, clean, StringComparison.OrdinalIgnoreCase)))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.AlreadyExists, Translation = existing.First(t => string.Equals(t.Locale, clean, StringComparison.OrdinalIgnoreCase)) };

			// The draft starts as the source's wording, aligned to its structure; the translator replaces the words.
			var source = ProtocolContentDocument.Parse(version.ContentJson, version.ContentHtml);
			var document = ProtocolTranslationRules.Align(source, source);
			var html = document.Render(ProtocolBlockLabelsProvider.For(clean));
			var now = DateTime.UtcNow;
			ProtocolVersionTranslation saved;
			try
			{
				saved = await _translations.InsertAsync(new ProtocolVersionTranslation
				{
					DepartmentId = reader.DepartmentId,
					DispatchProtocolId = version.DispatchProtocolId,
					ProtocolVersionId = version.ProtocolVersionId,
					Locale = clean,
					Status = (int)ProtocolTranslationStatuses.Draft,
					ContentJson = document.ToJson(),
					ContentHtml = html,
					ContentHash = ProtocolContentHash.Compute(html),
					SourceContentHash = version.ContentHash,
					CreatedOn = now,
					CreatedByUserId = reader.UserId,
					UpdatedOn = now,
					UpdatedByUserId = reader.UserId
				}, cancellationToken, true);
			}
			catch (Exception ex) when (ex.Message.IndexOf("UX_ProtocolVersionTranslations", StringComparison.OrdinalIgnoreCase) >= 0
				|| ex.Message.IndexOf("ux_protocolversiontranslations", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.AlreadyExists);
			}

			Audit(reader, AuditLogTypes.ProtocolTranslationSaved, null, saved);
			return new ProtocolTranslationResult { Translation = saved };
		}

		public async Task<ProtocolTranslationResult> SaveAsync(ProtocolReader reader, int protocolVersionTranslationId, string contentJson, string contentHtml,
			long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayAuthor)
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.NotAuthorized);

			var (translation, version, error) = await LoadAsync(reader, protocolVersionTranslationId);
			if (error != ProtocolTranslationErrors.None)
				return ProtocolTranslationResult.Fail(error);
			if (translation.Status != (int)ProtocolTranslationStatuses.Draft)
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.NotDraft, Translation = translation };
			if (translation.RowVersion != expectedRowVersion)
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.StaleCopy, Translation = translation };

			var source = ProtocolContentDocument.Parse(version.ContentJson, version.ContentHtml);
			var given = !string.IsNullOrWhiteSpace(contentJson)
				? ProtocolContentDocument.Parse(contentJson, null)
				: ProtocolContentDocument.FromHtml(contentHtml ?? string.Empty);
			var document = ProtocolTranslationRules.Align(source, given);
			var html = document.Render(ProtocolBlockLabelsProvider.For(translation.Locale));
			if (string.IsNullOrWhiteSpace(Framework.StringHelpers.HtmlToPlainText(html)))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.EmptyContent, Translation = translation };

			var before = translation.ContentHash;
			translation.ContentJson = document.ToJson();
			translation.ContentHtml = html;
			translation.ContentHash = ProtocolContentHash.Compute(html);
			translation.SourceContentHash = version.ContentHash;
			translation.UpdatedOn = DateTime.UtcNow;
			translation.UpdatedByUserId = reader.UserId;
			if (!await _translations.TryUpdateAsync(translation, expectedRowVersion, cancellationToken))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.StaleCopy, Translation = await GetAsync(reader.DepartmentId, protocolVersionTranslationId) };

			Audit(reader, AuditLogTypes.ProtocolTranslationSaved, new { ContentHash = before }, translation);
			return new ProtocolTranslationResult { Translation = translation };
		}

		public async Task<ProtocolTranslationResult> ApproveAsync(ProtocolReader reader, int protocolVersionTranslationId, SignatureProof proof, string statementText = null,
			CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.NotAuthorized);

			var (translation, version, error) = await LoadAsync(reader, protocolVersionTranslationId);
			if (error != ProtocolTranslationErrors.None)
				return ProtocolTranslationResult.Fail(error);
			if (translation.Status != (int)ProtocolTranslationStatuses.Draft)
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.NotDraft, Translation = translation };
			if (!string.Equals(translation.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.SourceChanged, Translation = translation };

			// The approval binds to the translation's hash recomputed from what is stored now, never to the source's.
			var hash = ProtocolContentHash.Compute(translation.ContentHtml);
			if (!string.Equals(hash, translation.ContentHash, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(Framework.StringHelpers.HtmlToPlainText(translation.ContentHtml)))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.EmptyContent, Translation = translation };
			if (proof == null || !string.Equals(proof.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.SignatureRequired, Translation = translation };

			var protocol = await _access.GetReadableAsync(reader.DepartmentId, reader.UserId, version.DispatchProtocolId);
			var settings = await _library.GetSettingsAsync(reader.DepartmentId);
			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = reader.DepartmentId,
				Use = SignatureUse.ProtocolTranslationApproval,
				SubjectId = translation.ProtocolVersionTranslationId.ToString(CultureInfo.InvariantCulture),
				RevisionId = translation.Locale + "/" + translation.RowVersion.ToString(CultureInfo.InvariantCulture),
				ArtifactSha256 = hash,
				StatementText = string.IsNullOrWhiteSpace(statementText) ? ProtocolSignatureStatements.TranslationApproval(protocol, version, translation) : statementText,
				RequirePasskey = settings.RequirePasskeyForApproval
			}, proof, cancellationToken);

			if (!signed.Succeeded)
				return new ProtocolTranslationResult
				{
					Error = signed.Outcome == SignatureOutcomes.PasskeyRequired ? ProtocolTranslationErrors.PasskeyRequired : ProtocolTranslationErrors.SignatureRequired,
					Translation = translation
				};

			var expected = translation.RowVersion;
			translation.Status = (int)ProtocolTranslationStatuses.Approved;
			translation.ApprovedOn = DateTime.UtcNow;
			translation.ApprovedByUserId = reader.UserId;
			translation.SignatureRecordId = signed.Record.SignatureRecordId;
			if (!await _translations.TryUpdateAsync(translation, expected, cancellationToken))
			{
				// Someone saved the draft between the check and the approval: the signature stands for nothing.
				await _signatures.VoidAsync(reader.DepartmentId, signed.Record.SignatureRecordId, reader.UserId, "The translation changed before its approval was recorded.", cancellationToken);
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.StaleCopy, Translation = await GetAsync(reader.DepartmentId, protocolVersionTranslationId) };
			}

			Audit(reader, AuditLogTypes.ProtocolTranslationApproved, null, translation);
			// Workflow trigger 249: the compare-and-set above stored the approval once; the protocol may be unreadable to this reader (ids only then).
			var approvedEvent = ProtocolWorkflowEvent.For(protocol != null && protocol.DepartmentId == reader.DepartmentId ? protocol : null, version,
				ProtocolWorkflowChanges.TranslationApproved, reader.UserId, translation.ApprovedOn ?? DateTime.UtcNow);
			approvedEvent.DepartmentId = reader.DepartmentId;
			approvedEvent.ProtocolId = translation.DispatchProtocolId;
			approvedEvent.Locale = translation.Locale;
			approvedEvent.TranslationId = translation.ProtocolVersionTranslationId;
			ContentWorkflowContext.Publish(_eventAggregator, approvedEvent);
			await AfterTranslationChangeAsync(reader.DepartmentId, translation.DispatchProtocolId, cancellationToken);
			return new ProtocolTranslationResult { Translation = translation, Signature = signed.Record };
		}

		public async Task<ProtocolTranslationResult> WithdrawAsync(ProtocolReader reader, int protocolVersionTranslationId, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolTranslationResult.Fail(ProtocolTranslationErrors.NotAuthorized);

			var (translation, _, error) = await LoadAsync(reader, protocolVersionTranslationId);
			if (error != ProtocolTranslationErrors.None)
				return ProtocolTranslationResult.Fail(error);
			if (translation.Status == (int)ProtocolTranslationStatuses.Withdrawn)
				return new ProtocolTranslationResult { Translation = translation };

			var expected = translation.RowVersion;
			translation.Status = (int)ProtocolTranslationStatuses.Withdrawn;
			translation.WithdrawnOn = DateTime.UtcNow;
			translation.WithdrawnByUserId = reader.UserId;
			if (!await _translations.TryUpdateAsync(translation, expected, cancellationToken))
				return new ProtocolTranslationResult { Error = ProtocolTranslationErrors.StaleCopy, Translation = await GetAsync(reader.DepartmentId, protocolVersionTranslationId) };

			Audit(reader, AuditLogTypes.ProtocolTranslationWithdrawn, null, translation);
			await AfterTranslationChangeAsync(reader.DepartmentId, translation.DispatchProtocolId, cancellationToken);
			return new ProtocolTranslationResult { Translation = translation };
		}

		public async Task<ProtocolLocalizedContent> GetLocalizedAsync(int departmentId, ProtocolVersion version, string requestedLocale)
		{
			var content = new ProtocolLocalizedContent { Version = version };
			if (version == null || departmentId <= 0 || version.DepartmentId != departmentId)
				return content;

			var settings = await _library.GetSettingsAsync(departmentId);
			var sourceLocale = settings.ContentLocaleFor(version);
			content.Locale = sourceLocale;
			content.AvailableLocales.Add(sourceLocale);
			if (version.IsDocument)
				return content;

			// Only approved translations made from the version's current content, whose stored HTML still hashes to the approved hash.
			var approved = (await _translations.GetForVersionAsync(departmentId, version.ProtocolVersionId, true) ?? new List<ProtocolVersionTranslation>())
				.Where(t => t.DepartmentId == departmentId && t.ProtocolVersionId == version.ProtocolVersionId && t.IsApproved
					&& string.Equals(t.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(ProtocolContentHash.Compute(t.ContentHtml), t.ContentHash, StringComparison.Ordinal))
				.ToList();
			content.AvailableLocales.AddRange(approved.Select(t => t.Locale).Where(l => !ProtocolTranslationRules.SameLanguage(l, sourceLocale)));

			var requested = ProtocolTranslationRules.NormalizeLocale(requestedLocale);
			if (requested == null || ProtocolTranslationRules.SameLanguage(requested, sourceLocale))
				return content;

			var match = approved.FirstOrDefault(t => string.Equals(t.Locale, requested, StringComparison.OrdinalIgnoreCase))
				?? approved.FirstOrDefault(t => ProtocolTranslationRules.SameLanguage(t.Locale, requested));
			if (match == null)
			{
				content.IsFallback = true;
				return content;
			}

			content.Translation = match;
			content.Locale = match.Locale;
			return content;
		}

		private async Task<(ProtocolVersionTranslation Translation, ProtocolVersion Version, ProtocolTranslationErrors Error)> LoadAsync(ProtocolReader reader, int protocolVersionTranslationId)
		{
			var translation = await GetAsync(reader.DepartmentId, protocolVersionTranslationId);
			if (translation == null)
				return (null, null, ProtocolTranslationErrors.NotFound);
			var version = await _library.GetVersionAsync(reader.DepartmentId, translation.ProtocolVersionId);
			if (version == null || await _access.GetReadableAsync(reader.DepartmentId, reader.UserId, version.DispatchProtocolId) == null)
				return (null, null, ProtocolTranslationErrors.NotFound);
			return (translation, version, ProtocolTranslationErrors.None);
		}

		/// <summary>
		/// Round 5F: an approved (or withdrawn) translation changes what search and the knowledge index hold for the protocol. Never fatal: the
		/// approval stands, and the nightly search rebuild and the next reindex pick it up.
		/// </summary>
		private async Task AfterTranslationChangeAsync(int departmentId, int protocolId, CancellationToken cancellationToken)
		{
			try
			{
				var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
				if (protocol == null || protocol.DepartmentId != departmentId)
					return;
				if (_searchProjections?.Value != null)
					await _searchProjections.Value.ProjectProtocolAsync(protocol, cancellationToken);
				await _knowledgeQueue.EnqueueAsync(departmentId, KnowledgeSourceKinds.Protocol, protocolId.ToString(CultureInfo.InvariantCulture), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Search and knowledge refresh after a translation of protocol {protocolId} failed.");
			}
		}

		private void Audit(ProtocolReader reader, AuditLogTypes type, object before, ProtocolVersionTranslation translation)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = reader.DepartmentId,
				UserId = reader.UserId,
				Type = type,
				Before = before == null ? null : JsonConvert.SerializeObject(before),
				After = JsonConvert.SerializeObject(new
				{
					translation.ProtocolVersionTranslationId,
					translation.DispatchProtocolId,
					translation.ProtocolVersionId,
					translation.Locale,
					translation.Status,
					translation.ContentHash,
					translation.SourceContentHash,
					translation.SignatureRecordId
				}),
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
