using System;
using CommonServiceLocator;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Model.Providers;

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

			_eventAggregator.AddListener(departmentSettingsUpdateHandler);
		}

		private Action<DepartmentSettingsUpdateEvent> departmentSettingsUpdateHandler = async delegate(DepartmentSettingsUpdateEvent message)
		{
			var departmentSettingsService = ServiceLocator.Current.GetInstance<IDepartmentSettingsService>();
			var result = await departmentSettingsService.SaveOrUpdateSettingAsync(message.DepartmentId, DateTime.UtcNow.ToString("G"), DepartmentSettingTypes.UpdateTimestamp);
		};
	}
}
