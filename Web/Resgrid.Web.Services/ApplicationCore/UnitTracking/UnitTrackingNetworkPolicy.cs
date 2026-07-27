using System.Net;
using Resgrid.Model.Tracking;

namespace Resgrid.Web.Services.ApplicationCore.UnitTracking
{
	public static class UnitTrackingNetworkPolicy
	{
		public static bool IsAllowed(IPAddress remoteAddress, string allowedSourceCidrs)
		{
			return UnitTrackingSourceNetworkPolicy.IsAllowed(
				remoteAddress,
				allowedSourceCidrs);
		}
	}
}
