using System.Threading.Tasks;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class PersonnelLocationEventProvider : IPersonnelLocationEventProvider
	{
		private RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public PersonnelLocationEventProvider()
		{

		}

		public async Task<bool> EnqueuePersonnelLocationEventAsync(PersonnelLocationEvent personnelLocationEvent)
		{
			if (_rabbitOutboundQueueProvider == null)
				_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
			
			_rabbitOutboundQueueProvider.EnqueuePersonnelLocationEvent(personnelLocationEvent);

			return true;
		}
	}
}
