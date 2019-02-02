using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IDocumentsService
	{
		List<Document> GetAllDocumentsByDepartmentId(int departmentId);
		Document SaveDocument(Document document);
		Document GetDocumentById(int documentId);
		List<string> GetDistinctCategoriesByDepartmentId(int departmentId);
		void DeleteDocument(Document document);
		List<Document> GetFilteredDocumentsByDepartmentId(int departmentId, string type, string category);
	}
}