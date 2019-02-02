using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ICommandsService
	{
		List<CommandDefinition> GetAllCommandsForDepartment(int departmentId);
		CommandDefinition Save(CommandDefinition command);
	}
}
