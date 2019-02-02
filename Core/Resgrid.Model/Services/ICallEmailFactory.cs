using System;
using System.Collections.Generic;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model.Services
{
	public interface ICallEmailFactory
	{
		Call GenerateCallFromEmailText(CallEmailTypes type, CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority);
	}
}
