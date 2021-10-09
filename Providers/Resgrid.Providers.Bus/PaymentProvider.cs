using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class PaymentProvider : IPaymentProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public PaymentProvider()
		{
			_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
		}

		public async Task<bool> EnqueuePaymentEventAsync(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueuePaymentEvent(cqrsEvent);
				return true;
			}

			return false;
		}
	}
}
