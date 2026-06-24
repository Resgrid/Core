using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Aggregates resources (units + personnel) that an incident commander can assign to lanes, unioning the
	/// own department with linked (mutual-aid) departments per the <c>DepartmentLink</c> share flags (§3.9).
	/// </summary>
	public interface IMutualAidService
	{
		/// <summary>
		/// Returns own-department resources plus those shared toward this department by accepted, enabled
		/// mutual-aid links (gated by the linked department's share-units / share-personnel flags), color-coded.
		/// </summary>
		Task<List<AssignableResource>> GetAssignableResourcesForIncidentAsync(int departmentId);
	}
}
