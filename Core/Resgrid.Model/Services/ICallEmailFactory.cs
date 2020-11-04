using System.Collections.Generic;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Services
{
	public interface ICallEmailFactory
	{
		Call GenerateCallFromEmailText(CallEmailTypes type, CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities);
	}
}
