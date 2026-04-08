using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICommunicationTestRunRepository : IRepository<CommunicationTestRun>
	{
		Task<IEnumerable<CommunicationTestRun>> GetRunsByTestIdAsync(Guid communicationTestId);
		Task<CommunicationTestRun> GetRunByRunCodeAsync(string runCode);
		Task<IEnumerable<CommunicationTestRun>> GetOpenRunsAsync();
	}
}
