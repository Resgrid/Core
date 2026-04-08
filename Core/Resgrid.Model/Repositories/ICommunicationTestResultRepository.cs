using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICommunicationTestResultRepository : IRepository<CommunicationTestResult>
	{
		Task<IEnumerable<CommunicationTestResult>> GetResultsByRunIdAsync(Guid communicationTestRunId);
		Task<CommunicationTestResult> GetResultByResponseTokenAsync(string responseToken);
	}
}
