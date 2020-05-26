using System;
using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface ICallsRepository : IRepository<Call>
	{
		void MarkCallDispatchesAsSent(int callId, List<Guid> usersToMark);
		void CleanUpCallDispatchAudio();
		List<Call> GetActiveCallsByDepartment(int departmentId);
		List<CallProtocol> GetCallProtocolsByCallId(int callId);
	}
}
