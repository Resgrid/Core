using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICommunicationTestRepository : IRepository<CommunicationTest>
	{
		Task<IEnumerable<CommunicationTest>> GetActiveTestsForScheduleTypeAsync(int scheduleType);
	}
}
