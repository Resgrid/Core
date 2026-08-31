using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Services
{
	public class DocumentsService : IDocumentsService
	{
		private readonly IDocumentRepository _documentRepository;
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		private readonly IDocumentCategoriesRepository _documentCategoriesRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;

		public DocumentsService(IDocumentRepository documentRepository, IDocumentCategoriesRepository documentCategoriesRepository,
			IEventAggregator eventAggregator, Lazy<IProtectedWriteService> protectedWriteService,
			IUnitOfWork unitOfWork)
		{
			_protectedWriteService = protectedWriteService;
			_documentRepository = documentRepository;
			_documentCategoriesRepository = documentCategoriesRepository;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
		}

		public async Task<List<Document>> GetAllDocumentsByDepartmentIdAsync(int departmentId)
		{
			var documents = await _documentRepository.GetAllByDepartmentIdAsync(departmentId);
			return documents.ToList();
		}

		public async Task<List<Document>> GetFilteredDocumentsByDepartmentIdAsync(int departmentId, string type, string category)
		{
			var result = await GetAllDocumentsByDepartmentIdAsync(departmentId);

			if (!string.IsNullOrWhiteSpace(type))
			{
				switch (type)
				{
					case "Documents":
						result = result.Where(x => x.Type == "application/pdf" || x.Type == "application/octet-stream" || x.Type == "application/msword" || x.Type == "application/vnd.openxmlformats-officedocument.wordprocessingml.document").ToList();
						break;
					case "Spreadsheets":
						result = result.Where(x => x.Type == "application/vnd.ms-excel" || x.Type == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet").ToList();
						break;
					case "Presentations":
						result = result.Where(x => x.Type == "application/vnd.ms-powerpoint" || x.Type == "application/powerpoint" || x.Type == "application/vnd.openxmlformats-officedocument.presentationml.presentation").ToList();
						break;
					case "Images":
						result = result.Where(x => x.Type == "image/jpeg" || x.Type == "image/png" || x.Type == "image/gif").ToList();
						break;
					default:
						break;
				}
			}

			if (!string.IsNullOrWhiteSpace(category))
			{
				result = result.Where(x => x.Category == category).ToList();
			}

			return result;
		}

		public async Task<Document> SaveDocumentAsync(Document document, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The stored row backs REDACTED-sentinel restoration and the "no new file uploaded" keep.
			Document existing = null;
			if (document != null && document.DocumentId > 0)
				existing = await _documentRepository.GetByIdAsync(document.DocumentId);

			// ADP write safety net (plan 4.2/19.2, catalog v9). An UPDATE already has its identity, so
			// it is enveloped BEFORE the save and no plaintext version of a cataloged field ever
			// reaches the table - the same split CertificationService uses. Fails closed.
			var isExistingRow = document != null && document.DocumentId > 0;

			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareDocumentWriteAsync(
					document.DepartmentId, document, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); document {document.DocumentId} was NOT saved.");

				return await _documentRepository.SaveOrUpdateAsync(document, cancellationToken);
			}

			// An INSERT cannot be enveloped first: the AAD row key IS the identity pk, and only the
			// database can assign it. So the insert, the encryption and the re-save run inside ONE
			// transaction and commit only once the values are enveloped. Without it a broker failure
			// left a committed row holding the document's plaintext - including its file bytes - in a
			// protected department, which is the exact thing this feature exists to prevent, and
			// throwing afterwards did nothing to remove it.
			//
			// The transaction does span the broker round trip (up to DataProtectionConfig
			// .BrokerTimeoutMs). That is deliberate: this is a low-frequency, interactive upload
			// holding one new row, and a slow save is recoverable where readable plaintext at rest
			// is not.
			//
			// A caller that ALREADY holds a unit of work is refused rather than served. The rollback
			// below is the only thing between a failed protected write and a committed plaintext row,
			// and it is not ours to perform on somebody else's transaction: discarding their work
			// would be worse than the problem being fixed, and IUnitOfWork exposes no rollback-only
			// flag to raise instead - so the caller would go on to commit the plaintext insert this
			// method had just made. Refusing is loud and recoverable; committing plaintext is not.
			if (_unitOfWork.Connection != null)
				throw new InvalidOperationException(
					"A new document cannot be created inside a caller-owned transaction: its plaintext insert could not be rolled back independently if the protected write failed.");

			await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

			try
			{
				var saved = await _documentRepository.SaveOrUpdateAsync(document, cancellationToken);

				var protectedWrite = await _protectedWriteService.Value.PrepareDocumentWriteAsync(saved.DepartmentId,
					saved, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); document {saved.DocumentId} was NOT saved.");

				if (protectedWrite.Changed)
					saved = await _documentRepository.SaveOrUpdateAsync(saved, cancellationToken);

				_unitOfWork.CommitChanges();

				return saved;
			}
			catch (Exception ex)
			{
				// Logged here rather than left to the caller: this is the point that knows the insert
				// was rolled back, and that no plaintext row survived the failure.
				Logging.LogException(ex, $"Document create rolled back for department {document?.DepartmentId}; the protected write did not complete.");

				_unitOfWork.DiscardChanges();

				throw;
			}
		}

		public async Task<List<string>> GetDistinctCategoriesByDepartmentIdAsync(int departmentId)
		{
			var categories = (from doc in await GetAllDocumentsByDepartmentIdAsync(departmentId)
				select doc.Category).Distinct().ToList();

			return categories;
		}

		public async Task<Document> GetDocumentByIdAsync(int documentId)
		{
			return await _documentRepository.GetByIdAsync(documentId);
		}

		public async Task<bool> DeleteDocumentAsync(Document document, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _documentRepository.DeleteAsync(document, cancellationToken);
		}

		public async Task<DocumentCategory> SaveDocumentCategoryAsync(DocumentCategory category, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _documentCategoriesRepository.SaveOrUpdateAsync(category, cancellationToken);
		}

		public async Task<DocumentCategory> GetDocumentCategoryByIdAsync(string categoryId)
		{
			return await _documentCategoriesRepository.GetByIdAsync(categoryId);
		}

		public async Task<bool> DeleteDocumentCategoryAsync(DocumentCategory category, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _documentCategoriesRepository.DeleteAsync(category, cancellationToken);
		}

		public async Task<List<DocumentCategory>> GetAllCategoriesByDepartmentIdAsync(int departmentId)
		{
			var categories = await _documentCategoriesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (categories != null)
				return categories.ToList();

			return new List<DocumentCategory>();
		}

		public async Task<bool> DoesDocumentCategoryAlreadyExistAsync(int departmentId, string documentCategoryText)
		{
			var categories = await _documentCategoriesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (categories == null)
				return false;

			return categories.Any(x => x.Name == documentCategoryText.Trim());
		}
	}
}
