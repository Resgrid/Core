using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICommandsService
	{
		/// <summary>
		/// Gets all commands for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CommandDefinition&gt;&gt;.</returns>
		Task<List<CommandDefinition>> GetAllCommandsForDepartmentAsync(int departmentId);
		/// <summary>
		/// Saves the specified command.
		/// </summary>
		/// <param name="command">The command.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CommandDefinition&gt;.</returns>
		Task<CommandDefinition> Save(CommandDefinition command, CancellationToken cancellationToken = default(CancellationToken));
	}
}
