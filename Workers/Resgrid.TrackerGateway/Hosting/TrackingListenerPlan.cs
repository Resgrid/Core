using System;
using System.Collections.Generic;

namespace Resgrid.TrackerGateway.Hosting
{
	public sealed class TrackingListenerPlan
	{
		public TrackingListenerPlan(IEnumerable<TrackingListenerDefinition> listeners)
		{
			Listeners = new List<TrackingListenerDefinition>(
				listeners ?? Array.Empty<TrackingListenerDefinition>())
				.AsReadOnly();
		}

		public IReadOnlyCollection<TrackingListenerDefinition> Listeners { get; }
	}
}
