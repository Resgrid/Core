using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IDocumentsService
	{
		/// <summary>
		/// Gets all documents by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Document&gt;&gt;.</returns>
		Task<List<Document>> GetAllDocumentsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the filtered documents by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <param name="category">The category.</param>
		/// <returns>Task&lt;List&lt;Document&gt;&gt;.</returns>
		Task<List<Document>> GetFilteredDocumentsByDepartmentIdAsync(int departmentId, string type, string category);

		/// <summary>
		/// Saves the document asynchronous.
		/// </summary>
		/// <param name="document">The document.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Document&gt;.</returns>
		Task<Document> SaveDocumentAsync(Document document, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the distinct categories by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;System.String&gt;&gt;.</returns>
		Task<List<string>> GetDistinctCategoriesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the document by identifier asynchronous.
		/// </summary>
		/// <param name="documentId">The document identifier.</param>
		/// <returns>Task&lt;Document&gt;.</returns>
		Task<Document> GetDocumentByIdAsync(int documentId);

		/// <summary>
		/// Deletes the document asynchronous.
		/// </summary>
		/// <param name="document">The document.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteDocumentAsync(Document document, CancellationToken cancellationToken = default(CancellationToken));

		Task<DocumentCategory> SaveDocumentCategoryAsync(DocumentCategory category, CancellationToken cancellationToken = default(CancellationToken));

		Task<DocumentCategory> GetDocumentCategoryByIdAsync(string categoryId);

		Task<bool> DeleteDocumentCategoryAsync(DocumentCategory category, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<DocumentCategory>> GetAllCategoriesByDepartmentIdAsync(int departmentId);

		Task<bool> DoesDocumentCategoryAlreadyExistAsync(int departmentId, string documentCategoryText);
	}
}
