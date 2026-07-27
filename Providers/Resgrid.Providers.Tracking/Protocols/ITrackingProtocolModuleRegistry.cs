using System.Collections.Generic;

namespace Resgrid.Providers.Tracking.Protocols
{
	public interface ITrackingProtocolModuleRegistry
	{
		IReadOnlyCollection<ITrackingProtocolModule> Modules { get; }

		bool TryResolve(
			string protocolKey,
			TrackingSocketTransport transport,
			out ITrackingProtocolModule module);

		ITrackingProtocolModule Resolve(
			string protocolKey,
			TrackingSocketTransport transport);
	}
}
