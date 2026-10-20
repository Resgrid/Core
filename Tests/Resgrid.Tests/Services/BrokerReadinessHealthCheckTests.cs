using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.ProtectedData;
using Resgrid.Services;
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
		private Mock<IHealthService> _health;

		[SetUp]
		public void SetUp()
		{
			_health = new Mock<IHealthService>();
			_health.Setup(h => h.GetDatabaseTimestamp()).ReturnsAsync("2026-10-20 16:00:00");
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
			new BrokerReadinessHealthCheck(provider, _health.Object).CheckHealthAsync(new HealthCheckContext());

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
	}
}
