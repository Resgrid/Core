using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Tracking;

namespace Resgrid.Model.Services
{
	public interface IUnitTrackingIngressService
	{
		Task<TrackingIngressResult> AcceptAsync(
			AuthenticatedTrackingSource source,
			IReadOnlyCollection<CanonicalTrackingPosition> positions,
			CancellationToken cancellationToken = default);

		Task<TrackingIngressResult> AcceptHeartbeatAsync(
			AuthenticatedTrackingSource source,
			DateTime receivedOnUtc,
			CancellationToken cancellationToken = default);
	}
}
