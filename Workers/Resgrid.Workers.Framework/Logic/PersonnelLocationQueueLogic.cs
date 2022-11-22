using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Model.Events;
using Resgrid.Model;

namespace Resgrid.Workers.Framework.Logic
{
	public class PersonnelLocationQueueLogic
	{
		public static async Task<bool> ProcessPersonnelLocationQueueItem(PersonnelLocationEvent personnelLocationEvent, CancellationToken cancellationToken = default(CancellationToken))
		{
			bool success = true;

			if (personnelLocationEvent != null)
			{
				try
				{
					var usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();

					var personnelLocation = new PersonnelLocation();
					personnelLocation.UserId = personnelLocationEvent.UserId;
					personnelLocation.DepartmentId = personnelLocationEvent.DepartmentId;
					personnelLocation.Timestamp = personnelLocationEvent.Timestamp;
					personnelLocation.Latitude = personnelLocationEvent.Latitude;
					personnelLocation.Longitude = personnelLocationEvent.Longitude;
					personnelLocation.Accuracy = personnelLocationEvent.Accuracy;
					personnelLocation.Altitude = personnelLocationEvent.Altitude;
					personnelLocation.AltitudeAccuracy = personnelLocationEvent.AltitudeAccuracy;
					personnelLocation.Speed = personnelLocationEvent.Speed;
					personnelLocation.Heading = personnelLocationEvent.Heading;

					if (!String.IsNullOrWhiteSpace(personnelLocation.UserId))
					{
						await usersService.SavePersonnelLocationAsync(personnelLocation);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return success;
		}
	}
}
