using System;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Listeners
{
	public sealed class UnavailableTrackingListenerFactory : ITrackingListenerFactory
	{
		public bool Supports(TrackingListenerDefinition definition)
		{
			return false;
		}

		public ITrackingListener Create(TrackingListenerDefinition definition)
		{
			throw new InvalidOperationException(
				$"No socket listener implementation is registered for {definition}.");
		}
	}
}
