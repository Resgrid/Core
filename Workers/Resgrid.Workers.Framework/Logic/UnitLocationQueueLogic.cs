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
			try
			{
				if (unitLocationEvent == null)
					throw new ArgumentNullException(nameof(unitLocationEvent));

				if (unitLocationEvent.UnitId <= 0)
					throw new InvalidOperationException("A Unit location queue event must identify a Unit.");

				if (unitLocationEvent.DepartmentId <= 0)
					throw new InvalidOperationException("A Unit location queue event must identify a Department.");

				if (unitLocationEvent.IsValidFix == false)
				{
					Logging.LogInfo($"UnitLocationQueueLogic dropping invalid fix for UnitId {unitLocationEvent.UnitId}, EventId {unitLocationEvent.EventId}.");
					return true;
				}

				if (!unitLocationEvent.Latitude.HasValue || !unitLocationEvent.Longitude.HasValue)
					throw new InvalidOperationException("A valid Unit location queue event must contain latitude and longitude.");

				var unitService = Bootstrapper.GetKernel().Resolve<IUnitsService>();
				var timestamp = unitLocationEvent.Timestamp == default
					? unitLocationEvent.ReceivedOn ?? DateTime.UtcNow
					: unitLocationEvent.Timestamp;
				var unitLocation = new UnitsLocation
				{
					EventId = unitLocationEvent.EventId,
					DepartmentId = unitLocationEvent.DepartmentId,
					UnitId = unitLocationEvent.UnitId,
					Timestamp = timestamp,
					ReceivedOn = unitLocationEvent.ReceivedOn ?? timestamp,
					SourceType = unitLocationEvent.SourceType,
					SourceId = unitLocationEvent.SourceId,
					SourcePriority = unitLocationEvent.SourcePriority,
					TransportType = unitLocationEvent.TransportType,
					ProtocolKey = unitLocationEvent.ProtocolKey,
					IsValidFix = unitLocationEvent.IsValidFix,
					Latitude = unitLocationEvent.Latitude.Value,
					Longitude = unitLocationEvent.Longitude.Value,
					Accuracy = unitLocationEvent.Accuracy,
					Altitude = unitLocationEvent.Altitude,
					AltitudeAccuracy = unitLocationEvent.AltitudeAccuracy,
					Speed = unitLocationEvent.Speed,
					Heading = unitLocationEvent.Heading,
					Satellites = unitLocationEvent.Satellites,
					Hdop = unitLocationEvent.Hdop,
					BatteryPercent = unitLocationEvent.BatteryPercent,
					ExternalPowerVolts = unitLocationEvent.ExternalPowerVolts,
					SignalPercent = unitLocationEvent.SignalPercent,
					Ignition = unitLocationEvent.Ignition,
					IsMoving = unitLocationEvent.IsMoving,
					AlarmCode = unitLocationEvent.AlarmCode,
					TimestampSource = unitLocationEvent.TimestampSource
				};

				await unitService.AddUnitLocationAsync(unitLocation, unitLocationEvent.DepartmentId, cancellationToken);

				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
