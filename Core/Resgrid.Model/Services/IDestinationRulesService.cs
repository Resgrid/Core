using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Why a destination rule was not saved (<see cref="IDestinationRulesService.SaveRuleAsync"/>).</summary>
	public enum DestinationRuleSaveErrors
	{
		None = 0,
		NameRequired = 1,
		NameTooLong = 2,
		UnknownCallType = 3,
		UnknownPriority = 4,
		UnknownUnitType = 5,
		UnknownDestinationType = 6,
		UnknownCapability = 7,

		/// <summary>The rule names no destination type and no capability, so it would change nothing.</summary>
		NothingRequired = 8,

		NotFound = 9
	}

	/// <summary>The outcome of saving a destination rule.</summary>
	public class DestinationRuleSaveResult
	{
		public DestinationRule Rule { get; set; }

		public DestinationRuleSaveErrors Error { get; set; }

		public bool Success => Error == DestinationRuleSaveErrors.None && Rule != null;
	}

	/// <summary>
	/// A department's destination rules (<see cref="DestinationRule"/>, M0317): what a call's or a unit's destination must
	/// offer. <see cref="INearestDestinationService.SuggestDestinationsAsync"/> applies them.
	/// </summary>
	public interface IDestinationRulesService
	{
		/// <summary>The department's rules, most specific first.</summary>
		Task<List<DestinationRule>> GetRulesForDepartmentAsync(int departmentId);

		/// <summary>One of the department's rules, or null.</summary>
		Task<DestinationRule> GetRuleByIdAsync(int departmentId, string destinationRuleId);

		/// <summary>
		/// Adds (blank id) or updates one of the department's rules. Every id must be the department's own: the call type, the
		/// priority (an active one), the unit type, destination POI types and capabilities; lists are de-duplicated and a
		/// capability both required and preferred stays required only.
		/// </summary>
		Task<DestinationRuleSaveResult> SaveRuleAsync(int departmentId, DestinationRule rule, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Deletes one of the department's rules; false when it is not found.</summary>
		Task<bool> DeleteRuleAsync(int departmentId, string destinationRuleId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// What a destination must be for the context, from the department's rules (<see cref="DestinationRuleResolver.Resolve"/>),
		/// dropping ids of types and capabilities that no longer exist.
		/// </summary>
		Task<DestinationRequirements> ResolveAsync(int departmentId, DestinationContext context);

		/// <summary>
		/// The context for a call and (optionally) the unit travelling: the call type by id (else by its name, case-insensitive),
		/// the priority as given, and the unit's type by its <see cref="Unit.Type"/> name. A unit of another department is ignored.
		/// </summary>
		Task<DestinationContext> BuildContextAsync(int departmentId, int? callTypeId, string callTypeName, int? callPriority, int? unitId);
	}
}
