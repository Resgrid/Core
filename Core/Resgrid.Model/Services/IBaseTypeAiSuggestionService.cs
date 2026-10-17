using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Enhanced AI base-type suggestions: a meaning for department list entries that have none yet (BaseType below zero) and whose
	/// name the keyword suggester (<see cref="BaseTypeSuggestions"/>) has no rule for; where it has one, it wins and the entry is
	/// never sent. Only list names leave Resgrid, as a DepartmentConfiguration decision request. A suggestion is only ever shown:
	/// nothing is applied until the administrator picks it and saves. Each result maps an entry id to its suggested base type;
	/// entries without a confident suggestion are absent, and a department that is not entitled, an unconfigured decision model or
	/// any model failure gives an empty result rather than an error.
	/// </summary>
	public interface IBaseTypeAiSuggestionService
	{
		/// <summary>Keyed by CallTypeId; values are <see cref="CallTypeClasses"/>.</summary>
		Task<IReadOnlyDictionary<int, int>> SuggestCallTypeClassesAsync(int departmentId, IEnumerable<CallType> callTypes, CancellationToken cancellationToken);

		/// <summary>Keyed by UnitTypeId; values are <see cref="UnitTypeClasses"/>.</summary>
		Task<IReadOnlyDictionary<int, int>> SuggestUnitTypeClassesAsync(int departmentId, IEnumerable<UnitType> unitTypes, CancellationToken cancellationToken);

		/// <summary>Keyed by DepartmentCallPriorityId; values are <see cref="CallPriority"/>.</summary>
		Task<IReadOnlyDictionary<int, int>> SuggestCallPrioritiesAsync(int departmentId, IEnumerable<DepartmentCallPriority> priorities, CancellationToken cancellationToken);

		/// <summary>
		/// The set's active options, keyed by CustomStateDetailId; values are <see cref="ActionBaseTypes"/> for unit and personnel
		/// status sets and <see cref="StaffingBaseTypes"/> for staffing sets.
		/// </summary>
		Task<IReadOnlyDictionary<int, int>> SuggestStatusesAsync(int departmentId, CustomState state, CancellationToken cancellationToken);
	}
}
