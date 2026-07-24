using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUnitTrackingDevicesRepository : IRepository<UnitTrackingDevice>
	{
		Task<IEnumerable<UnitTrackingDevice>> GetAllByUnitIdAsync(int departmentId, int unitId);
		Task<UnitTrackingDevice> GetByProtocolIdentifierAsync(string protocolKey, string deviceIdentifier);
	}
}
