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

		public CommandsService(ICommandDefinitionRepository commandDefinitionRepository)
		{
			_commandDefinitionRepository = commandDefinitionRepository;
		}

		public async Task<List<CommandDefinition>> GetAllCommandsForDepartmentAsync(int departmentId)
		{
			var items = await _commandDefinitionRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<CommandDefinition>();
		}

		public async Task<CommandDefinition> Save(CommandDefinition command, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _commandDefinitionRepository.SaveOrUpdateAsync(command, cancellationToken);
		}

		public async Task<CommandDefinition> GetCommandByIdAsync(int commandDefinitionId)
		{
			return await _commandDefinitionRepository.GetByIdAsync(commandDefinitionId);
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
					return match;
			}

			// Fall back to the department's "Any Call Type" definition (CallTypeId == null)
			return list.FirstOrDefault(x => !x.CallTypeId.HasValue);
		}

		public async Task<bool> DeleteAsync(CommandDefinition command, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _commandDefinitionRepository.DeleteAsync(command, cancellationToken);
		}
	}
}
