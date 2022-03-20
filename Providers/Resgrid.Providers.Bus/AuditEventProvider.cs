using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Rabbit;

namespace Resgrid.Providers.Bus
{
	public class AuditEventProvider : IAuditEventProvider
	{
		private readonly RabbitOutboundQueueProvider _rabbitOutboundQueueProvider;

		public AuditEventProvider()
		{
			_rabbitOutboundQueueProvider = new RabbitOutboundQueueProvider();
		}

		public async Task<bool> EnqueueAuditEventAsync(AuditEvent auditEvent)
		{
			if (Config.SystemBehaviorConfig.ServiceBusType == Config.ServiceBusTypes.Rabbit)
			{
				_rabbitOutboundQueueProvider.EnqueueAuditEvent(auditEvent);
				return true;
			}

			return false;
		}
	}
}
