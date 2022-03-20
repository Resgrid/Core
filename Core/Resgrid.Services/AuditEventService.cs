using System;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	public class AuditEventService : IAuditEventService
	{
		private readonly IEventAggregator _eventAggregator;
		private static IAuditEventProvider _auditEventProvider;

		public AuditEventService(IEventAggregator eventAggregator, IAuditEventProvider auditEventProvider)
		{
			_eventAggregator = eventAggregator;
			_auditEventProvider = auditEventProvider;

			_eventAggregator.AddListener(auditEventHandler);
		}

		private Action<AuditEvent> auditEventHandler = async delegate(AuditEvent message)
		{
			await _auditEventProvider.EnqueueAuditEventAsync(message);
		};
	}
}
