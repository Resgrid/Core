using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDocumentDbRepository
	/// </summary>
	public interface IDocumentDbRepository
	{
		/// <summary>
		/// Updates the Postgres document database schema.
		/// </summary>
		/// <returns>If the operation was successful</returns>
		Task<bool> UpdateDocumentDatabaseAsync();
	}
}
