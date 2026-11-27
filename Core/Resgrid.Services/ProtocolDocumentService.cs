using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Document-backed protocols (P1 R-P5, R-P6). A version's content is one primary PDF or DOCX (ProtocolVersionFiles), hashed
	/// (SHA-256 of the bytes = the version's ContentHash) and immutable once it leaves draft, with the same lifecycle, approvals,
	/// audiences and citations as authored content (the library service runs those). Text is extracted page by page on upload
	/// (inline when small, else queued for worker 86) and feeds Unified Search through the protocol projection, audience-filtered.
	/// </summary>
	public class ProtocolDocumentService : IProtocolDocumentService
	{
		public const int WorkerBatch = 25;
		public const int AttachmentQueueBatch = 200;

		/// <summary>Bytes a zip may expand to (the per-file cap times the bulk count, bounded) and entries it may hold.</summary>
		public const long MaxZipExpandedBytes = 512L * 1024 * 1024;
		public const int MaxZipEntries = 1000;

		private readonly IProtocolLibraryService _library;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IProtocolVersionFilesRepository _files;
		private readonly IProtocolFileTextsRepository _texts;
		private readonly IDocumentsService _documents;
		private readonly IProtocolTextExtractor _extractor;
		private readonly IEventAggregator _eventAggregator;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly IProtocolAcknowledgmentsRepository _acknowledgments;
		private readonly IKnowledgeIndexQueue _knowledgeQueue;

		private readonly IDepartmentDataProtectionService _protection;

		public ProtocolDocumentService(IProtocolLibraryService library, IProtocolAccessService access, IProtocolsService protocolsService, IProtocolVersionsRepository versions,
			IProtocolVersionFilesRepository files, IProtocolFileTextsRepository texts, IDocumentsService documents, IProtocolTextExtractor extractor,
			IEventAggregator eventAggregator, Lazy<ISearchProjectionService> searchProjections, IProtocolAcknowledgmentsRepository acknowledgments,
			IKnowledgeIndexQueue knowledgeQueue, IDepartmentDataProtectionService protection)
		{
			_protection = protection;
			_acknowledgments = acknowledgments;
			_knowledgeQueue = knowledgeQueue;
			_library = library;
			_access = access;
			_protocolsService = protocolsService;
			_versions = versions;
			_files = files;
			_texts = texts;
			_documents = documents;
			_extractor = extractor;
			_eventAggregator = eventAggregator;
			_searchProjections = searchProjections;
		}

		#region Upload

		/// <summary>Whether a file may be a protocol's content: a PDF or DOCX (by its bytes, not its claimed type) within the cap.</summary>
		public static ProtocolLifecycleErrors CheckFile(string fileName, byte[] data)
		{
			if (data == null || data.Length == 0)
				return ProtocolLifecycleErrors.FileRequired;
			if (data.LongLength > ProtocolDocumentRules.MaxFileBytes)
				return ProtocolLifecycleErrors.FileTooLarge;
			if (!ProtocolDocumentRules.IsAllowed(fileName, data))
				return ProtocolLifecycleErrors.FileTypeNotAllowed;
			return ProtocolLifecycleErrors.None;
		}

		public async Task<ProtocolLifecycleResult> CreateFromFileAsync(ProtocolReader reader, ProtocolDocumentCreateRequest request, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayAuthor)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);
			if (request == null)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var check = CheckFile(request.FileName, request.Data);
			if (check != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(check);

			var fileName = ProtocolDocumentRules.SafeFileName(request.FileName);
			var (parsedNumber, parsedTitle) = ProtocolDocumentRules.ParseFileName(fileName);
			var title = string.IsNullOrWhiteSpace(request.Title) ? parsedTitle : request.Title.Trim();
			var number = string.IsNullOrWhiteSpace(request.Number) ? parsedNumber : request.Number.Trim();
			var kind = ProtocolKindRules.IsAuthorable(request.Kind) ? request.Kind : (int)ProtocolKinds.Policy;

			var created = await _library.CreateAsync(new ProtocolCreateRequest
			{
				DepartmentId = reader.DepartmentId,
				UserId = reader.UserId,
				ContentHtml = ProtocolDocumentRules.StubHtml(title, fileName),
				RequiresAcknowledgment = request.RequiresAcknowledgment,
				Audience = request.Audience ?? new List<ContentAudienceRule>(),
				Protocol = new DispatchProtocol
				{
					Name = title.Length > 100 ? title.Substring(0, 100) : title,
					Code = string.IsNullOrWhiteSpace(request.Code) ? ProtocolDocumentRules.CodeFor(number, title) : ProtocolRules.NormalizeCode(request.Code),
					Number = number,
					Description = request.Description,
					Kind = kind,
					CategoryId = request.CategoryId,
					Triggers = new List<DispatchProtocolTrigger>(),
					Questions = new List<DispatchProtocolQuestion>(),
					Attachments = new List<DispatchProtocolAttachment>()
				}
			}, cancellationToken);

			if (!created.Succeeded)
				return created;

			var attached = await AttachToDraftAsync(reader, created.Protocol, created.Version, fileName, request.Data, cancellationToken);
			return attached.Succeeded ? new ProtocolLifecycleResult { Protocol = created.Protocol, Version = attached.Version } : attached;
		}

		public async Task<ProtocolLifecycleResult> UploadToDraftAsync(ProtocolReader reader, int protocolId, string fileName, byte[] data, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayAuthor)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotAuthorized);

			var check = CheckFile(fileName, data);
			if (check != ProtocolLifecycleErrors.None)
				return ProtocolLifecycleResult.Fail(check);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);
			if (protocol.IsBulletin)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			// A new upload is a new version: the open draft, or a new one started from the version in force.
			var draft = await _library.GetOpenDraftAsync(reader.DepartmentId, protocolId);
			if (draft == null)
			{
				var started = await _library.StartDraftAsync(reader, protocolId, cancellationToken);
				if (!started.Succeeded)
					return started;
				draft = started.Version;
			}

			if (!ProtocolLifecycle.IsContentEditable(draft.Status))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotDraft, Protocol = protocol, Version = draft };

			return await AttachToDraftAsync(reader, protocol, draft, ProtocolDocumentRules.SafeFileName(fileName), data, cancellationToken);
		}

		/// <summary>
		/// The draft becomes document-backed with this file: any earlier file (and its text) of the draft goes, the new file is stored
		/// with its hash, the version's ContentHash becomes the file's and its ContentHtml the fixed stub; then the text is extracted.
		/// </summary>
		private async Task<ProtocolLifecycleResult> AttachToDraftAsync(ProtocolReader reader, DispatchProtocol protocol, ProtocolVersion draft, string fileName, byte[] data,
			CancellationToken cancellationToken)
		{
			if (draft == null || !ProtocolLifecycle.IsContentEditable(draft.Status))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.NotDraft, Protocol = protocol, Version = draft };

			var now = DateTime.UtcNow;
			var contentType = ProtocolDocumentRules.DetectContentType(fileName, data);
			var hash = ProtocolDocumentRules.HashFile(data);

			var previous = await _files.GetMetadataForVersionAsync(reader.DepartmentId, draft.ProtocolVersionId);
			if (previous != null)
			{
				await _texts.DeleteForSourceAsync(reader.DepartmentId, (int)ProtocolFileSourceKinds.VersionFile, previous.ProtocolVersionFileId, cancellationToken);
				await _files.DeleteForVersionAsync(reader.DepartmentId, draft.ProtocolVersionId, cancellationToken);
			}

			var file = await _files.InsertAsync(new ProtocolVersionFile
			{
				DepartmentId = reader.DepartmentId,
				DispatchProtocolId = protocol.DispatchProtocolId,
				ProtocolVersionId = draft.ProtocolVersionId,
				FileName = fileName,
				ContentType = contentType,
				Size = data.LongLength,
				Sha256 = hash,
				Data = data,
				CreatedOn = now,
				CreatedByUserId = reader.UserId
			}, cancellationToken, true);

			var fresh = await _library.GetVersionAsync(reader.DepartmentId, draft.ProtocolVersionId) ?? draft;
			var expected = fresh.RowVersion;
			var stub = ProtocolDocumentRules.StubHtml(protocol.Name, fileName);
			fresh.ContentKind = (int)ProtocolVersionContentKinds.Document;
			fresh.ContentHtml = stub;
			fresh.ContentJson = ProtocolContentDocument.FromHtml(stub).ToJson();
			fresh.ContentHash = hash;
			fresh.UpdatedOn = now;
			fresh.UpdatedByUserId = reader.UserId;
			if (!await _versions.TryUpdateAsync(fresh, expected, cancellationToken))
				return new ProtocolLifecycleResult { Error = ProtocolLifecycleErrors.StaleCopy, Protocol = protocol, Version = fresh, ConflictUserId = fresh.UpdatedByUserId, ConflictOn = fresh.UpdatedOn };

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolDocumentUploaded, JsonConvert.SerializeObject(new
			{
				ProtocolId = protocol.DispatchProtocolId,
				VersionId = fresh.ProtocolVersionId,
				file.FileName,
				file.Size,
				file.Sha256
			}));

			await ExtractOrQueueAsync(reader.DepartmentId, protocol.DispatchProtocolId, ProtocolFileSourceKinds.VersionFile, file.ProtocolVersionFileId, fresh.ProtocolVersionId,
				fileName, contentType, data, cancellationToken);

			return new ProtocolLifecycleResult { Protocol = protocol, Version = fresh };
		}

		public async Task<ProtocolVersionFile> GetReadableFileAsync(ProtocolReader reader, int protocolVersionId)
		{
			if (reader == null || !reader.IsCurrentMember || protocolVersionId <= 0)
				return null;

			var version = await _library.GetVersionAsync(reader.DepartmentId, protocolVersionId);
			if (version == null || !version.IsDocument)
				return null;

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId)
				return null;

			// The same per-version read rule as the reading view (R-P4): members read only published, superseded and retired
			// versions of a protocol in their audience; drafts and reviews are the managers'.
			var audience = await _library.GetAudienceAsync(reader.DepartmentId, protocol.DispatchProtocolId);
			if (!ProtocolAccessRules.CanReadVersion(reader, protocol, audience, version))
			{
				// A version scheduled for later is acknowledgeable from publish: the members it was assigned to may read it now.
				var assigned = await _acknowledgments.GetForVersionAndUserAsync(reader.DepartmentId, protocolVersionId, reader.UserId);
				if (assigned == null || !ProtocolAcknowledgmentRules.IsAcknowledgeable(protocol, version))
					return null;
			}

			return await _files.GetWithDataForVersionAsync(reader.DepartmentId, protocolVersionId);
		}

		public async Task<ProtocolDocumentView> GetDocumentViewAsync(int departmentId, int protocolVersionId)
		{
			var view = new ProtocolDocumentView { File = await _files.GetMetadataForVersionAsync(departmentId, protocolVersionId) };
			if (view.File == null)
				return view;

			view.Text = await _texts.GetForSourceAsync(departmentId, (int)ProtocolFileSourceKinds.VersionFile, view.File.ProtocolVersionFileId);
			if (view.Text != null && view.Text.Status == (int)ProtocolFileTextStatuses.Extracted)
				view.Pages = await _texts.GetPagesAsync(departmentId, view.Text.ProtocolFileTextId) ?? new List<ProtocolFileTextPage>();
			return view;
		}

		public async Task<ProtocolVersionFile> GetFileInfoAsync(int departmentId, int protocolVersionId)
		{
			if (departmentId <= 0 || protocolVersionId <= 0)
				return null;
			var file = await _files.GetMetadataForVersionAsync(departmentId, protocolVersionId);
			if (file == null || file.DepartmentId != departmentId)
				return null;
			file.Data = null;
			return file;
		}

		#endregion Upload

		#region Bulk upload and the move from Documents

		public async Task<List<ProtocolBulkUploadItem>> BulkUploadAsync(ProtocolReader reader, int kind, int? categoryId, bool foldersAsCategories,
			IReadOnlyList<ProtocolUploadFile> files, CancellationToken cancellationToken = default)
		{
			var results = new List<ProtocolBulkUploadItem>();
			if (reader == null || !reader.MayAuthor)
				return results;

			var categories = await _library.GetCategoriesAsync(reader.DepartmentId);
			if (categoryId.HasValue && categories.All(c => c.ProtocolCategoryId != categoryId.Value))
				categoryId = null;

			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var file in (files ?? new List<ProtocolUploadFile>()).Take(ProtocolDocumentRules.MaxBulkFiles))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var name = ProtocolDocumentRules.SafeFileName(file?.FileName);
				var (number, title) = ProtocolDocumentRules.ParseFileName(name);
				var item = new ProtocolBulkUploadItem { FileName = name, Number = number, Title = title };

				// The same file twice in one upload lands once.
				var hash = file?.Data == null ? null : ProtocolDocumentRules.HashFile(file.Data);
				var duplicate = hash != null && !seen.Add(hash);

				// A duplicate is not filed, so it never creates a category; it shows where it would have gone.
				var target = categoryId;
				if (foldersAsCategories && !string.IsNullOrWhiteSpace(file?.Folder))
					target = (duplicate
						? ExistingCategoryForFolder(categories, file.Folder)
						: await CategoryForFolderAsync(reader, categories, file.Folder, kind, cancellationToken)) ?? categoryId;

				// The result shows the category the file is filed in (the chosen one for loose files), not the raw zip folder path.
				item.Category = target.HasValue ? categories.FirstOrDefault(c => c.ProtocolCategoryId == target.Value)?.Name : null;

				if (duplicate)
				{
					item.Error = "duplicate";
					results.Add(item);
					continue;
				}

				try
				{
					var created = await CreateFromFileAsync(reader, new ProtocolDocumentCreateRequest
					{
						Kind = kind,
						CategoryId = target,
						Title = title,
						Number = number,
						FileName = name,
						Data = file?.Data
					}, cancellationToken);

					if (created.Succeeded)
						item.ProtocolId = created.Protocol.DispatchProtocolId;
					else
						item.Error = created.Error.ToString();
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// One bad file never stops the rest of the binder.
					Logging.LogException(ex, $"Bulk protocol upload failed for one file in department {reader.DepartmentId}.");
					item.Error = ProtocolLifecycleErrors.InvalidInput.ToString();
				}

				results.Add(item);
			}

			return results;
		}

		/// <summary>The category a zip folder names (the last folder of its path), created under no parent when the department has none by that name.</summary>
		private async Task<int?> CategoryForFolderAsync(ProtocolReader reader, List<ProtocolCategory> categories, string folder, int kind, CancellationToken cancellationToken)
		{
			var name = FolderCategoryName(folder);
			if (string.IsNullOrWhiteSpace(name))
				return null;

			var existing = ExistingCategoryForFolder(categories, folder);
			if (existing.HasValue)
				return existing;

			// Categories are a managers' structure: authors who are not managers file into the chosen category instead.
			if (!reader.IsManager)
				return null;

			var saved = await _library.SaveCategoryAsync(reader.DepartmentId, new ProtocolCategory
			{
				Name = name,
				Kind = ProtocolKindRules.IsAuthorable(kind) ? kind : (int?)null
			}, reader.UserId, cancellationToken);
			if (saved != null)
				categories.Add(saved);
			return saved?.ProtocolCategoryId;
		}

		/// <summary>The department category a zip folder already names, if any; never creates one.</summary>
		private static int? ExistingCategoryForFolder(List<ProtocolCategory> categories, string folder)
		{
			var name = FolderCategoryName(folder);
			if (string.IsNullOrWhiteSpace(name))
				return null;

			return categories.FirstOrDefault(c => string.Equals(c.Name?.Trim(), name, StringComparison.CurrentCultureIgnoreCase))?.ProtocolCategoryId;
		}

		private static string FolderCategoryName(string folder) =>
			folder?.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();

		public List<ProtocolUploadFile> ReadZip(byte[] zip)
		{
			var files = new List<ProtocolUploadFile>();
			if (zip == null || zip.Length == 0)
				return files;

			try
			{
				using var stream = new MemoryStream(zip, false);
				using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
				long expanded = 0;
				foreach (var entry in archive.Entries.Take(MaxZipEntries))
				{
					if (files.Count >= ProtocolDocumentRules.MaxBulkFiles)
						break;
					if (string.IsNullOrEmpty(entry.Name) || entry.FullName.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) || entry.Name.StartsWith("._", StringComparison.Ordinal))
						continue;

					var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
					if (extension != ".pdf" && extension != ".docx")
						continue;

					// Bounded by the declared size and by what is actually read (a zip bomb lies about the first).
					if (entry.Length > ProtocolDocumentRules.MaxFileBytes || expanded + entry.Length > MaxZipExpandedBytes)
						continue;

					using var entryStream = entry.Open();
					using var buffer = new MemoryStream();
					var chunk = new byte[81920];
					int read;
					long total = 0;
					var tooBig = false;
					while ((read = entryStream.Read(chunk, 0, chunk.Length)) > 0)
					{
						total += read;
						if (total > ProtocolDocumentRules.MaxFileBytes)
						{
							tooBig = true;
							break;
						}
						buffer.Write(chunk, 0, read);
					}

					if (tooBig)
						continue;

					expanded += total;
					var folder = Path.GetDirectoryName(entry.FullName.Replace('\\', '/'))?.Replace('\\', '/');
					files.Add(new ProtocolUploadFile { FileName = entry.Name, Folder = string.IsNullOrWhiteSpace(folder) ? null : folder, Data = buffer.ToArray() });
				}
			}
			catch (InvalidDataException)
			{
				// Not a zip: nothing to read.
			}

			return files;
		}

		public async Task<ProtocolBulkUploadItem> MoveFromDocumentAsync(ProtocolReader reader, int documentId, int kind, bool canSeeAdminsOnly, CancellationToken cancellationToken = default)
		{
			var item = new ProtocolBulkUploadItem();
			if (reader == null || !reader.MayAuthor)
			{
				item.Error = ProtocolLifecycleErrors.NotAuthorized.ToString();
				return item;
			}

			var document = await _documents.GetDocumentByIdAsync(documentId);
			if (document == null || document.DepartmentId != reader.DepartmentId || (document.AdminsOnly && !canSeeAdminsOnly))
			{
				item.Error = ProtocolLifecycleErrors.NotFound.ToString();
				return item;
			}

			item.FileName = document.Filename ?? document.Name;
			item.Title = document.Name;
			item.Category = document.Category;

			if (document.MovedToProtocolId.HasValue)
			{
				item.ProtocolId = document.MovedToProtocolId;
				return item;
			}

			// Advanced Data Protection: the server never decrypts a document to move it; a protected document stays in Documents. That holds for
			// every document of a department whose ADP is on, including one its enrollment night has not swept yet (Documents.Data, Description and
			// Filename are cataloged): moving it would copy it out of protection into the protocol library, search and the knowledge index.
			if (document.IsProtected || ProtectedReadService.IsBinaryEnveloped(document.Data) || await DepartmentProtectsDocumentsAsync(reader.DepartmentId))
			{
				item.Error = "protected";
				return item;
			}

			var moveKind = kind == (int)ProtocolKinds.Reference ? (int)ProtocolKinds.Reference : (int)ProtocolKinds.Policy;
			var fileName = string.IsNullOrWhiteSpace(document.Filename) ? document.Name : document.Filename;
			var (number, _) = ProtocolDocumentRules.ParseFileName(fileName);
			var created = await CreateFromFileAsync(reader, new ProtocolDocumentCreateRequest
			{
				Kind = moveKind,
				Title = string.IsNullOrWhiteSpace(document.Name) ? null : document.Name.Trim(),
				Number = number,
				Description = document.Description,
				FileName = fileName,
				Data = document.Data
			}, cancellationToken);

			if (!created.Succeeded)
			{
				item.Error = created.Error.ToString();
				return item;
			}

			// The document stays as the redirect at its old link; its bytes now live with the protocol version.
			document.MovedToProtocolId = created.Protocol.DispatchProtocolId;
			document.MovedOn = DateTime.UtcNow;
			document.Data = Array.Empty<byte>();
			await _documents.SaveDocumentAsync(document, cancellationToken);

			Audit(reader.DepartmentId, reader.UserId, AuditLogTypes.ProtocolDocumentMoved, JsonConvert.SerializeObject(new
			{
				DocumentId = document.DocumentId,
				ProtocolId = created.Protocol.DispatchProtocolId,
				Kind = moveKind
			}));

			item.ProtocolId = created.Protocol.DispatchProtocolId;
			return item;
		}

		/// <summary>True when the department encrypts new writes (an unreadable state counts as protected: nothing is copied out).</summary>
		private async Task<bool> DepartmentProtectsDocumentsAsync(int departmentId)
		{
			if (_protection == null)
				return false;
			try
			{
				return await _protection.ShouldEncryptNewWritesAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The protection state of department {departmentId} could not be read; the document stays in Documents.");
				return true;
			}
		}

		public async Task<List<ProtocolBulkUploadItem>> MoveCategoryFromDocumentsAsync(ProtocolReader reader, string category, int kind, bool canSeeAdminsOnly,
			CancellationToken cancellationToken = default)
		{
			var results = new List<ProtocolBulkUploadItem>();
			if (reader == null || !reader.MayAuthor || string.IsNullOrWhiteSpace(category))
				return results;

			var documents = (await _documents.GetFilteredDocumentsByDepartmentIdAsync(reader.DepartmentId, null, category) ?? new List<Document>())
				.Where(d => d.DepartmentId == reader.DepartmentId && !d.MovedToProtocolId.HasValue && string.Equals(d.Category, category, StringComparison.CurrentCultureIgnoreCase))
				.Take(ProtocolDocumentRules.MaxBulkFiles)
				.ToList();

			foreach (var document in documents)
				results.Add(await MoveFromDocumentAsync(reader, document.DocumentId, kind, canSeeAdminsOnly, cancellationToken));

			return results;
		}

		#endregion Bulk upload and the move from Documents

		#region Text extraction

		public async Task<ProtocolFileText> ExtractOrQueueAsync(int departmentId, int protocolId, ProtocolFileSourceKinds sourceKind, int sourceId, int? protocolVersionId,
			string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			try
			{
				var text = await _texts.GetForSourceAsync(departmentId, (int)sourceKind, sourceId);
				var now = DateTime.UtcNow;
				text ??= await _texts.InsertAsync(new ProtocolFileText
				{
					DepartmentId = departmentId,
					DispatchProtocolId = protocolId,
					SourceKind = (int)sourceKind,
					SourceId = sourceId,
					ProtocolVersionId = protocolVersionId,
					Status = (int)ProtocolFileTextStatuses.Pending,
					QueuedOn = now
				}, cancellationToken, true);

				// Large files never extract in a request thread: worker 86 takes them.
				if (data == null || data.LongLength > ProtocolDocumentRules.InlineExtractionMaxBytes)
					return text;

				using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				budget.CancelAfter(ProtocolDocumentRules.InlineExtractionBudget);
				var result = _extractor.Extract(fileName, contentType, data, budget.Token);

				// Out of time inline: left Pending for worker 86 (not counted as an attempt).
				if (result.Status == ProtocolFileTextStatuses.Failed && result.Reason == ProtocolFileNoTextReasons.Timeout)
					return text;

				await SaveResultAsync(text, result, now, cancellationToken);
				return text;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// The upload succeeded; a failed extraction leaves the file searchable by its title only.
				Logging.LogException(ex, $"Text extraction of protocol file {sourceKind} {sourceId} failed.");
				return null;
			}
		}

		private async Task SaveResultAsync(ProtocolFileText text, ProtocolTextExtractionResult result, DateTime utcNow, CancellationToken cancellationToken)
		{
			text.Status = (int)result.Status;
			text.Reason = result.Reason?.Length > ProtocolFileText.ReasonMaxLength ? result.Reason.Substring(0, ProtocolFileText.ReasonMaxLength) : result.Reason;
			text.PageCount = result.Status == ProtocolFileTextStatuses.Extracted ? result.Pages.Count : 0;
			text.CharCount = result.Status == ProtocolFileTextStatuses.Extracted ? result.CharCount : 0;
			text.ExtractedOn = utcNow;
			await _texts.SaveResultAsync(text, result.Status == ProtocolFileTextStatuses.Extracted ? result.Pages : new List<string>(), cancellationToken);

			// The knowledge index (K1) reads finished extractions: re-chunk the protocol's file text now that it has some. The queue
			// never throws, and worker 89 drops the request when the department is not entitled to knowledge answers.
			if (result.Status == ProtocolFileTextStatuses.Extracted)
				await _knowledgeQueue.EnqueueAsync(text.DepartmentId, KnowledgeSourceKinds.ProtocolFile, text.DispatchProtocolId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
		}

		public async Task<int> QueueAttachmentExtractionsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var queued = 0;
			foreach (var candidate in await _texts.GetAttachmentsWithoutTextAsync(AttachmentQueueBatch) ?? new List<ProtocolAttachmentTextCandidate>())
			{
				try
				{
					await _texts.InsertAsync(new ProtocolFileText
					{
						DepartmentId = candidate.DepartmentId,
						DispatchProtocolId = candidate.DispatchProtocolId,
						SourceKind = (int)ProtocolFileSourceKinds.Attachment,
						SourceId = candidate.DispatchProtocolAttachmentId,
						Status = (int)ProtocolFileTextStatuses.Pending,
						QueuedOn = utcNow
					}, cancellationToken, true);
					queued++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// Another worker queued it first (the unique source index): nothing to do.
					Logging.LogDebug($"Attachment {candidate.DispatchProtocolAttachmentId} was not queued for text: {ex.Message}");
				}
			}

			return queued;
		}

		public async Task<int> ProcessPendingExtractionsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var finished = 0;
			foreach (var text in await _texts.GetPendingAsync(WorkerBatch) ?? new List<ProtocolFileText>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					// Claim one attempt first: a second worker run never extracts the same file at once.
					if (!await _texts.TryClaimAsync(text.ProtocolFileTextId, text.Attempts, cancellationToken))
						continue;
					text.Attempts++;

					var (fileName, contentType, data) = await LoadSourceAsync(text);
					ProtocolTextExtractionResult result;
					if (data == null)
					{
						result = ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Unreadable);
					}
					else
					{
						using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
						budget.CancelAfter(ProtocolDocumentRules.WorkerExtractionBudget);
						result = _extractor.Extract(fileName, contentType, data, budget.Token);
					}

					if (result.Status == ProtocolFileTextStatuses.Failed)
					{
						if (text.Attempts < ProtocolDocumentRules.MaxAttempts)
							continue; // stays Pending for the next run
						result = result.Reason == ProtocolFileNoTextReasons.Timeout ? ProtocolTextExtractionResult.NoText(ProtocolFileNoTextReasons.Timeout) : result;
					}

					await SaveResultAsync(text, result, utcNow, cancellationToken);
					finished++;

					// The version in force's file or an attachment: the search projection picks up the new text.
					if (result.Status == ProtocolFileTextStatuses.Extracted && _searchProjections?.Value != null)
					{
						var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(text.DispatchProtocolId);
						if (protocol != null && protocol.DepartmentId == text.DepartmentId && protocol.IsPublished &&
							(text.SourceKind == (int)ProtocolFileSourceKinds.Attachment || protocol.CurrentVersionId == text.ProtocolVersionId))
							await _searchProjections.Value.ProjectProtocolAsync(protocol, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Queued text extraction {text.ProtocolFileTextId} failed.");
				}
			}

			return finished;
		}

		private async Task<(string FileName, string ContentType, byte[] Data)> LoadSourceAsync(ProtocolFileText text)
		{
			if (text.SourceKind == (int)ProtocolFileSourceKinds.VersionFile)
			{
				var file = await _files.GetWithDataByIdAsync(text.DepartmentId, text.SourceId);
				return file == null ? (null, null, null) : (file.FileName, file.ContentType, file.Data);
			}

			var attachment = await _protocolsService.GetAttachmentForDepartmentAsync(text.DepartmentId, text.SourceId);
			return attachment == null ? (null, null, null) : (attachment.FileName, attachment.FileType, attachment.Data);
		}

		#endregion Text extraction

		private void Audit(int departmentId, string userId, AuditLogTypes type, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
