using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
using System;
using System.Linq;
using Autofac;
using Resgrid.Model.Events;

namespace Resgrid.Workers.Framework.Logic
{
	public class ResourceOrderNotifierLogic
	{
		private ITrainingService _trainingService;
		private ICommunicationService _communicationService;
		private IUserProfileService _userProfileService;
		private IDepartmentSettingsService _departmentSettingsService;

		public ResourceOrderNotifierLogic()
		{
			_trainingService = Bootstrapper.GetKernel().Resolve<ITrainingService>();
			_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
			_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
			_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
		}

		public Tuple<bool, string> Process(ResourceOrderAddedEvent item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.Order != null && item.Order.Items != null && item.Order.Items.Count > 0)
			{
				
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
