using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;

namespace Resgrid.Services
{
	public class UnitTrackingIngressService : IUnitTrackingIngressService
	{
		private const int MaximumAlarmCodeLength = 64;
		private const int MaximumEventIdLength = 256;

		private readonly IUnitLocationEventProvider _unitLocationEventProvider;
		private readonly IUnitTrackingDevicesRepository _devicesRepository;
		private readonly IUnitTrackingEventIdService _eventIdService;
		private readonly IUnitTrackingIdentifierService _identifierService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUnitsService _unitsService;

		public UnitTrackingIngressService(
			IUnitLocationEventProvider unitLocationEventProvider,
			IUnitTrackingDevicesRepository devicesRepository,
			IUnitTrackingEventIdService eventIdService,
			IUnitTrackingIdentifierService identifierService,
			IDepartmentSettingsService departmentSettingsService,
			IUnitsService unitsService)
		{
			_unitLocationEventProvider = unitLocationEventProvider;
			_devicesRepository = devicesRepository;
			_eventIdService = eventIdService;
			_identifierService = identifierService;
			_departmentSettingsService = departmentSettingsService;
			_unitsService = unitsService;
		}

		public async Task<TrackingIngressResult> AcceptAsync(
			AuthenticatedTrackingSource source,
			IReadOnlyCollection<CanonicalTrackingPosition> positions,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var receivedOn = GetReceivedOn(positions);

			if (!IsEnabled(source?.Device))
				return Invalid(receivedOn, "The tracking binding is not enabled.");

		var device = source.Device;

		if (positions == null || positions.Count == 0)
		{
			await TryUpdateDeviceStatusAsync(device, receivedOn, null, "empty-payload", cancellationToken);
			return Invalid(receivedOn, "At least one position is required.");
		}

		if (positions.Count > Math.Max(1, UnitTrackingConfig.MaxBatchPositions))
		{
			await TryUpdateDeviceStatusAsync(device, receivedOn, null, "batch-limit", cancellationToken);
			return Invalid(receivedOn, "The position batch exceeds the configured limit.");
		}

		var unit = await _unitsService.GetUnitByIdAsync(device.UnitId);
		if (unit == null || unit.DepartmentId != device.DepartmentId)
		{
			await TryUpdateDeviceStatusAsync(device, receivedOn, null, "tenant-binding-invalid", cancellationToken);
			return Invalid(receivedOn, "The tracking binding is invalid.");
		}

		if (!IdentifierMatches(device.DeviceIdentifier, source.ReportedDeviceIdentifier))
		{
			await TryUpdateDeviceStatusAsync(device, receivedOn, null, "identifier-mismatch", cancellationToken);
			return Invalid(receivedOn, "The reported device identifier does not match the binding.");
		}

		var retentionDays =
				await _departmentSettingsService.GetHardwareTrackingLocationRetentionDaysAsync(device.DepartmentId);
			var normalization = NormalizeAll(positions, retentionDays);
			if (normalization.Errors.Count > 0)
			{
				await TryUpdateDeviceStatusAsync(device, receivedOn, null, "invalid-payload", cancellationToken);
				return new TrackingIngressResult
				{
					Status = TrackingIngressStatus.Invalid,
					ReceivedOn = receivedOn,
					Errors = normalization.Errors
				};
			}

			var events = normalization.Positions
				.Where(position => position.IsValidFix)
				.Select(position => BuildEvent(device, position))
				.ToList();

			if (events.Count > 0)
			{
				var published = await _unitLocationEventProvider.EnqueueUnitLocationEventsAsync(events, cancellationToken);
				if (!published)
				{
					await TryUpdateDeviceStatusAsync(
						device,
						receivedOn,
						normalization.Positions,
						"queue-unavailable",
						cancellationToken);
					return new TrackingIngressResult
					{
						Status = TrackingIngressStatus.Unavailable,
						ReceivedOn = receivedOn
					};
				}
			}

			await TryUpdateDeviceStatusAsync(
				device,
				receivedOn,
				normalization.Positions,
				null,
				cancellationToken);

			return new TrackingIngressResult
			{
				Status = TrackingIngressStatus.Accepted,
				Accepted = events.Count,
				DuplicatesPossible = false,
				ReceivedOn = receivedOn
			};
		}

		public async Task<TrackingIngressResult> AcceptHeartbeatAsync(
			AuthenticatedTrackingSource source,
			DateTime receivedOnUtc,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var receivedOn = EnsureUtc(
				receivedOnUtc == default
					? DateTime.UtcNow
					: receivedOnUtc);

			if (!IsEnabled(source?.Device))
				return Invalid(
					receivedOn,
					"The tracking binding is not enabled.");

			var device = source.Device;
			var unit = await _unitsService.GetUnitByIdAsync(
				device.UnitId);
			if (unit == null ||
			    unit.DepartmentId != device.DepartmentId)
			{
				await TryUpdateDeviceStatusAsync(
					device,
					receivedOn,
					null,
					"tenant-binding-invalid",
					cancellationToken);
				return Invalid(
					receivedOn,
					"The tracking binding is invalid.");
			}

			if (!IdentifierMatches(
				    device.DeviceIdentifier,
				    source.ReportedDeviceIdentifier))
			{
				await TryUpdateDeviceStatusAsync(
					device,
					receivedOn,
					null,
					"identifier-mismatch",
					cancellationToken);
				return Invalid(
					receivedOn,
					"The reported device identifier does not match the binding.");
			}

			await TryUpdateDeviceStatusAsync(
				device,
				receivedOn,
				null,
				null,
				cancellationToken);
			return new TrackingIngressResult
			{
				Status = TrackingIngressStatus.Accepted,
				ReceivedOn = receivedOn
			};
		}

		private NormalizationResult NormalizeAll(
			IReadOnlyCollection<CanonicalTrackingPosition> positions,
			int retentionDays)
		{
			var normalized = new List<CanonicalTrackingPosition>(positions.Count);
			var errors = new List<string>();
			var index = 0;

			foreach (var position in positions)
			{
				var itemErrors = new List<string>();
				var item = Normalize(position, Math.Max(1, retentionDays), itemErrors);
				if (itemErrors.Count > 0)
					errors.AddRange(itemErrors.Select(error => $"positions[{index}]: {error}"));
				else
					normalized.Add(item);

				index++;
			}

			return new NormalizationResult(normalized, errors);
		}

		private CanonicalTrackingPosition Normalize(
			CanonicalTrackingPosition position,
			int retentionDays,
			ICollection<string> errors)
		{
			if (position == null)
			{
				errors.Add("Position is required.");
				return null;
			}

			if (string.IsNullOrWhiteSpace(position.EventId))
				errors.Add("eventId is required.");
			else if (position.EventId.Trim().Length > MaximumEventIdLength)
				errors.Add($"eventId cannot exceed {MaximumEventIdLength} characters.");

			if (position.Latitude < -90m || position.Latitude > 90m)
				errors.Add("latitude must be between -90 and 90.");
			if (position.Longitude < -180m || position.Longitude > 180m)
				errors.Add("longitude must be between -180 and 180.");

			var receivedOn = EnsureUtc(
				position.ReceivedOnUtc == default ? DateTime.UtcNow : position.ReceivedOnUtc);
			var timestampSource = position.TimestampSource;
			var timestamp = position.TimestampUtc;
			if (timestamp == default)
			{
				timestamp = receivedOn;
				timestampSource = TrackingTimestampSource.Server;
			}
			else
			{
				timestamp = EnsureUtc(timestamp);
				if (timestampSource == TrackingTimestampSource.Unknown)
					timestampSource = TrackingTimestampSource.Device;
			}

			if (timestamp > receivedOn.AddSeconds(Math.Max(0, UnitTrackingConfig.MaxFutureSkewSeconds)))
				errors.Add("timestamp is too far in the future.");
			if (timestamp < receivedOn.AddDays(-retentionDays))
				errors.Add("timestamp is outside the configured retention window.");

			var alarmCode = string.IsNullOrWhiteSpace(position.AlarmCode)
				? null
				: position.AlarmCode.Trim();
			if (alarmCode?.Length > MaximumAlarmCodeLength)
				errors.Add($"alarmCode cannot exceed {MaximumAlarmCodeLength} characters.");

			return new CanonicalTrackingPosition
			{
				EventId = position.EventId?.Trim(),
				TimestampUtc = timestamp,
				ReceivedOnUtc = receivedOn,
				Latitude = position.Latitude,
				Longitude = position.Longitude,
				AccuracyMeters = NonNegative(position.AccuracyMeters),
				AltitudeMeters = position.AltitudeMeters,
				SpeedMetersPerSecond = NonNegative(position.SpeedMetersPerSecond),
				HeadingDegrees = NormalizeHeading(position.HeadingDegrees),
				Satellites = NonNegative(position.Satellites),
				Hdop = NonNegative(position.Hdop),
				BatteryPercent = Percentage(position.BatteryPercent),
				ExternalPowerVolts = NonNegative(position.ExternalPowerVolts),
				SignalPercent = Percentage(position.SignalPercent),
				Ignition = position.Ignition,
				IsMoving = position.IsMoving,
				AlarmCode = alarmCode,
				TimestampSource = timestampSource,
				IsValidFix = position.IsValidFix
			};
		}

		private UnitLocationEvent BuildEvent(
			UnitTrackingDevice device,
			CanonicalTrackingPosition position)
		{
			return new UnitLocationEvent
			{
				EventId = _eventIdService.CreateForHttps(device.UnitTrackingDeviceId, position.EventId),
				DepartmentId = device.DepartmentId,
				UnitId = device.UnitId,
				Timestamp = position.TimestampUtc,
				ReceivedOn = position.ReceivedOnUtc,
				Latitude = position.Latitude,
				Longitude = position.Longitude,
				Accuracy = position.AccuracyMeters,
				Altitude = position.AltitudeMeters,
				Speed = position.SpeedMetersPerSecond,
				Heading = position.HeadingDegrees,
				SourceType = (int)UnitLocationSourceType.HardwareTracker,
				SourceId = device.UnitTrackingDeviceId,
				SourcePriority = device.SourcePriority,
				TransportType = device.TransportType,
				ProtocolKey = device.ProtocolKey,
				IsValidFix = position.IsValidFix,
				Satellites = position.Satellites,
				Hdop = position.Hdop,
				BatteryPercent = position.BatteryPercent,
				ExternalPowerVolts = position.ExternalPowerVolts,
				SignalPercent = position.SignalPercent,
				Ignition = position.Ignition,
				IsMoving = position.IsMoving,
				AlarmCode = position.AlarmCode,
				TimestampSource = (int)position.TimestampSource
			};
		}

		private async Task TryUpdateDeviceStatusAsync(
			UnitTrackingDevice device,
			DateTime receivedOn,
			IReadOnlyCollection<CanonicalTrackingPosition> positions,
			string errorCode,
			CancellationToken cancellationToken)
		{
			try
			{
				device.LastSeenOn = receivedOn;
				device.LastErrorCode = errorCode;
				device.LastStatus = errorCode == null
					? (int)UnitTrackingDeviceStatus.Online
					: (int)UnitTrackingDeviceStatus.Error;

				var validPositions = positions?
					.Where(position => position.IsValidFix)
					.ToList();
				if (validPositions?.Count > 0)
				{
					device.LastPositionOn = validPositions.Max(position => position.TimestampUtc);
					device.LastReceivedOn = validPositions.Max(position => position.ReceivedOnUtc);
				}

				await _devicesRepository.UpdateAsync(device, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Unable to update tracking device status metadata.");
			}
		}

		private bool IdentifierMatches(string configured, string reported)
		{
			if (string.IsNullOrWhiteSpace(reported))
				return true;

			try
			{
				var normalizedReported = _identifierService.Normalize(reported);
				var normalizedConfigured = _identifierService.Normalize(configured);
				return normalizedConfigured != null &&
				       string.Equals(normalizedConfigured, normalizedReported, StringComparison.Ordinal);
			}
			catch (ArgumentException)
			{
				return false;
			}
		}

		private static bool IsEnabled(UnitTrackingDevice device) =>
			device != null && device.IsEnabled && !device.IsDeleted;

		private static TrackingIngressResult Invalid(DateTime receivedOn, string error) =>
			new()
			{
				Status = TrackingIngressStatus.Invalid,
				ReceivedOn = receivedOn,
				Errors = new[] { error }
			};

		private static DateTime GetReceivedOn(IReadOnlyCollection<CanonicalTrackingPosition> positions)
		{
			var receivedOn = positions?
				.Where(position => position != null && position.ReceivedOnUtc != default)
				.Select(position => EnsureUtc(position.ReceivedOnUtc))
				.DefaultIfEmpty(DateTime.UtcNow)
				.Min() ?? DateTime.UtcNow;
			return receivedOn;
		}

		private static DateTime EnsureUtc(DateTime value) =>
			value.Kind switch
			{
				DateTimeKind.Utc => value,
				DateTimeKind.Local => value.ToUniversalTime(),
				_ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
			};

		private static decimal? NonNegative(decimal? value) =>
			value.HasValue && value.Value >= 0m ? value : null;

		private static int? NonNegative(int? value) =>
			value.HasValue && value.Value >= 0 ? value : null;

		private static decimal? Percentage(decimal? value) =>
			value.HasValue && value.Value >= 0m && value.Value <= 100m ? value : null;

		private static int? Percentage(int? value) =>
			value.HasValue && value.Value >= 0 && value.Value <= 100 ? value : null;

		private static decimal? NormalizeHeading(decimal? heading)
		{
			if (!heading.HasValue)
				return null;

			return ((heading.Value % 360m) + 360m) % 360m;
		}

		private sealed class NormalizationResult
		{
			public NormalizationResult(
				IReadOnlyCollection<CanonicalTrackingPosition> positions,
				IReadOnlyCollection<string> errors)
			{
				Positions = positions;
				Errors = errors;
			}

			public IReadOnlyCollection<CanonicalTrackingPosition> Positions { get; }
			public IReadOnlyCollection<string> Errors { get; }
		}
	}
}
