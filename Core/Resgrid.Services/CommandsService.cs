using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CommandsService: ICommandsService
	{
		private readonly ICommandDefinitionRepository _commandDefinitionRepository;
		private readonly ICommandDefinitionRoleRepository _commandDefinitionRoleRepository;
		private readonly ICommandDefinitionRoleUnitTypeRepository _commandDefinitionRoleUnitTypeRepository;
		private readonly ICommandDefinitionRolePersonnelRoleRepository _commandDefinitionRolePersonnelRoleRepository;

		public CommandsService(ICommandDefinitionRepository commandDefinitionRepository,
			ICommandDefinitionRoleRepository commandDefinitionRoleRepository,
			ICommandDefinitionRoleUnitTypeRepository commandDefinitionRoleUnitTypeRepository,
			ICommandDefinitionRolePersonnelRoleRepository commandDefinitionRolePersonnelRoleRepository)
		{
			_commandDefinitionRepository = commandDefinitionRepository;
			_commandDefinitionRoleRepository = commandDefinitionRoleRepository;
			_commandDefinitionRoleUnitTypeRepository = commandDefinitionRoleUnitTypeRepository;
			_commandDefinitionRolePersonnelRoleRepository = commandDefinitionRolePersonnelRoleRepository;
		}

		public async Task<List<CommandDefinition>> GetAllCommandsForDepartmentAsync(int departmentId)
		{
			var items = await _commandDefinitionRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
			{
				var list = items.ToList();

				foreach (var command in list)
					await HydrateAssignmentsAsync(command);

				return list;
			}

			return new List<CommandDefinition>();
		}

		public async Task<CommandDefinition> Save(CommandDefinition command, CancellationToken cancellationToken = default(CancellationToken))
		{
			// SaveOrUpdateAsync only persists the parent row (Assignments is in IgnoredProperties),
			// so the lanes are reconciled explicitly below.
			var incomingAssignments = command.Assignments;
			var saved = await _commandDefinitionRepository.SaveOrUpdateAsync(command, cancellationToken);

			// null = caller didn't load/touch the lanes; leave them alone. An empty list clears them.
			if (incomingAssignments != null)
			{
				var existing = (await _commandDefinitionRoleRepository.GetRolesByCommandDefinitionIdAsync(saved.CommandDefinitionId))?.ToList()
					?? new List<CommandDefinitionRole>();

				var incomingIds = incomingAssignments
					.Where(x => x.CommandDefinitionRoleId > 0)
					.Select(x => x.CommandDefinitionRoleId)
					.ToHashSet();

				foreach (var removed in existing.Where(x => !incomingIds.Contains(x.CommandDefinitionRoleId)))
					await DeleteRoleWithRequirementsAsync(removed, cancellationToken);

				var savedAssignments = new List<CommandDefinitionRole>();
				foreach (var assignment in incomingAssignments)
				{
					// A role id from another definition can't be hijacked to move/overwrite that row.
					if (assignment.CommandDefinitionRoleId > 0 && existing.All(x => x.CommandDefinitionRoleId != assignment.CommandDefinitionRoleId))
						assignment.CommandDefinitionRoleId = 0;

					var incomingUnitTypes = assignment.RequiredUnitTypes;
					var incomingRoles = assignment.RequiredRoles;

					assignment.CommandDefinitionId = saved.CommandDefinitionId;
					var savedAssignment = await _commandDefinitionRoleRepository.SaveOrUpdateAsync(assignment, cancellationToken);

					await ReconcileRoleRequirementsAsync(savedAssignment, incomingUnitTypes, incomingRoles, cancellationToken);
					savedAssignments.Add(savedAssignment);
				}

				saved.Assignments = savedAssignments.OrderBy(x => x.SortOrder).ToList();
			}

			return saved;
		}

		public async Task<CommandDefinition> GetCommandByIdAsync(int commandDefinitionId)
		{
			var command = await _commandDefinitionRepository.GetByIdAsync(commandDefinitionId);
			return await HydrateAssignmentsAsync(command);
		}

		public async Task<CommandDefinition> GetCommandForCallTypeAsync(int departmentId, int? callTypeId)
		{
			var items = await _commandDefinitionRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items == null || !items.Any())
				return null;

			var list = items.ToList();

			if (callTypeId.HasValue)
			{
				var match = list.FirstOrDefault(x => x.CallTypeId == callTypeId.Value);
				if (match != null)
					return await HydrateAssignmentsAsync(match);
			}

			// Fall back to the department's "Any Call Type" definition (CallTypeId == null)
			return await HydrateAssignmentsAsync(list.FirstOrDefault(x => !x.CallTypeId.HasValue));
		}

		public async Task<CommandDefinitionRole> GetRoleWithRequirementsAsync(int commandDefinitionRoleId)
		{
			var role = await _commandDefinitionRoleRepository.GetByIdAsync(commandDefinitionRoleId);
			if (role == null)
				return null;

			var unitTypes = await _commandDefinitionRoleUnitTypeRepository.GetUnitTypesByRoleIdAsync(commandDefinitionRoleId);
			var personnelRoles = await _commandDefinitionRolePersonnelRoleRepository.GetPersonnelRolesByRoleIdAsync(commandDefinitionRoleId);

			role.RequiredUnitTypes = unitTypes?.ToList() ?? new List<CommandDefinitionRoleUnitType>();
			role.RequiredRoles = personnelRoles?.ToList() ?? new List<CommandDefinitionRolePersonnelRole>();

			return role;
		}

		public async Task<bool> DeleteAsync(CommandDefinition command, CancellationToken cancellationToken = default(CancellationToken))
		{
			var roles = await _commandDefinitionRoleRepository.GetRolesByCommandDefinitionIdAsync(command.CommandDefinitionId);
			if (roles != null)
			{
				foreach (var role in roles)
					await DeleteRoleWithRequirementsAsync(role, cancellationToken);
			}

			return await _commandDefinitionRepository.DeleteAsync(command, cancellationToken);
		}

		private async Task<CommandDefinition> HydrateAssignmentsAsync(CommandDefinition command)
		{
			if (command == null)
				return null;

			var roles = await _commandDefinitionRoleRepository.GetRolesByCommandDefinitionIdAsync(command.CommandDefinitionId);
			command.Assignments = roles?.OrderBy(x => x.SortOrder).ToList() ?? new List<CommandDefinitionRole>();

			if (command.Assignments.Any())
			{
				var unitTypes = (await _commandDefinitionRoleUnitTypeRepository.GetUnitTypesByCommandDefinitionIdAsync(command.CommandDefinitionId))?.ToList()
					?? new List<CommandDefinitionRoleUnitType>();
				var personnelRoles = (await _commandDefinitionRolePersonnelRoleRepository.GetPersonnelRolesByCommandDefinitionIdAsync(command.CommandDefinitionId))?.ToList()
					?? new List<CommandDefinitionRolePersonnelRole>();

				foreach (var role in command.Assignments)
				{
					role.RequiredUnitTypes = unitTypes.Where(x => x.CommandDefinitionRoleId == role.CommandDefinitionRoleId).ToList();
					role.RequiredRoles = personnelRoles.Where(x => x.CommandDefinitionRoleId == role.CommandDefinitionRoleId).ToList();
				}
			}

			return command;
		}

		/// <summary>
		/// Full-replace reconcile of a lane's requirement rows. A null incoming collection means the caller
		/// didn't touch that requirement set (leave it alone); an empty collection clears it.
		/// </summary>
		private async Task ReconcileRoleRequirementsAsync(CommandDefinitionRole savedRole,
			ICollection<CommandDefinitionRoleUnitType> incomingUnitTypes,
			ICollection<CommandDefinitionRolePersonnelRole> incomingRoles,
			CancellationToken cancellationToken)
		{
			if (incomingUnitTypes != null)
			{
				var existing = await _commandDefinitionRoleUnitTypeRepository.GetUnitTypesByRoleIdAsync(savedRole.CommandDefinitionRoleId);
				if (existing != null)
				{
					foreach (var row in existing)
						await _commandDefinitionRoleUnitTypeRepository.DeleteAsync(row, cancellationToken);
				}

				var savedUnitTypes = new List<CommandDefinitionRoleUnitType>();
				foreach (var unitTypeId in incomingUnitTypes.Select(x => x.UnitTypeId).Distinct())
				{
					savedUnitTypes.Add(await _commandDefinitionRoleUnitTypeRepository.SaveOrUpdateAsync(new CommandDefinitionRoleUnitType
					{
						CommandDefinitionRoleId = savedRole.CommandDefinitionRoleId,
						UnitTypeId = unitTypeId
					}, cancellationToken));
				}

				savedRole.RequiredUnitTypes = savedUnitTypes;
			}

			if (incomingRoles != null)
			{
				var existing = await _commandDefinitionRolePersonnelRoleRepository.GetPersonnelRolesByRoleIdAsync(savedRole.CommandDefinitionRoleId);
				if (existing != null)
				{
					foreach (var row in existing)
						await _commandDefinitionRolePersonnelRoleRepository.DeleteAsync(row, cancellationToken);
				}

				var savedRoles = new List<CommandDefinitionRolePersonnelRole>();
				foreach (var personnelRoleId in incomingRoles.Select(x => x.PersonnelRoleId).Distinct())
				{
					savedRoles.Add(await _commandDefinitionRolePersonnelRoleRepository.SaveOrUpdateAsync(new CommandDefinitionRolePersonnelRole
					{
						CommandDefinitionRoleId = savedRole.CommandDefinitionRoleId,
						PersonnelRoleId = personnelRoleId
					}, cancellationToken));
				}

				savedRole.RequiredRoles = savedRoles;
			}
		}

		private async Task DeleteRoleWithRequirementsAsync(CommandDefinitionRole role, CancellationToken cancellationToken)
		{
			var unitTypes = await _commandDefinitionRoleUnitTypeRepository.GetUnitTypesByRoleIdAsync(role.CommandDefinitionRoleId);
			if (unitTypes != null)
			{
				foreach (var row in unitTypes)
					await _commandDefinitionRoleUnitTypeRepository.DeleteAsync(row, cancellationToken);
			}

			var personnelRoles = await _commandDefinitionRolePersonnelRoleRepository.GetPersonnelRolesByRoleIdAsync(role.CommandDefinitionRoleId);
			if (personnelRoles != null)
			{
				foreach (var row in personnelRoles)
					await _commandDefinitionRolePersonnelRoleRepository.DeleteAsync(row, cancellationToken);
			}

			await _commandDefinitionRoleRepository.DeleteAsync(role, cancellationToken);
		}
	}
}
