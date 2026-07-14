using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICommandDefinitionRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CommandDefinition}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CommandDefinition}" />
	public interface ICommandDefinitionRepository: IRepository<CommandDefinition>
	{
	}

	/// <summary>
	/// Repository for the lanes/assignments (<see cref="CommandDefinitionRole"/>) of a command definition template.
	/// </summary>
	public interface ICommandDefinitionRoleRepository : IRepository<CommandDefinitionRole>
	{
		/// <summary>
		/// Gets all lanes/assignments for a command definition template.
		/// </summary>
		/// <param name="commandDefinitionId">The command definition identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CommandDefinitionRole&gt;&gt;.</returns>
		Task<IEnumerable<CommandDefinitionRole>> GetRolesByCommandDefinitionIdAsync(int commandDefinitionId);
	}

	/// <summary>
	/// Repository for a lane's required unit types (<see cref="CommandDefinitionRoleUnitType"/>).
	/// </summary>
	public interface ICommandDefinitionRoleUnitTypeRepository : IRepository<CommandDefinitionRoleUnitType>
	{
		/// <summary>
		/// Gets the required unit types for every lane of a command definition template.
		/// </summary>
		Task<IEnumerable<CommandDefinitionRoleUnitType>> GetUnitTypesByCommandDefinitionIdAsync(int commandDefinitionId);

		/// <summary>
		/// Gets the required unit types for a single lane.
		/// </summary>
		Task<IEnumerable<CommandDefinitionRoleUnitType>> GetUnitTypesByRoleIdAsync(int commandDefinitionRoleId);
	}

	/// <summary>
	/// Repository for a lane's required personnel roles (<see cref="CommandDefinitionRolePersonnelRole"/>).
	/// </summary>
	public interface ICommandDefinitionRolePersonnelRoleRepository : IRepository<CommandDefinitionRolePersonnelRole>
	{
		/// <summary>
		/// Gets the required personnel roles for every lane of a command definition template.
		/// </summary>
		Task<IEnumerable<CommandDefinitionRolePersonnelRole>> GetPersonnelRolesByCommandDefinitionIdAsync(int commandDefinitionId);

		/// <summary>
		/// Gets the required personnel roles for a single lane.
		/// </summary>
		Task<IEnumerable<CommandDefinitionRolePersonnelRole>> GetPersonnelRolesByRoleIdAsync(int commandDefinitionRoleId);
	}
}
