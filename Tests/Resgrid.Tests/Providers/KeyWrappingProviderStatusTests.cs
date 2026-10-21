using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.ProtectedData;
using Resgrid.Services;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The broker resolves IKeyWrappingProvider to the audit decorator, so "is this the fail-closed placeholder?" has
	/// to be asked through the status surface. A type check saw only the decorator, which let a broker with a
	/// mistyped KeyWrappingProviderType start and run migration nights that could only fail.
	/// </summary>
	[TestFixture]
	public class KeyWrappingProviderStatusTests
	{
		private string _originalProviderType;

		[SetUp]
		public void SetUp()
		{
			_originalProviderType = DataProtectionConfig.KeyWrappingProviderType;
		}

		[TearDown]
		public void TearDown()
		{
			DataProtectionConfig.KeyWrappingProviderType = _originalProviderType;
		}

		private static AuditedKeyWrappingProvider Audited(IKeyWrappingProvider inner) =>
			new AuditedKeyWrappingProvider(inner, Mock.Of<IAdpAuditRepository>());

		private static DepartmentDataMigrationEngine Engine(IKeyWrappingProvider provider) =>
			new DepartmentDataMigrationEngine(Mock.Of<IDepartmentDataProtectionBulkRepository>(),
				Mock.Of<IDepartmentDataProtectionMigrationRepository>(), Mock.Of<IDepartmentKeyService>(), provider,
				Mock.Of<IProtectedFieldCryptoService>(), Mock.Of<IProtectedFieldCatalog>());

		[Test]
		public void Mistyped_provider_type_on_the_broker_resolves_to_an_unconfigured_provider()
		{
			// The broker's real composition: ServicesModule's placeholder, then ProtectedDataProviderModule, which
			// adds the audit decorator and only registers OpenBao when the type is spelled exactly.
			DataProtectionConfig.KeyWrappingProviderType = "OpenBao";

			var builder = new ContainerBuilder();
			builder.RegisterType<NotConfiguredKeyWrappingProvider>().As<IKeyWrappingProvider>().SingleInstance();
			builder.RegisterInstance(Mock.Of<IAdpAuditRepository>()).As<IAdpAuditRepository>();
			builder.RegisterModule(new ProtectedDataProviderModule());

			using var container = builder.Build();
			var provider = container.Resolve<IKeyWrappingProvider>();

			provider.Should().BeOfType<AuditedKeyWrappingProvider>("this is why a type check cannot work on the broker");
			provider.IsKmsConfigured().Should().BeFalse("the broker startup check must refuse to start");
			Engine(provider).IsAvailable.Should().BeFalse("migration nights must not run where every one would fail");
		}

		[Test]
		public async Task Audit_decorator_forwards_the_placeholder_status()
		{
			var provider = Audited(new NotConfiguredKeyWrappingProvider());

			provider.IsKmsConfigured().Should().BeFalse();
			(await provider.CheckKmsHealthAsync()).Reason.Should().Be("kms_not_configured");
		}

		[Test]
		public async Task Audit_decorator_forwards_a_real_providers_health()
		{
			var inner = new Mock<IKeyWrappingProvider>();
			var status = inner.As<IKeyWrappingProviderStatus>();
			status.SetupGet(s => s.IsConfigured).Returns(true);
			status.Setup(s => s.CheckHealthAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(KeyWrappingProviderHealth.Unhealthy("kms_sealed"));

			var provider = Audited(inner.Object);

			provider.IsKmsConfigured().Should().BeTrue();
			Engine(provider).IsAvailable.Should().BeTrue();

			var health = await provider.CheckKmsHealthAsync();
			health.IsHealthy.Should().BeFalse();
			health.Reason.Should().Be("kms_sealed");
		}

		[Test]
		public async Task Missing_provider_is_unconfigured_and_unhealthy()
		{
			IKeyWrappingProvider provider = null;

			provider.IsKmsConfigured().Should().BeFalse();
			(await provider.CheckKmsHealthAsync()).Reason.Should().Be("kms_not_configured");
		}

		[Test]
		public async Task Provider_without_a_status_surface_counts_as_configured_and_healthy()
		{
			// Pins the compatibility rule: a test double (or LocalDev) behaves as it did under the old type check.
			var provider = Mock.Of<IKeyWrappingProvider>();

			provider.IsKmsConfigured().Should().BeTrue();
			(await provider.CheckKmsHealthAsync()).IsHealthy.Should().BeTrue();
		}

		[Test]
		public void Bare_placeholder_is_unconfigured()
		{
			Engine(new NotConfiguredKeyWrappingProvider()).IsAvailable.Should().BeFalse();
		}
	}
}
