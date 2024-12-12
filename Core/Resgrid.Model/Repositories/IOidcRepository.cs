using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IOidcRepository
	/// </summary>
	public interface IOidcRepository
	{
		/// <summary>
		/// Updates the Oidc Database
		/// </summary>
		/// <returns>If the operation was successful</returns>
		Task<bool> UpdateOidcDatabaseAsync();
	}
}
