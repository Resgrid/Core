using System;
using Autofac;
using Autofac.Core;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Providers.ProtectedData;
using Resgrid.Tests.Helpers;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The broker's startup check resolves the key wrapping provider and relies on that failing when the OpenBao client
	/// certificate is missing or unreadable. Moving the certificate into a reloading holder must keep that, and the
	/// provider and the readiness check must share one holder.
	/// </summary>
	[TestFixture]
	public class OpenBaoClientCertificateTests
	{
		private Pkcs12TestFile _file;
		private string _originalProviderType;
		private string _originalAddress;
		private string _originalPath;
		private string _originalPassword;

		[SetUp]
		public void SetUp()
		{
			_file = new Pkcs12TestFile();
			_originalProviderType = DataProtectionConfig.KeyWrappingProviderType;
			_originalAddress = DataProtectionConfig.OpenBaoAddress;
			_originalPath = DataProtectionConfig.OpenBaoClientCertificatePath;
			_originalPassword = DataProtectionConfig.OpenBaoClientCertificatePassword;

			DataProtectionConfig.KeyWrappingProviderType = "OpenBaoTransit";
			DataProtectionConfig.OpenBaoAddress = "https://bao.test:8200";
			DataProtectionConfig.OpenBaoClientCertificatePath = _file.Path;
			DataProtectionConfig.OpenBaoClientCertificatePassword = Pkcs12TestFile.Password;
		}

		[TearDown]
		public void TearDown()
		{
			_file.Dispose();
			DataProtectionConfig.KeyWrappingProviderType = _originalProviderType;
			DataProtectionConfig.OpenBaoAddress = _originalAddress;
			DataProtectionConfig.OpenBaoClientCertificatePath = _originalPath;
			DataProtectionConfig.OpenBaoClientCertificatePassword = _originalPassword;
		}

		private static IContainer BrokerContainer()
		{
			var builder = new ContainerBuilder();
			builder.RegisterInstance(TimeProvider.System).As<TimeProvider>();
			builder.RegisterInstance(Mock.Of<IAdpAuditRepository>()).As<IAdpAuditRepository>();
			builder.RegisterModule(new ProtectedDataProviderModule());
			return builder.Build();
		}

		[Test]
		public void Provider_resolves_with_one_shared_reloading_client_certificate()
		{
			var thumbprint = _file.Write("CN=broker.protected.internal");
			using var container = BrokerContainer();

			var provider = container.Resolve<IKeyWrappingProvider>();
			var certificate = container.Resolve<OpenBaoClientCertificate>();

			provider.Should().BeOfType<AuditedKeyWrappingProvider>();
			provider.IsKmsConfigured().Should().BeTrue();
			certificate.Should().BeSameAs(container.Resolve<OpenBaoClientCertificate>());
			certificate.Current.Thumbprint.Should().Be(thumbprint);
		}

		[Test]
		public void Missing_certificate_path_still_fails_provider_resolution()
		{
			DataProtectionConfig.OpenBaoClientCertificatePath = "";
			using var container = BrokerContainer();

			FluentActions.Invoking(() => container.Resolve<IKeyWrappingProvider>())
				.Should().Throw<DependencyResolutionException>()
				.Which.ToString().Should().Contain("DataProtectionConfig.OpenBaoClientCertificatePath is not configured");
		}

		[Test]
		public void Unreadable_certificate_still_fails_provider_resolution()
		{
			_file.Corrupt();
			using var container = BrokerContainer();

			FluentActions.Invoking(() => container.Resolve<IKeyWrappingProvider>())
				.Should().Throw<DependencyResolutionException>()
				.Which.ToString().Should().Contain(_file.Path);
		}

		[Test]
		public void Plaintext_address_still_fails_provider_resolution()
		{
			_file.Write("CN=broker.protected.internal");
			DataProtectionConfig.OpenBaoAddress = "http://bao.test:8200";
			using var container = BrokerContainer();

			FluentActions.Invoking(() => container.Resolve<IKeyWrappingProvider>())
				.Should().Throw<DependencyResolutionException>()
				.Which.ToString().Should().Contain("must use HTTPS");
		}
	}
}
