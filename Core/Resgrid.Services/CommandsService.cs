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
	}
}
