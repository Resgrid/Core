using Resgrid.Model.Helpers;
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

			try
			{
				// A training without assignees is still marked, so it stops coming back as unannounced on every run.
				if (item != null && item.Training != null)
				{
					// Own scope per item: root-scope services would share the process-wide root unit of work.
					using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
					var trainingService = scope.Resolve<ITrainingService>();
					var communicationService = scope.Resolve<ICommunicationService>();
					var userProfileService = scope.Resolve<IUserProfileService>();
					var departmentSettingsService = scope.Resolve<IDepartmentSettingsService>();
					var departmentsService = scope.Resolve<IDepartmentsService>();

					// Who is told and with what text: assignees who have not completed it and are still active members, and never
					// an old training announced as new (TrainingNoticePlanner).
					var activeMemberIds = await departmentsService.GetActiveMemberUserIdsAsync(item.Training.DepartmentId);
					var plan = TrainingNoticePlanner.Plan(item.Training, activeMemberIds, DateTime.UtcNow);

					if (plan.Kind != TrainingNoticeKinds.None && ConfigHelper.CanTransmit(item.Training.DepartmentId))
					{
						var profiles = await userProfileService.GetSelectedUserProfilesAsync(plan.RecipientUserIds);
						var departmentNumber = await departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.Training.DepartmentId);
						var department = await departmentsService.GetDepartmentByIdAsync(item.Training.DepartmentId, false);

						foreach (var userId in plan.RecipientUserIds)
						{
							var profile = profiles?.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));

							await communicationService.SendNotificationAsync(userId, item.Training.DepartmentId, plan.Message, departmentNumber, department, plan.Title, profile);
						}
					}

					await trainingService.MarkAsNotifiedAsync(item.Training.TrainingId);
				}
			}
			catch (Exception ex)
			{
				// One training that fails must not stop the rest of the run.
				Logging.LogException(ex);
				success = false;
				result = ex.ToString();
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
