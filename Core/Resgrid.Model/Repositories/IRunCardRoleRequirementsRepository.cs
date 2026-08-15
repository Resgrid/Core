using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardRoleRequirementsRepository : IRepository<RunCardRoleRequirement>
	{
		Task<IEnumerable<RunCardRoleRequirement>> GetRoleRequirementsByRunCardIdAsync(int runCardId);
	}
}
