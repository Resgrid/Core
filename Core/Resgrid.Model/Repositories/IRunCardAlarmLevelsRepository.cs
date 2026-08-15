using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRunCardAlarmLevelsRepository : IRepository<RunCardAlarmLevel>
	{
		Task<IEnumerable<RunCardAlarmLevel>> GetAlarmLevelsByRunCardIdAsync(int runCardId);
	}
}
