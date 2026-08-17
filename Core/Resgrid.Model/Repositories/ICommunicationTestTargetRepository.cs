using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICommunicationTestTargetRepository : IRepository<CommunicationTestTarget>
	{
		Task<IEnumerable<CommunicationTestTarget>> GetTargetsByTestIdAsync(Guid communicationTestId);
	}
}
