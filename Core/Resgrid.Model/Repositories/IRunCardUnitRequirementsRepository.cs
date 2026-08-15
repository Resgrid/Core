using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardUnitRequirementsRepository : IRepository<RunCardUnitRequirement>
	{
		Task<IEnumerable<RunCardUnitRequirement>> GetUnitRequirementsByRunCardIdAsync(int runCardId);
	}
}
