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
	public class UnitLocationQueueLogic
	{
		public static async Task<bool> ProcessUnitLocationQueueItem(UnitLocationEvent unitLocationEvent, CancellationToken cancellationToken = default(CancellationToken))
		{
			bool success = true;

			if (unitLocationEvent != null)
			{
				try
				{
					var unitService = Bootstrapper.GetKernel().Resolve<IUnitsService>();

					if (unitLocationEvent.Latitude.HasValue && unitLocationEvent.Longitude.HasValue)
					{
						var unitLocation = new UnitsLocation();
						unitLocation.DepartmentId = unitLocationEvent.DepartmentId;
						unitLocation.UnitId = unitLocationEvent.UnitId;
						unitLocation.Timestamp = unitLocationEvent.Timestamp;
						unitLocation.Latitude = unitLocationEvent.Latitude.Value;
						unitLocation.Longitude = unitLocationEvent.Longitude.Value;
						unitLocation.Accuracy = unitLocationEvent.Accuracy;
						unitLocation.Altitude = unitLocationEvent.Altitude;
						unitLocation.AltitudeAccuracy = unitLocationEvent.AltitudeAccuracy;
						unitLocation.Speed = unitLocationEvent.Speed;
						unitLocation.Heading = unitLocationEvent.Heading;

						if (unitLocation.UnitId > 0)
						{
							await unitService.AddUnitLocationAsync(unitLocation, unitLocationEvent.DepartmentId, cancellationToken);
						}
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
