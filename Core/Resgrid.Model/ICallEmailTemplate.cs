using System.Collections.Generic;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	public interface ICallEmailTemplate
	{
		Call GenerateCall(CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority);
	}
}
