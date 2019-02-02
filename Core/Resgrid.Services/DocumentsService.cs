using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class DocumentsService : IDocumentsService
	{
		private readonly IGenericDataRepository<Document> _documentRepository;
		private readonly IEventAggregator _eventAggregator;

		public DocumentsService(IGenericDataRepository<Document> documentRepository, IEventAggregator eventAggregator)
		{
			_documentRepository = documentRepository;
			_eventAggregator = eventAggregator;
		}

		public List<Document> GetAllDocumentsByDepartmentId(int departmentId)
		{
			return _documentRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public List<Document> GetFilteredDocumentsByDepartmentId(int departmentId, string type, string category)
		{
			var result = _documentRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();

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

		public Document SaveDocument(Document document)
		{
			_documentRepository.SaveOrUpdate(document);
			
			return document;
		}

		public List<string> GetDistinctCategoriesByDepartmentId(int departmentId)
		{
			var categories = (from doc in _documentRepository.GetAll()
				where doc.DepartmentId == departmentId
				select doc.Category).Distinct().ToList();

			return categories;
		}

		public Document GetDocumentById(int documentId)
		{
			return _documentRepository.GetAll().FirstOrDefault(x => x.DocumentId == documentId);
		}

		public void DeleteDocument(Document document)
		{
			_documentRepository.DeleteOnSubmit(document);
		}
	}
}