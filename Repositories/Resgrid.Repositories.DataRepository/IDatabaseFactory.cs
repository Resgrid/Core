using Resgrid.Repositories.DataRepository.Contexts;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Defines the methods that are required for a DatabaseFactory instance.
	/// </summary>
	public interface IDatabaseFactory
	{
		/// <summary>
		/// Returns a concrete instance of the data context
		/// </summary>
		/// <returns>DbContext (DataContext)</returns>
		DataContext Get();
	}
}
