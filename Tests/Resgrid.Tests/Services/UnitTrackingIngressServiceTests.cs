using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Tracking;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingIngressServiceTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private readonly DateTime _receivedOn =
			new(2026, 7, 24, 18, 42, 52, DateTimeKind.Utc);

		private Mock<IUnitLocationEventProvider> _eventProvider;
		private Mock<IUnitTrackingDevicesRepository> _devicesRepository;
		private Mock<IDepartmentSettingsService> _settingsService;
		private Mock<IUnitsService> _unitsService;
		private UnitTrackingIngressService _service;
		private int _originalMaxFutureSkewSeconds;

		[SetUp]
		public void SetUp()
		{
			_originalMaxFutureSkewSeconds = UnitTrackingConfig.MaxFutureSkewSeconds;
			UnitTrackingConfig.MaxFutureSkewSeconds = 300;

			_eventProvider = new Mock<IUnitLocationEventProvider>();
			_devicesRepository = new Mock<IUnitTrackingDevicesRepository>();
			_settingsService = new Mock<IDepartmentSettingsService>();
			_unitsService = new Mock<IUnitsService>();

			_eventProvider
				.Setup(provider => provider.EnqueueUnitLocationEventsAsync(
					It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_devicesRepository
				.Setup(repository => repository.UpdateAsync(
					It.IsAny<UnitTrackingDevice>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((UnitTrackingDevice device, CancellationToken cancellationToken, bool firstLevelOnly) =>
					device);
			_settingsService
				.Setup(service => service.GetHardwareTrackingLocationRetentionDaysAsync(DepartmentId, false))
				.ReturnsAsync(90);
			_unitsService
				.Setup(service => service.GetUnitByIdAsync(UnitId))
				.ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId });

			_service = new UnitTrackingIngressService(
				_eventProvider.Object,
				_devicesRepository.Object,
				new UnitTrackingEventIdService(),
				new UnitTrackingIdentifierService(),
				_settingsService.Object,
				_unitsService.Object);
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig.MaxFutureSkewSeconds = _originalMaxFutureSkewSeconds;
		}

		[Test]
		public async Task AcceptAsync_ValidBatch_NormalizesAndPublishesAllWithBindingMetadata()
		{
			IReadOnlyCollection<UnitLocationEvent> published = null;
			_eventProvider
				.Setup(provider => provider.EnqueueUnitLocationEventsAsync(
					It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
					It.IsAny<CancellationToken>()))
				.Callback<IReadOnlyCollection<UnitLocationEvent>, CancellationToken>(
					(events, cancellationToken) => published = events)
				.ReturnsAsync(true);
			var positions = new[]
			{
				Position("record-1", _receivedOn.AddSeconds(-2), 361m),
				Position("record-2", _receivedOn.AddSeconds(-1), -1m)
			};
			positions[0].AccuracyMeters = -1m;

			var result = await _service.AcceptAsync(Source(), positions);

			result.Status.Should().Be(TrackingIngressStatus.Accepted);
			result.Accepted.Should().Be(2);
			published.Should().HaveCount(2);
			published.Should().OnlyContain(item =>
				item.DepartmentId == DepartmentId &&
				item.UnitId == UnitId &&
				item.SourceType == (int)UnitLocationSourceType.HardwareTracker &&
				item.SourceId == "device-1" &&
				item.SourcePriority == 100);
			published.ElementAt(0).Heading.Should().Be(1m);
			published.ElementAt(0).Accuracy.Should().BeNull();
			published.ElementAt(1).Heading.Should().Be(359m);
			published.Select(item => item.EventId).Should().OnlyContain(id => id.Length == 64);
		}

		[Test]
		public async Task AcceptAsync_OneFutureRecord_RejectsWholeBatchBeforePublishing()
		{
			var positions = new[]
			{
				Position("valid", _receivedOn, 0m),
				Position("future", _receivedOn.AddMinutes(6), 0m)
			};

			var result = await _service.AcceptAsync(Source(), positions);

			result.Status.Should().Be(TrackingIngressStatus.Invalid);
			result.Errors.Should().Contain(error => error.Contains("positions[1]"));
			_eventProvider.Verify(provider => provider.EnqueueUnitLocationEventsAsync(
				It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task AcceptAsync_CallerEventIdOverMaximum_RejectsBeforePublishing()
		{
			var position = Position(new string('e', 257), _receivedOn, 0m);

			var result = await _service.AcceptAsync(Source(), new[] { position });

			result.Status.Should().Be(TrackingIngressStatus.Invalid);
			result.Errors.Should().Contain(error => error.Contains("eventId cannot exceed 256"));
			_eventProvider.Verify(provider => provider.EnqueueUnitLocationEventsAsync(
				It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task AcceptAsync_MissingAndBufferedTimestamps_UseServerTimeAndRetentionWindow()
		{
			var missing = Position("missing", default, 0m);
			missing.TimestampSource = TrackingTimestampSource.Unknown;
			var buffered = Position("buffered", _receivedOn.AddDays(-89), 0m);
			IReadOnlyCollection<UnitLocationEvent> published = null;
			_eventProvider
				.Setup(provider => provider.EnqueueUnitLocationEventsAsync(
					It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
					It.IsAny<CancellationToken>()))
				.Callback<IReadOnlyCollection<UnitLocationEvent>, CancellationToken>(
					(events, cancellationToken) => published = events)
				.ReturnsAsync(true);

			var result = await _service.AcceptAsync(Source(), new[] { missing, buffered });

			result.Status.Should().Be(TrackingIngressStatus.Accepted);
			published.ElementAt(0).Timestamp.Should().Be(_receivedOn);
			published.ElementAt(0).TimestampSource.Should().Be((int)TrackingTimestampSource.Server);
			published.ElementAt(1).Timestamp.Should().Be(_receivedOn.AddDays(-89));
		}

		[Test]
		public async Task AcceptAsync_TenantBindingMismatch_DoesNotPublish()
		{
			_unitsService
				.Setup(service => service.GetUnitByIdAsync(UnitId))
				.ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = 999 });

			var result = await _service.AcceptAsync(Source(), new[] { Position("record", _receivedOn, 0m) });

			result.Status.Should().Be(TrackingIngressStatus.Invalid);
			_eventProvider.Verify(provider => provider.EnqueueUnitLocationEventsAsync(
				It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task AcceptAsync_ReportedIdentifierMismatch_DoesNotPublish()
		{
			var source = Source();
			source.ReportedDeviceIdentifier = "OTHER-DEVICE";

			var result = await _service.AcceptAsync(source, new[] { Position("record", _receivedOn, 0m) });

			result.Status.Should().Be(TrackingIngressStatus.Invalid);
			_eventProvider.Verify(provider => provider.EnqueueUnitLocationEventsAsync(
				It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task AcceptAsync_ConfirmedPublishFailure_ReturnsUnavailable()
		{
			_eventProvider
				.Setup(provider => provider.EnqueueUnitLocationEventsAsync(
					It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(false);

			var result = await _service.AcceptAsync(
				Source(),
				new[] { Position("record", _receivedOn, 0m) });

			result.Status.Should().Be(TrackingIngressStatus.Unavailable);
			result.Accepted.Should().Be(0);
		}

		[Test]
		public async Task AcceptHeartbeatAsync_ValidBinding_UpdatesLastSeenWithoutPublishing()
		{
			// Arrange
			var source = Source();
			source.ReportedDeviceIdentifier = "DEVICE-1234";

			// Act
			var result = await _service.AcceptHeartbeatAsync(
				source,
				_receivedOn);

			// Assert
			result.Status.Should().Be(
				TrackingIngressStatus.Accepted);
			result.ReceivedOn.Should().Be(_receivedOn);
			source.Device.LastSeenOn.Should().Be(_receivedOn);
			source.Device.LastStatus.Should().Be(
				(int)UnitTrackingDeviceStatus.Online);
			_eventProvider.Verify(
				provider =>
					provider.EnqueueUnitLocationEventsAsync(
						It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
						It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task AcceptHeartbeatAsync_TenantBindingMismatch_RejectsHeartbeat()
		{
			// Arrange
			_unitsService
				.Setup(service =>
					service.GetUnitByIdAsync(UnitId))
				.ReturnsAsync(
					new Unit
					{
						UnitId = UnitId,
						DepartmentId = 999
					});

			// Act
			var result = await _service.AcceptHeartbeatAsync(
				Source(),
				_receivedOn);

			// Assert
			result.Status.Should().Be(
				TrackingIngressStatus.Invalid);
			_eventProvider.Verify(
				provider =>
					provider.EnqueueUnitLocationEventsAsync(
						It.IsAny<IReadOnlyCollection<UnitLocationEvent>>(),
						It.IsAny<CancellationToken>()),
				Times.Never);
		}

		private AuthenticatedTrackingSource Source()
		{
			return new AuthenticatedTrackingSource
			{
				Device = new UnitTrackingDevice
				{
					UnitTrackingDeviceId = "device-1",
					DepartmentId = DepartmentId,
					UnitId = UnitId,
					DeviceIdentifier = "DEVICE-1234",
					IsEnabled = true,
					SourcePriority = 100,
					TransportType = (int)UnitTrackingTransportType.NativeHttps,
					ProtocolKey = "resgrid-json"
				},
				Credential = new UnitTrackingCredential
				{
					UnitTrackingCredentialId = "credential-1"
				}
			};
		}

		private CanonicalTrackingPosition Position(
			string eventId,
			DateTime timestamp,
			decimal heading)
		{
			return new CanonicalTrackingPosition
			{
				EventId = eventId,
				TimestampUtc = timestamp,
				ReceivedOnUtc = _receivedOn,
				Latitude = 39.7392m,
				Longitude = -104.9903m,
				HeadingDegrees = heading,
				TimestampSource = TrackingTimestampSource.Device,
				IsValidFix = true
			};
		}
	}
}
