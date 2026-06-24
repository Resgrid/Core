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

		/// <summary>
		/// Gets a single command definition by its identifier.
		/// </summary>
		/// <param name="commandDefinitionId">The command definition identifier.</param>
		/// <returns>Task&lt;CommandDefinition&gt;.</returns>
		Task<CommandDefinition> GetCommandByIdAsync(int commandDefinitionId);

		/// <summary>
		/// Resolves the command definition (template) to use for a given call type, falling back to the
		/// department's "Any Call Type" definition (CallTypeId == null) when no type-specific one exists.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="callTypeId">The call type identifier, or null for the "Any Call Type" template.</param>
		/// <returns>Task&lt;CommandDefinition&gt;.</returns>
		Task<CommandDefinition> GetCommandForCallTypeAsync(int departmentId, int? callTypeId);

		/// <summary>
		/// Deletes the specified command definition.
		/// </summary>
		/// <param name="command">The command definition.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteAsync(CommandDefinition command, CancellationToken cancellationToken = default(CancellationToken));
	}
}
