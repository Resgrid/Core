using System;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Practices.ServiceLocation;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Microsoft.AspNet.Identity.EntityFramework6;
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
			public void Handle(DepartmentSettingsUpdateEvent message)
			{
				var departmentSettingsService = ServiceLocator.Current.GetInstance<IDepartmentSettingsService>();
				departmentSettingsService.SaveOrUpdateSetting(message.DepartmentId, DateTime.UtcNow.ToString("G"), DepartmentSettingTypes.UpdateTimestamp);
			}
		}

		public class AuditEventHandler : IListener<AuditEvent>
		{
			public void Handle(AuditEvent message)
			{
				CqrsEvent cqrsEvent = new CqrsEvent();
				cqrsEvent.Type = (int)CqrsEventTypes.AuditLog;
				cqrsEvent.Data = ObjectSerialization.Serialize(message);

				_cqrsProvider.EnqueueCqrsEvent(cqrsEvent);
			}
		}
	}
}
