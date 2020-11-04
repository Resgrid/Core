using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ILogUnitsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.LogUnit}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.LogUnit}" />
	public interface ILogUnitsRepository: IRepository<LogUnit>
	{
		/// <summary>
		/// Gets the logs by log identifier asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;LogUser&gt;&gt;.</returns>
		Task<IEnumerable<LogUnit>> GetLogsByLogIdAsync(int logId);
	}
}
