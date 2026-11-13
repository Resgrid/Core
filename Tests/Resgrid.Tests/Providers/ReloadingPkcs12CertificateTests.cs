using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.ProtectedData;
using Resgrid.Tests.Helpers;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The broker's certificates are short-lived step-ca leaves renewed in place by a sidecar. What these tests protect:
	/// a renewed file reaches the next handshake without a restart, the handshake path does not re-read an unchanged
	/// file, and a bad or missing file never takes away a certificate that is already loaded.
	/// </summary>
	[TestFixture]
	public class ReloadingPkcs12CertificateTests
	{
		private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

		private Pkcs12TestFile _file;
		private Clock _clock;
		private int _originalPollSeconds;

		[SetUp]
		public void SetUp()
		{
			_file = new Pkcs12TestFile();
			_clock = new Clock(Start);
			_originalPollSeconds = DataProtectionConfig.BrokerCertificatePollSeconds;
		}

		[TearDown]
		public void TearDown()
		{
			_file.Dispose();
			DataProtectionConfig.BrokerCertificatePollSeconds = _originalPollSeconds;
		}

		private ReloadingPkcs12Certificate Holder(TimeSpan pollInterval) =>
			new ReloadingPkcs12Certificate(_file.Path, Pkcs12TestFile.Password, pollInterval, _clock);

		[Test]
		public void Initial_load_presents_the_certificate_on_disk()
		{
			var thumbprint = _file.Write("CN=broker.protected.internal");

			var holder = Holder(TimeSpan.FromMinutes(1));

			holder.Current.Thumbprint.Should().Be(thumbprint);
			holder.Current.HasPrivateKey.Should().BeTrue("the certificate is presented on a TLS handshake");
			holder.Path.Should().Be(_file.Path);
		}

		[Test]
		public void Initial_load_failure_throws_so_the_host_stops_at_startup()
		{
			// Missing file.
			FluentActions.Invoking(() => Holder(TimeSpan.Zero)).Should().Throw<InvalidOperationException>()
				.WithMessage($"*{_file.Path}*");

			// Not a PKCS#12.
			_file.Corrupt();
			FluentActions.Invoking(() => Holder(TimeSpan.Zero)).Should().Throw<InvalidOperationException>();

			// Wrong password.
			_file.Write("CN=broker.protected.internal", password: "some-other-password");
			FluentActions.Invoking(() => Holder(TimeSpan.Zero)).Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void A_path_is_required()
		{
			FluentActions.Invoking(() => new ReloadingPkcs12Certificate(" ", null, TimeSpan.Zero, _clock))
				.Should().Throw<ArgumentException>();
		}

		[Test]
		public void Unchanged_file_is_not_reloaded()
		{
			_file.Write("CN=broker.protected.internal");
			var holder = Holder(TimeSpan.FromMinutes(1));
			var first = holder.Current;

			_clock.Advance(TimeSpan.FromMinutes(5));

			holder.Current.Should().BeSameAs(first, "an unchanged file is stat-ed, not re-read");
		}

		[Test]
		public void Changed_file_is_reloaded_once_the_poll_interval_has_passed()
		{
			var first = _file.Write("CN=broker.protected.internal");
			var holder = Holder(TimeSpan.FromMinutes(1));
			holder.Current.Thumbprint.Should().Be(first);

			// What the sidecar does at renewal: same path, new contents, by rename.
			var renewed = _file.Write("CN=broker.protected.internal");
			renewed.Should().NotBe(first);

			_clock.Advance(TimeSpan.FromSeconds(59));
			holder.Current.Thumbprint.Should().Be(first, "the handshake path checks the file at most once per poll interval");

			_clock.Advance(TimeSpan.FromSeconds(1));
			holder.Current.Thumbprint.Should().Be(renewed);
		}

		[Test]
		public void Failed_reload_keeps_the_previous_certificate_and_retries_on_a_later_poll()
		{
			var first = _file.Write("CN=broker.protected.internal");
			var holder = Holder(TimeSpan.FromMinutes(1));
			holder.Current.Thumbprint.Should().Be(first);

			// A bad write.
			_file.Corrupt();
			_clock.Advance(TimeSpan.FromMinutes(1));
			holder.Current.Thumbprint.Should().Be(first);

			// A file that is gone (e.g. the shared volume was emptied).
			_file.Delete();
			_clock.Advance(TimeSpan.FromMinutes(1));
			holder.Current.Thumbprint.Should().Be(first);

			// The sidecar recovers: the next poll picks the good file up.
			var renewed = _file.Write("CN=broker.protected.internal");
			_clock.Advance(TimeSpan.FromMinutes(1));
			holder.Current.Thumbprint.Should().Be(renewed);
		}

		[TestCase(60, 60)]
		[TestCase(15, 15)]
		[TestCase(0, 60)]
		[TestCase(-5, 60)]
		[TestCase(86400, 3600)]
		public void Configured_poll_interval_falls_back_and_caps(int configuredSeconds, int expectedSeconds)
		{
			DataProtectionConfig.BrokerCertificatePollSeconds = configuredSeconds;

			ReloadingPkcs12Certificate.ConfiguredPollInterval.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
		}

		private sealed class Clock : TimeProvider
		{
			private DateTimeOffset _now;

			public Clock(DateTimeOffset now) => _now = now;

			public void Advance(TimeSpan amount) => _now = _now.Add(amount);

			public override DateTimeOffset GetUtcNow() => _now;
		}
	}
}
