using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Road closures (RoadClosures, M0314).</summary>
	public interface IRoadClosuresRepository : IRepository<RoadClosure>
	{
		Task<List<RoadClosure>> GetForDepartmentAsync(int departmentId);

		Task<RoadClosure> GetByIdForDepartmentAsync(string roadClosureId, int departmentId);
	}
}
