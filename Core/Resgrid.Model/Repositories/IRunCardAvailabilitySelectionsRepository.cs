using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardAvailabilitySelectionsRepository : IRepository<RunCardAvailabilitySelection>
	{
		Task<IEnumerable<RunCardAvailabilitySelection>> GetSelectionsByRunCardIdAsync(int runCardId);
	}
}
