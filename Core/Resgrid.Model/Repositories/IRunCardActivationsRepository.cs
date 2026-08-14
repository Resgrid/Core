using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardActivationsRepository : IRepository<RunCardActivation>
	{
		Task<IEnumerable<RunCardActivation>> GetActivationsByCallIdAsync(int callId);
	}
}
