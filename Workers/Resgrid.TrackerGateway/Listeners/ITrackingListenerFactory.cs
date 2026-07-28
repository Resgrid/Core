using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Listeners
{
	public interface ITrackingListenerFactory
	{
		bool Supports(TrackingListenerDefinition definition);
		ITrackingListener Create(TrackingListenerDefinition definition);
	}
}
