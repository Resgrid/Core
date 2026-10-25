using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Builds the department's <see cref="MapIconResolver"/>: the map icon of every call, unit and member.</summary>
	public interface IMapIconsService
	{
		/// <summary>
		/// The department's resolver, from its call types, priorities, unit types, status sets, roles and uploaded status and
		/// priority icons. Pass <paramref name="latestUnitStates"/> when members On Unit should take the style of the unit
		/// they ride (the map already holds them).
		/// </summary>
		Task<MapIconResolver> GetResolverAsync(int departmentId, IEnumerable<UnitState> latestUnitStates = null);

		/// <summary>
		/// Every icon the department's map can show (<see cref="MapIconLegendBuilder.Build"/>), named with the department's own
		/// call types, priorities, unit types, status sets, statuses and roles. The "none" rows take the host's names.
		/// </summary>
		Task<MapIconLegend> GetLegendAsync(int departmentId, string noCallTypeName, string noUnitTypeName, string noRoleName);
	}
}
