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
	}
}
