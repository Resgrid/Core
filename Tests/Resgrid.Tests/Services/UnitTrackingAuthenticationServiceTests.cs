using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	[NonParallelizable]
	public class UnitTrackingAuthenticationServiceTests
	{
		private Mock<IUnitTrackingCredentialsRepository> _credentialsRepository;
		private Mock<IUnitTrackingDevicesRepository> _devicesRepository;
		private UnitTrackingAuthenticationService _service;
		private string _originalPepper;
		private bool _originalCacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_originalPepper = UnitTrackingConfig.CredentialPepper;
			_originalCacheEnabled = SystemBehaviorConfig.CacheEnabled;
			UnitTrackingConfig.CredentialPepper = "unit-test-pepper-with-enough-entropy";
			SystemBehaviorConfig.CacheEnabled = false;

			_credentialsRepository = new Mock<IUnitTrackingCredentialsRepository>();
			_devicesRepository = new Mock<IUnitTrackingDevicesRepository>();
			_service = new UnitTrackingAuthenticationService(
				_credentialsRepository.Object,
				_devicesRepository.Object,
				new UnitTrackingIdentifierService(),
				Mock.Of<ICacheProvider>());
		}

		[TearDown]
		public void TearDown()
		{
			UnitTrackingConfig.CredentialPepper = _originalPepper;
			SystemBehaviorConfig.CacheEnabled = _originalCacheEnabled;
		}

		[Test]
		public void GenerateCredential_CreatesOneTimeTokenAndLowercaseHash()
		{
			var generated = _service.GenerateCredential();
			var tokenPrefix = $"rgtrk_{generated.KeyPrefix}_";
			var encodedSecret = generated.Token.Substring(tokenPrefix.Length);

			generated.Token.Should().StartWith(tokenPrefix);
			generated.KeyPrefix.Should().HaveLength(8);
			generated.KeyPrefix.Should().MatchRegex("^[A-Za-z0-9_-]{8}$");
			encodedSecret.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
			generated.KeyPrefix.Should().NotBe(encodedSecret.Substring(0, 8));
			generated.SecretHash.Should().MatchRegex("^[0-9a-f]{64}$");
			_service.VerifySecret(generated.Token, generated.SecretHash).Should().BeTrue();
			_service.VerifySecret(generated.Token + "x", generated.SecretHash).Should().BeFalse();
		}

		[Test]
		public async Task AuthenticateAsync_ActiveCredentialAndDevice_ReturnsBinding()
		{
			var now = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);
			var generated = _service.GenerateCredential();
			var credential = new UnitTrackingCredential
			{
				UnitTrackingCredentialId = "credential-1",
				UnitTrackingDeviceId = "device-1",
				SecretHash = generated.SecretHash,
				ValidFrom = now.AddMinutes(-1)
			};
			var device = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				IsEnabled = true
			};
			_credentialsRepository
				.Setup(repository => repository.GetBySecretHashAsync(generated.SecretHash))
				.ReturnsAsync(credential);
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync("device-1"))
				.ReturnsAsync(device);

			var result = await _service.AuthenticateAsync(generated.Token, now);

			result.Should().NotBeNull();
			result.Device.Should().BeSameAs(device);
			result.Credential.Should().BeSameAs(credential);
		}

		[TestCase(true, false)]
		[TestCase(false, true)]
		public async Task AuthenticateAsync_InactiveCredential_ReturnsNull(bool revoked, bool expired)
		{
			var now = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);
			var generated = _service.GenerateCredential();
			_credentialsRepository
				.Setup(repository => repository.GetBySecretHashAsync(generated.SecretHash))
				.ReturnsAsync(new UnitTrackingCredential
				{
					UnitTrackingDeviceId = "device-1",
					SecretHash = generated.SecretHash,
					ValidFrom = now.AddDays(-1),
					RevokedOn = revoked ? now.AddMinutes(-1) : null,
					ExpiresOn = expired ? now.AddMinutes(-1) : null
				});
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync("device-1"))
				.ReturnsAsync(new UnitTrackingDevice
				{
					UnitTrackingDeviceId = "device-1",
					IsEnabled = true
				});

			var result = await _service.AuthenticateAsync(generated.Token, now);

			result.Should().BeNull();
		}

		[Test]
		public async Task GetActiveCredentialsForDeviceAsync_NullCacheResult_ReturnsEmptyCollection()
		{
			// Arrange
			var device = new UnitTrackingDevice
			{
				UnitTrackingDeviceId = "device-1",
				IsEnabled = true
			};
			var cacheProvider = new Mock<ICacheProvider>();
			_devicesRepository
				.Setup(repository => repository.GetByIdAsync("device-1"))
				.ReturnsAsync(device);
			cacheProvider
				.Setup(provider => provider.RetrieveAsync(
					It.IsAny<string>(),
					It.IsAny<Func<Task<UnitTrackingDevice>>>(),
					It.IsAny<TimeSpan>()))
				.Returns((string _, Func<Task<UnitTrackingDevice>> fallback, TimeSpan _) => fallback());
			cacheProvider
				.Setup(provider => provider.RetrieveAsync(
					It.IsAny<string>(),
					It.IsAny<Func<Task<List<UnitTrackingCredential>>>>(),
					It.IsAny<TimeSpan>()))
				.ReturnsAsync((List<UnitTrackingCredential>)null);
			var service = new UnitTrackingAuthenticationService(
				_credentialsRepository.Object,
				_devicesRepository.Object,
				new UnitTrackingIdentifierService(),
				cacheProvider.Object);
			SystemBehaviorConfig.CacheEnabled = true;

			// Act
			var result = await service.GetActiveCredentialsForDeviceAsync("device-1");

			// Assert
			result.Should().NotBeNull().And.BeEmpty();
		}
	}
}
