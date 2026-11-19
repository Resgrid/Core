using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>A department's destination rules (DestinationRules, M0317). Rows are read by department (GetAllByDepartmentIdAsync).</summary>
	public interface IDestinationRulesRepository : IRepository<DestinationRule>
	{
		/// <summary>
		/// Writes an existing rule's name, conditions, id lists and updated stamp, matched on its id and department. False when
		/// no row matched (the rule was deleted meanwhile), so an edit is never reported as saved when nothing was stored.
		/// </summary>
		Task<bool> UpdateRuleAsync(DestinationRule rule, CancellationToken cancellationToken = default);
	}
}
