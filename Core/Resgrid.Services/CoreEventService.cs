using System;
using System.Threading.Tasks;
using CommonServiceLocator;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Model.Providers;
using Resgrid.Framework;

namespace Resgrid.Services
{
	public class CoreEventService : ICoreEventService
	{
		private readonly IEventAggregator _eventAggregator;
		private static ICqrsProvider _cqrsProvider;

		public CoreEventService(IEventAggregator eventAggregator, ICqrsProvider cqrsProvider)
		{
			_eventAggregator = eventAggregator;
			_cqrsProvider = cqrsProvider;

			_eventAggregator.AddListener(new DepartmentSettingsUpdateHandler(), true);
			_eventAggregator.AddListener(new AuditEventHandler(), true);
		}

		public class DepartmentSettingsUpdateHandler : IListener<DepartmentSettingsUpdateEvent>
		{
			public async Task<bool> Handle(DepartmentSettingsUpdateEvent message)
			{
				var departmentSettingsService = ServiceLocator.Current.GetInstance<IDepartmentSettingsService>();
				var result = await departmentSettingsService.SaveOrUpdateSettingAsync(message.DepartmentId, DateTime.UtcNow.ToString("G"), DepartmentSettingTypes.UpdateTimestamp);

				if (result != null)
					return true;

				return false;
			}
		}

		public class AuditEventHandler : IListener<AuditEvent>
		{
			public async Task<bool> Handle(AuditEvent message)
			{
				CqrsEvent cqrsEvent = new CqrsEvent();
				cqrsEvent.Type = (int)CqrsEventTypes.AuditLog;
				cqrsEvent.Data = ObjectSerialization.Serialize(message);

				return await _cqrsProvider.EnqueueCqrsEventAsync(cqrsEvent);
			}
		}
	}
}
