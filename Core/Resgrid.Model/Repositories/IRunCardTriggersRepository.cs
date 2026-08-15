using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardTriggersRepository : IRepository<RunCardTrigger>
	{
		Task<IEnumerable<RunCardTrigger>> GetTriggersByRunCardIdAsync(int runCardId);

		Task<IEnumerable<RunCardTrigger>> GetTriggersByDepartmentIdAsync(int departmentId);
	}
}
