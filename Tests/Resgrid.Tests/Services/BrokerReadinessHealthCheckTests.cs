using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.ProtectedData;
using Resgrid.Services;
using Resgrid.Tests.Helpers;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// /health/ready is what the kubelet and the enrollment wizard trust. It must fail when the broker cannot do its
	/// job (KMS down or refusing the broker certificate, database down), not merely when the process is gone.
	/// </summary>
	[TestFixture]
	public class BrokerReadinessHealthCheckTests
	{
		private static readonly DateTimeOffset IssuedAt = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

		private Mock<IHealthService> _health;
		private Clock _clock;
		private Pkcs12TestFile _certificateFile;
		private string _originalCertificatePath;
		private string _originalCertificatePassword;

		[SetUp]
		public void SetUp()
		{
			_health = new Mock<IHealthService>();
			_health.Setup(h => h.GetDatabaseTimestamp()).ReturnsAsync("2026-10-20 16:00:00");

			_clock = new Clock { Now = IssuedAt };
			_certificateFile = new Pkcs12TestFile();
			_originalCertificatePath = DataProtectionConfig.OpenBaoClientCertificatePath;
			_originalCertificatePassword = DataProtectionConfig.OpenBaoClientCertificatePassword;
		}

		[TearDown]
		public void TearDown()
		{
			_certificateFile.Dispose();
			DataProtectionConfig.OpenBaoClientCertificatePath = _originalCertificatePath;
			DataProtectionConfig.OpenBaoClientCertificatePassword = _originalCertificatePassword;
		}

		/// <summary>A 24-hour step-ca style leaf issued at <see cref="IssuedAt"/>, loaded the way the broker loads it.</summary>
		private OpenBaoClientCertificate ClientCertificate()
		{
			_certificateFile.Write("CN=broker.protected.internal", IssuedAt, IssuedAt.AddHours(24));
			DataProtectionConfig.OpenBaoClientCertificatePath = _certificateFile.Path;
			DataProtectionConfig.OpenBaoClientCertificatePassword = Pkcs12TestFile.Password;
			return new OpenBaoClientCertificate(_clock);
		}

		private static IKeyWrappingProvider Kms(KeyWrappingProviderHealth health)
		{
			var provider = new Mock<IKeyWrappingProvider>();
			var status = provider.As<IKeyWrappingProviderStatus>();
			status.SetupGet(s => s.IsConfigured).Returns(true);
			status.Setup(s => s.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(health);
			return provider.Object;
		}

		private Task<HealthCheckResult> Check(IKeyWrappingProvider provider) =>
			new BrokerReadinessHealthCheck(provider, _health.Object, TimeProvider.System).CheckHealthAsync(new HealthCheckContext());

		/// <summary>Probes at <paramref name="now"/>; the certificate holder shares the clock, as both share TimeProvider in the broker.</summary>
		private Task<HealthCheckResult> Check(IKeyWrappingProvider provider, OpenBaoClientCertificate certificate, DateTimeOffset now)
		{
			_clock.Now = now;
			return new BrokerReadinessHealthCheck(provider, _health.Object, _clock, certificate).CheckHealthAsync(new HealthCheckContext());
		}

		[Test]
		public async Task Reachable_kms_and_database_is_ready()
		{
			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()));

			result.Status.Should().Be(HealthStatus.Healthy);
		}

		[Test]
		public async Task Unhealthy_kms_is_not_ready_and_skips_the_database()
		{
			var result = await Check(Kms(KeyWrappingProviderHealth.Unhealthy("kms_sealed")));

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Be("kms_sealed");
			_health.Verify(h => h.GetDatabaseTimestamp(), Times.Never);
		}

		[Test]
		public async Task Unreachable_database_is_not_ready()
		{
			_health.Setup(h => h.GetDatabaseTimestamp()).ReturnsAsync((string)null);

			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()));

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Be("database_unavailable");
		}

		[Test]
		public async Task Placeholder_inside_the_audit_decorator_is_not_ready()
		{
			// The broker's shape when KeyWrappingProviderType is mistyped.
			var provider = new AuditedKeyWrappingProvider(new NotConfiguredKeyWrappingProvider(), Mock.Of<IAdpAuditRepository>());

			var result = await Check(provider);

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Be("kms_not_configured");
		}

		[Test]
		public async Task Database_fault_is_not_ready_rather_than_an_exception()
		{
			_health.Setup(h => h.GetDatabaseTimestamp()).ThrowsAsync(new System.InvalidOperationException("boom"));

			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()));

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Be("probe_failed");
		}

		[Test]
		public async Task Client_certificate_with_most_of_its_life_left_is_ready()
		{
			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()), ClientCertificate(), IssuedAt.AddHours(16));

			result.Status.Should().Be(HealthStatus.Healthy);
		}

		[Test]
		public async Task Client_certificate_inside_the_renewal_window_is_not_ready_even_with_a_healthy_kms()
		{
			// The sidecar renews at about 16 hours; still holding the original at 22 hours means renewal is failing.
			// A cached OpenBao token keeps the KMS check green, so only the certificate check catches this.
			var now = IssuedAt.AddHours(24) - BrokerReadinessHealthCheck.ClientCertificateRenewalWindow;

			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()), ClientCertificate(), now);

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Be("kms_client_certificate_expiring");
			_health.Verify(h => h.GetDatabaseTimestamp(), Times.Never);
		}

		[Test]
		public async Task Expired_client_certificate_is_not_ready()
		{
			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()), ClientCertificate(), IssuedAt.AddHours(25));

			result.Status.Should().Be(HealthStatus.Unhealthy);
			result.Description.Should().Be("kms_client_certificate_expired");
		}

		[Test]
		public async Task Renewed_client_certificate_on_disk_clears_the_expiry_signal()
		{
			var certificate = ClientCertificate();
			var now = IssuedAt.AddHours(23);
			(await Check(Kms(KeyWrappingProviderHealth.Healthy()), certificate, now)).Status.Should().Be(HealthStatus.Unhealthy);

			// The sidecar recovers and writes a fresh leaf; one poll interval later the probe reads the certificate the
			// next handshake would use.
			_certificateFile.Write("CN=broker.protected.internal", now.AddMinutes(-5), now.AddHours(24));
			var result = await Check(Kms(KeyWrappingProviderHealth.Healthy()), certificate,
				now + ReloadingPkcs12Certificate.ConfiguredPollInterval);

			result.Status.Should().Be(HealthStatus.Healthy);
		}

		private sealed class Clock : TimeProvider
		{
			public DateTimeOffset Now { get; set; }

			public override DateTimeOffset GetUtcNow() => Now;
		}
	}
}
