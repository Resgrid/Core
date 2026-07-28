using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Gt06;
using Resgrid.Providers.Tracking.Protocols.Queclink;
using Resgrid.Providers.Tracking.Protocols.Teltonika;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class TrackingProtocolCatalogValidatorTests
	{
		[Test]
		public void Validate_RegisteredNativeModulesSupportCatalogTransports_Completes()
		{
			// Arrange
			var registry =
				new TrackingProtocolModuleRegistry(
					new ITrackingProtocolModule[]
					{
						new TeltonikaCodec8ProtocolModule(),
						new QueclinkProtocolModule(),
						new Gt06ProtocolModule()
					});

			// Act
			Action act = () =>
				TrackingProtocolCatalogValidator.Validate(
					registry);

			// Assert
			act.Should().NotThrow();
		}

		[Test]
		public void Validate_NativeCatalogModuleMissing_RejectsStartup()
		{
			// Arrange
			var registry =
				new TrackingProtocolModuleRegistry(
					Array.Empty<ITrackingProtocolModule>());

			// Act
			Action act = () =>
				TrackingProtocolCatalogValidator.Validate(
					registry);

			// Assert
			act.Should()
				.Throw<InvalidOperationException>()
				.WithMessage(
					"*teltonika-codec8*");
		}
	}
}
