using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDocumentDbRepository
	/// </summary>
	public interface IDocumentDbRepository
	{
		/// <summary>
		/// Updates the configured document database schema and indexes.
		/// </summary>
		/// <returns>If the operation was successful</returns>
		Task<bool> UpdateDocumentDatabaseAsync();
	}
}
