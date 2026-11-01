using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDocumentRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Document}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Document}" />
	public interface IDocumentRepository: IRepository<Document>
	{
		/// <summary>
		/// The department's newest documents (by AddedOn, then id), metadata only: the file bytes and the ADP-catalogued
		/// description and file name are not selected, so the returned rows leave them null. Admins-only documents are left
		/// out unless <paramref name="includeAdminsOnly"/>; <paramref name="category"/> (exact, when given) narrows the list.
		/// </summary>
		Task<IEnumerable<Document>> GetRecentByDepartmentIdAsync(int departmentId, int take, bool includeAdminsOnly, string category = null,
			CancellationToken cancellationToken = default);
	}
}
