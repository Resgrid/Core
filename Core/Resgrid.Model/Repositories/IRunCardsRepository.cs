using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardsRepository : IRepository<RunCard>
	{
		/// <summary>Most recent dispatch time per unit for the department (rest-period input).</summary>
		Task<IEnumerable<UnitLastDispatchTime>> GetLastUnitDispatchTimesByDepartmentAsync(int departmentId);

		/// <summary>Most recent dispatch time per user for the department (rest-period input).</summary>
		Task<IEnumerable<UserLastDispatchTime>> GetLastUserDispatchTimesByDepartmentAsync(int departmentId);
	}
}
