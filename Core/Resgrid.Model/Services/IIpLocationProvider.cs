using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	public interface IIpLocationProvider
	{
		Task<IpLocationResult> GetApproximateLocationAsync(string ipAddress,
			CancellationToken cancellationToken = default);
	}
}
