using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUnitTrackingCredentialsRepository : IRepository<UnitTrackingCredential>
	{
		Task<IEnumerable<UnitTrackingCredential>> GetAllByDeviceIdAsync(string unitTrackingDeviceId);
		Task<UnitTrackingCredential> GetBySecretHashAsync(string secretHash);
		Task<int> RevokeAllByDeviceIdAsync(string unitTrackingDeviceId, DateTime revokedOn);
	}
}
