using Resgrid.Model.Events;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IUnitLocationEventProvider
	{
		Task<bool> EnqueueUnitLocationEventAsync(UnitLocationEvent unitLocationEvent);
		Task<bool> EnqueueUnitLocationEventsAsync(
			IReadOnlyCollection<UnitLocationEvent> unitLocationEvents,
			CancellationToken cancellationToken = default);
	}
}
