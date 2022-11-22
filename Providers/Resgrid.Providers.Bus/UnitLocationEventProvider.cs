using System.Threading.Tasks;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class UnitLocationEventProvider : IUnitLocationEventProvider
	{
		private RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public UnitLocationEventProvider()
		{

		}

		public async Task<bool> EnqueueUnitLocationEventAsync(UnitLocationEvent unitLocationEvent)
		{
			if (_rabbitOutboundQueueProvider == null)
				_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
			
			_rabbitOutboundQueueProvider.EnqueueUnitLocationEvent(unitLocationEvent);
			
			return true;
		}
	}
}
