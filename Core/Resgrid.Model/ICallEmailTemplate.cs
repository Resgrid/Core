using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;

namespace Resgrid.Model
{
	public interface ICallEmailTemplate
	{
		Task<Call> GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls,
			List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes, IGeoLocationProvider geolocationProvider);
	}
}
