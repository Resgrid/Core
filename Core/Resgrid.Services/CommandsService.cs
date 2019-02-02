using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CommandsService: ICommandsService
	{
		private readonly IGenericDataRepository<CommandDefinition> _commandDefinitionRepository;

		public CommandsService(IGenericDataRepository<CommandDefinition> commandDefinitionRepository)
		{
			_commandDefinitionRepository = commandDefinitionRepository;
		}

		public List<CommandDefinition> GetAllCommandsForDepartment(int departmentId)
		{
			return _commandDefinitionRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public CommandDefinition Save(CommandDefinition command)
		{
			_commandDefinitionRepository.SaveOrUpdate(command);

			return command;
		}
	}
}
