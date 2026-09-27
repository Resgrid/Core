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
		public async Task<Tuple<bool, string>> Process(TrainingNotifierQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.Training != null && item.Training.Users != null && item.Training.Users.Count > 0)
			{
				// Own scope per item: root-scope services would share the process-wide root unit of work.
				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var trainingService = scope.Resolve<ITrainingService>();
				var communicationService = scope.Resolve<ICommunicationService>();
				var userProfileService = scope.Resolve<IUserProfileService>();
				var departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();
				var departmentsService = scope.Resolve<IDepartmentsService>();

				var message = String.Empty;
				var title = String.Empty;
				var profiles = await userProfileService.GetSelectedUserProfilesAsync(item.Training.Users.Select(x => x.UserId).ToList());
				var departmentNumber = await departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.Training.DepartmentId);
				var department = await departmentsService.GetDepartmentByIdAsync(item.Training.DepartmentId, false);

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
							await communicationService.SendNotificationAsync(person.UserId, item.Training.DepartmentId, message, departmentNumber, department, title, profile);

						title = "Training Due Notice";
					}
				}

				await trainingService.MarkAsNotifiedAsync(item.Training.TrainingId);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
