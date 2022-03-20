using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;

namespace Resgrid.Workers.Framework.Logic
{
	public class TrainingNotifierLogic
	{
		private ITrainingService _trainingService;
		private ICommunicationService _communicationService;
		private IUserProfileService _userProfileService;
		private IDepartmentSettingsService _departmentSettingsService;

		public TrainingNotifierLogic()
		{
			_trainingService = Bootstrapper.GetKernel().Resolve<ITrainingService>();
			_communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
			_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
			_departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
		}

		public async Task<Tuple<bool, string>> Process(TrainingNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.Training != null && item.Training.Users != null && item.Training.Users.Count > 0)
			{
				var message = String.Empty;
				var title = String.Empty;
				var profiles = await _userProfileService.GetSelectedUserProfilesAsync(item.Training.Users.Select(x => x.UserId).ToList());
				var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.Training.DepartmentId);

				if (ConfigHelper.CanTransmit(item.Training.DepartmentId))
				{
					if (!item.Training.Notified.HasValue)
					{
						if (item.Training.ToBeCompletedBy.HasValue)
							message = string.Format("New Training ({0}) due on {1}", item.Training.Name,
								item.Training.ToBeCompletedBy.Value.ToShortDateString());
						else
							message = string.Format("New Training ({0}) assigned to you", item.Training.Name);

						title = "New Training Notice";
					}
					else
					{
						message = string.Format("Training ({0}) is due tomorrow", item.Training.Name);
					}

					foreach (var person in item.Training.Users)
					{
						var profile = profiles.FirstOrDefault(x => x.UserId == person.UserId);

						if (!item.Training.Notified.HasValue || !person.Complete)
							await _communicationService.SendNotificationAsync(person.UserId, item.Training.DepartmentId, message, departmentNumber, title, profile);

						title = "Training Due Notice";
					}
				}

				await _trainingService.MarkAsNotifiedAsync(item.Training.TrainingId);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
