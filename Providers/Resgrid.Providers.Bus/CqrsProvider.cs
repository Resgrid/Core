using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class CqrsProvider : ICqrsProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public CqrsProvider()
		{

			_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
		}

		public async Task<bool> EnqueueCqrsEventAsync(CqrsEvent cqrsEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueCqrsEvent(cqrsEvent);
				return true;
			}

			return false;
		}
	}
}
