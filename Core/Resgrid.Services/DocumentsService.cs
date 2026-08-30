using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
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

		public DocumentsService(IDocumentRepository documentRepository, IDocumentCategoriesRepository documentCategoriesRepository,
			IEventAggregator eventAggregator, Lazy<IProtectedWriteService> protectedWriteService)
		{
			_protectedWriteService = protectedWriteService;
			_documentRepository = documentRepository;
			_documentCategoriesRepository = documentCategoriesRepository;
			_eventAggregator = eventAggregator;
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

			// ADP write safety net (plan 4.2/19.2, catalog v9).
			// An UPDATE already has its identity, so it is enveloped BEFORE the save and no plaintext
			// version of a cataloged field ever reaches the table - the same split CertificationService
			// uses. Only an INSERT has to be persisted first, because the AAD row key IS the identity
			// pk and cannot be bound until the database assigns it (plan 4.2/19.2). Fails closed
			// either way.
			var isExistingRow = document != null && document.DocumentId > 0;

			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareDocumentWriteAsync(
					document.DepartmentId, document, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); document {document.DocumentId} was NOT saved.");
			}

			var saved = await _documentRepository.SaveOrUpdateAsync(document, cancellationToken);

			if (!isExistingRow)
			{
				var protectedWrite = await _protectedWriteService.Value.PrepareDocumentWriteAsync(saved.DepartmentId,
					saved, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); document {saved.DocumentId} has transient plaintext pending re-encryption.");
				if (protectedWrite.Changed)
					saved = await _documentRepository.SaveOrUpdateAsync(saved, cancellationToken);
			}

			return saved;
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
