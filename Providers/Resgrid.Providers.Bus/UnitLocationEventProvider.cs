using System.Threading.Tasks;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus
{
	public class UnitLocationEventProvider : IUnitLocationEventProvider
	{
		private readonly IRabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public UnitLocationEventProvider(IRabbitOutboundQueueProvider rabbitOutboundQueueProvider)
		{
			_rabbitOutboundQueueProvider = rabbitOutboundQueueProvider;
		}

		public async Task<bool> EnqueueUnitLocationEventAsync(UnitLocationEvent unitLocationEvent)
		{
			return await _rabbitOutboundQueueProvider.EnqueueUnitLocationEvent(unitLocationEvent);
		}

		public async Task<bool> EnqueueUnitLocationEventsAsync(
			System.Collections.Generic.IReadOnlyCollection<UnitLocationEvent> unitLocationEvents,
			System.Threading.CancellationToken cancellationToken = default)
		{
			return await _rabbitOutboundQueueProvider.EnqueueUnitLocationEvents(
				unitLocationEvents,
				cancellationToken);
		}
	}
}
