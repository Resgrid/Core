using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Model.Services
{
	public interface ICallEmailFactory
	{
		Task<Call> GenerateCallFromEmailText(CallEmailTypes type, CallEmail email, string managingUser, List<IdentityUser> users,
			Department department, List<Call> activeCalls, List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities,
			List<CallType> callTypes, IGeoLocationProvider geolocationProvider);

		/// <summary>
		/// The email as the format reads it (cad-email-call-updates-plan.md §4.2): always a new, unsaved call, the format's key for the
		/// incident and the fields it read. Null when the format is unknown or failed (logged); a result without a call when the format
		/// found none. <paramref name="labeledFields"/> is only read by <see cref="CallEmailTypes.LabeledFields"/>.
		/// </summary>
		Task<CallEmailParseResult> ParseCallEmailAsync(CallEmailTypes type, CallEmail email, string managingUser, List<IdentityUser> users,
			Department department, List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes,
			IGeoLocationProvider geolocationProvider, EmailLabeledFieldsConfig labeledFields);
	}
}
