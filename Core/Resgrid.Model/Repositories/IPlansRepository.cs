using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IPlansRepository : IRepository<Plan>
	{
		Plan GetPlanByExternalId(string externalId);
		Task<Plan> GetPlanByExternalIdAsync(string externalId);
		Task<Plan> GetPlanByIdAsync(int planId);
	}
}
