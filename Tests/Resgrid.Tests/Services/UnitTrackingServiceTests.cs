using System;
using System.Collections.Generic;
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
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingServiceTests
	{
		private const int DepartmentId = 10;
		private const int UnitId = 42;
		private const string UserId = "admin-user";

		private Mock<IUnitTrackingDevicesRepository> _devicesRepository;
		private Mock<IUnitTrackingCredentialsRepository> _credentialsRepository;
		private Mock<IUnitTrackingAuthenticationService> _authenticationService;
		private Mock<IUnitsService> _unitsService;
		private Mock<IEventAggregator> _eventAggregator;
		private Mock<IUnitOfWork> _unitOfWork;
		private UnitTrackingService _service;
		private List<AuditEvent> _auditEvents;
		private string _originalPublicHttpsBaseUrl;

		[SetUp]
		public void SetUp()
		{
			_originalPublicHttpsBaseUrl = UnitTrackingConfig.PublicHttpsBaseUrl;
			UnitTrackingConfig.PublicHttpsBaseUrl = "https://tracking.example";

			_devicesRepository = new Mock<IUnitTrackingDevicesRepository>();
			_credentialsRepository = new Mock<IUnitTrackingCredentialsRepository>();
			_authenticationService = new Mock<IUnitTrackingAuthenticationService>();
			_unitsService = new Mock<IUnitsService>();
			_eventAggregator = new Mock<IEventAggregator>();
			_unitOfWork = new Mock<IUnitOfWork>();
			_auditEvents = new List<AuditEvent>();

			_unitsService
				.Setup(service => service.GetUnitByIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int unitId) => new Unit
				{
					UnitId = unitId,
					DepartmentId = DepartmentId,
					Name = $"Unit {unitId}"
				});
			_devicesRepository
				.Setup(repository => repository.InsertAsync(
					It.IsAny<UnitTrackingDevice>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((UnitTrackingDevice device, CancellationToken cancellationToken, bool firstLevelOnly) => device);
			_devicesRepository
				.Setup(repository => repository.UpdateAsync(
					It.IsAny<UnitTrackingDevice>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((UnitTrackingDevice device, CancellationToken cancellationToken, bool firstLevelOnly) => device);
			_credentialsRepository
				.Setup(repository => repository.InsertAsync(
					It.IsAny<UnitTrackingCredential>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((UnitTrackingCredential credential, CancellationToken cancellationToken, bool firstLevelOnly) => credential);
			_credentialsRepository
				.Setup(repository => repository.UpdateAsync(
					It.IsAny<UnitTrackingCredential>(),
					It.IsAny<CancellationToken>(),
					false))
				.ReturnsAsync((UnitTrackingCredential credential, CancellationToken cancellationToken, bool firstLevelOnly) => credential);
			_authenticationService
				.Setup(service => service.InvalidateDeviceAsync(It.IsAny<UnitTrackingDevice>()))
				.Returns(Task.CompletedTask);
			_authenticationService
				.Setup(service => service.InvalidateCredentialAsync(It.IsAny<string>()))
				.Returns(Task.CompletedTask);
			_eventAggregator
				.Setup(aggregator => aggregator.SendMessage(It.IsAny<AuditEvent>()))
				.Callback<AuditEvent>(auditEvent => _auditEvents.Add(auditEvent));

			_service = new UnitTrackingService(
				_devicesRepository.Object,
				_credentialsRepository.Object,
				_authenticationService.Object,
				new UnitTrackingIdentifierService(),
				_unitsService.Object,
				_eventAggregator.Object,
				_unitOfWork.Object);
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig.PublicHttpsBaseUrl = _originalPublicHttpsBaseUrl;
		}

		[Test]
		public async Task CreateDeviceAsync_ValidBinding_NormalizesIdentifierAndPublishesRedactedAudit()
		{
			var result = await _service.CreateDeviceAsync(
				new UnitTrackingDevice
				{
					UnitId = UnitId,
					DisplayName = " Engine Tracker ",
					TransportType = (int)UnitTrackingTransportType.NativeTcpUdp,
					ProtocolKey = " GT06 ",
					DeviceIdentifier = " secret-device-1234 ",
					SourcePriority = 100
				},
				DepartmentId,
				UserId);

			result.UnitTrackingDeviceId.Should().NotBeNullOrWhiteSpace();
			result.DepartmentId.Should().Be(DepartmentId);
			result.ProtocolKey.Should().Be("gt06");
			result.DeviceIdentifier.Should().Be("SECRET-DEVICE-1234");
			result.CreatedByUserId.Should().Be(UserId);
			_auditEvents.Should().ContainSingle();
			_auditEvents[0].Type.Should().Be(AuditLogTypes.UnitTrackingDeviceCreated);
			_auditEvents[0].After.Should().NotContain("SECRET-DEVICE-1234");
			_auditEvents[0].After.Should().Contain("1234");
		}

		[Test]
		public async Task CreateDeviceAsync_UnitOwnedByAnotherDepartment_RejectsBinding()
		{
			_unitsService
				.Setup(service => service.GetUnitByIdAsync(UnitId))
				.ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = 999, Name = "Other Unit" });

			Func<Task> act = () => _service.CreateDeviceAsync(
				new UnitTrackingDevice
				{
					UnitId = UnitId,
					TransportType = (int)UnitTrackingTransportType.NativeHttps
				},
				DepartmentId,
				UserId);

			await act.Should().ThrowAsync<InvalidOperationException>()
				.WithMessage("*not found for this department*");
			_devicesRepository.Verify(
				repository => repository.InsertAsync(
					It.IsAny<UnitTrackingDevice>(),
					It.IsAny<CancellationToken>(),
					It.IsAny<bool>()),
				Times.Never);
		}

		[Test]
		public async Task CreateCredentialAsync_EnabledDevice_ReturnsTokenWithoutExposingStoredHash()
		{
			var device = Device();
			var generatedHash = new string('b', 64);
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(device);
			_authenticationService
				.Setup(service => service.GenerateCredential())
				.Returns(new UnitTrackingGeneratedCredential
				{
					Token = "rgtrk_prefix12_generated-secret",
					KeyPrefix = "prefix12",
					SecretHash = generatedHash
				});

			var result = await _service.CreateCredentialAsync(
				device.UnitTrackingDeviceId,
				DepartmentId,
				UnitTrackingAuthMode.Bearer,
				UserId);

			result.Token.Should().Be("rgtrk_prefix12_generated-secret");
			result.Credential.SecretHash.Should().BeNull();
			result.EndpointUrl.Should().Be(
				"https://tracking.example/api/v4/unit-trackers/device-1/positions");
			result.HeaderName.Should().Be("Authorization");
			result.HeaderValue.Should().Be("Bearer rgtrk_prefix12_generated-secret");
			_credentialsRepository.Verify(
				repository => repository.InsertAsync(
					It.Is<UnitTrackingCredential>(credential =>
						credential.SecretHash == generatedHash &&
						credential.KeyPrefix == "prefix12"),
					It.IsAny<CancellationToken>(),
					false),
				Times.Once);
			_auditEvents.Should().ContainSingle(audit =>
				audit.Type == AuditLogTypes.UnitTrackingCredentialCreated &&
				!audit.After.Contains(result.Token) &&
				!audit.After.Contains(generatedHash));
		}

		[Test]
		public async Task CreateCredentialAsync_CapabilityPath_ReturnsOneTimeSecretOnlyInProvisioningData()
		{
			var device = Device();
			const string token = "rgtrk_capability_generated-secret";
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(device);
			_authenticationService
				.Setup(service => service.GenerateCredential())
				.Returns(new UnitTrackingGeneratedCredential
				{
					Token = token,
					KeyPrefix = "capabili",
					SecretHash = new string('c', 64)
				});

			var result = await _service.CreateCredentialAsync(
				device.UnitTrackingDeviceId,
				DepartmentId,
				UnitTrackingAuthMode.CapabilityPath,
				UserId);

			result.EndpointUrl.Should().Be(
				$"https://tracking.example/api/v4/unit-trackers/c/{token}");
			result.HeaderName.Should().BeNull();
			result.HeaderValue.Should().BeNull();
			result.Credential.SecretHash.Should().BeNull();
			_auditEvents.Should().OnlyContain(audit =>
				!audit.After.Contains(token));
		}

		[Test]
		public async Task RotateCredentialAsync_ValidCredential_ExpiresOldAndReturnsNewTokenOnce()
		{
			var device = Device();
			var existing = Credential("credential-old", "old-hash");
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(device);
			_credentialsRepository
				.Setup(repository => repository.GetByIdAsync(existing.UnitTrackingCredentialId))
				.ReturnsAsync(existing);
			_authenticationService
				.Setup(service => service.GenerateCredential())
				.Returns(new UnitTrackingGeneratedCredential
				{
					Token = "rgtrk_prefix12_generated-secret",
					KeyPrefix = "prefix12",
					SecretHash = new string('a', 64)
				});

			var before = DateTime.UtcNow;
			var result = await _service.RotateCredentialAsync(
				device.UnitTrackingDeviceId,
				existing.UnitTrackingCredentialId,
				DepartmentId,
				UserId,
				TimeSpan.FromHours(1));

			existing.ExpiresOn.Should().NotBeNull();
			existing.ExpiresOn.Should().BeAfter(before.AddMinutes(59));
			result.Token.Should().Be("rgtrk_prefix12_generated-secret");
			result.Credential.SecretHash.Should().BeNull();
			_unitOfWork.Verify(unitOfWork => unitOfWork.CommitChanges(), Times.Once);
			_auditEvents.Should().ContainSingle(audit =>
				audit.Type == AuditLogTypes.UnitTrackingCredentialRotated &&
				!audit.After.Contains(result.Token) &&
				!audit.After.Contains(new string('a', 64)));
		}

		[Test]
		public async Task RevokeCredentialAsync_ActiveCredential_RevokesAndInvalidates()
		{
			var device = Device();
			var credential = Credential("credential-1", "credential-hash");
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(device);
			_credentialsRepository
				.Setup(repository => repository.GetByIdAsync(credential.UnitTrackingCredentialId))
				.ReturnsAsync(credential);

			var result = await _service.RevokeCredentialAsync(
				device.UnitTrackingDeviceId,
				credential.UnitTrackingCredentialId,
				DepartmentId,
				UserId);

			credential.RevokedOn.Should().NotBeNull();
			result.SecretHash.Should().BeNull();
			_authenticationService.Verify(
				service => service.InvalidateCredentialAsync("credential-hash"),
				Times.Once);
			_auditEvents.Should().ContainSingle(audit =>
				audit.Type == AuditLogTypes.UnitTrackingCredentialRevoked);
		}

		[Test]
		public async Task DisableDeviceAsync_ActiveDevice_RevokesCredentialsAtomically()
		{
			var device = Device();
			var credentials = new List<UnitTrackingCredential>
			{
				Credential("credential-1", "hash-1"),
				Credential("credential-2", "hash-2")
			};
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(device);
			_credentialsRepository
				.Setup(repository => repository.GetAllByDeviceIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(credentials);

			var result = await _service.DisableDeviceAsync(
				device.UnitTrackingDeviceId,
				DepartmentId,
				UserId);

			result.IsEnabled.Should().BeFalse();
			result.LastStatus.Should().Be((int)UnitTrackingDeviceStatus.Disabled);
			credentials.Should().OnlyContain(credential => credential.RevokedOn.HasValue);
			_unitOfWork.Verify(unitOfWork => unitOfWork.CommitChanges(), Times.Once);
			_auditEvents.Should().ContainSingle(audit =>
				audit.Type == AuditLogTypes.UnitTrackingDeviceDisabled);
		}

		[Test]
		public async Task RebindDeviceAsync_NewUnit_SoftDeletesOldBindingAndCreatesReplacement()
		{
			var device = Device();
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(device);
			_credentialsRepository
				.Setup(repository => repository.GetAllByDeviceIdAsync(device.UnitTrackingDeviceId))
				.ReturnsAsync(new List<UnitTrackingCredential>());

			var result = await _service.RebindDeviceAsync(
				device.UnitTrackingDeviceId,
				DepartmentId,
				99,
				UserId);

			device.IsDeleted.Should().BeTrue();
			device.IsEnabled.Should().BeFalse();
			result.UnitTrackingDeviceId.Should().NotBe(device.UnitTrackingDeviceId);
			result.UnitId.Should().Be(99);
			result.DeviceIdentifier.Should().Be(device.DeviceIdentifier);
			result.IsEnabled.Should().BeTrue();
			_unitOfWork.Verify(unitOfWork => unitOfWork.CommitChanges(), Times.Once);
			_auditEvents.Should().Contain(audit => audit.Type == AuditLogTypes.UnitTrackingDeviceDeleted);
			_auditEvents.Should().Contain(audit => audit.Type == AuditLogTypes.UnitTrackingDeviceCreated);
		}

		private static UnitTrackingDevice Device()
		{
			return new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				DepartmentId = DepartmentId,
				UnitId = UnitId,
				DisplayName = "Engine Tracker",
				TransportType = (int)UnitTrackingTransportType.NativeTcpUdp,
				ProtocolKey = "gt06",
				DeviceIdentifier = "DEVICE-1234",
				IsEnabled = true,
				SourcePriority = 100,
				CreatedByUserId = UserId,
				CreatedOn = DateTime.UtcNow.AddDays(-1)
			};
		}

		private static UnitTrackingCredential Credential(string credentialId, string secretHash)
		{
			return new UnitTrackingCredential
			{
				UnitTrackingCredentialId = credentialId,
				UnitTrackingDeviceId = "device-1",
				AuthMode = (int)UnitTrackingAuthMode.Bearer,
				KeyPrefix = "prefix12",
				SecretHash = secretHash,
				ValidFrom = DateTime.UtcNow.AddDays(-1),
				CreatedByUserId = UserId,
				CreatedOn = DateTime.UtcNow.AddDays(-1)
			};
		}
	}
}
