using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Listeners;

namespace Resgrid.Tracking.Tests.Hosting
{
	[TestFixture]
	public class TrackingListenerPlanBuilderTests
	{
		[Test]
		public void Build_NativeGatewayDisabled_ReturnsEmptyPlan()
		{
			// Arrange
			var settings = CreateSettings(
				trackingEnabled: false,
				nativeGatewayEnabled: false,
				credentialPepper: null,
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						true,
						5000,
						true,
						5000)
				});
			var registry = new TrackingProtocolModuleRegistry(
				Array.Empty<ITrackingProtocolModule>());
			var factory = new SupportingListenerFactory();

			// Act
			var plan = new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				factory);

			// Assert
			plan.Listeners.Should().BeEmpty();
		}

		[Test]
		public void Build_NativeGatewayEnabledWithoutMasterSwitch_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				trackingEnabled: false,
				protocols: Array.Empty<TrackingProtocolListenerSettings>());
			var registry = new TrackingProtocolModuleRegistry(
				Array.Empty<ITrackingProtocolModule>());

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.Which.Errors.Should().Contain(
					error => error.Contains(
						"UnitTrackingConfig.Enabled",
						StringComparison.Ordinal));
		}

		[Test]
		public void Build_NativeGatewayEnabledWithoutPepper_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				credentialPepper: " ",
				protocols: Array.Empty<TrackingProtocolListenerSettings>());
			var registry = new TrackingProtocolModuleRegistry(
				Array.Empty<ITrackingProtocolModule>());

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.Which.Errors.Should().Contain(
					error => error.Contains(
						"CredentialPepper",
						StringComparison.Ordinal));
		}

		[Test]
		public void Build_FrameLimitAboveSafetyCeiling_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				maxFrameBytes: 1024 * 1024 + 1,
				protocols:
				Array.Empty<TrackingProtocolListenerSettings>());
			var registry = new TrackingProtocolModuleRegistry(
				Array.Empty<ITrackingProtocolModule>());

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.Which.Errors.Should().Contain(
					error => error.Contains(
						"MaxFrameBytes",
						StringComparison.Ordinal));
		}

		[Test]
		public void Build_RegisteredTcpModule_PlansOnlyCertifiedTransport()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						true,
						5000,
						false,
						0)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-v1",
					TrackingSocketTransport.Tcp)
			});

			// Act
			var plan = new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			plan.Listeners.Should().ContainSingle();
			plan.Listeners.Single().Transport.Should().Be(
				TrackingSocketTransport.Tcp);
			plan.Listeners.Single().Port.Should().Be(5000);
		}

		[Test]
		public void Build_EnabledTransportNotSupportedByModule_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						false,
						5000,
						true,
						5001)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-v1",
					TrackingSocketTransport.Tcp)
			});

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.WithMessage("*does not support enabled transport 'Udp'*");
		}

		[Test]
		public void Build_EnabledProtocolWithoutTransport_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						false,
						5000,
						false,
						5001)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-v1",
					TrackingSocketTransport.Tcp)
			});

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.WithMessage("*must enable at least one transport*");
		}

		[Test]
		public void Build_TcpAndUdpUseSamePort_AllowsTransportSpecificBindings()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						true,
						5000,
						true,
						5000)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-v1",
					TrackingSocketTransport.Tcp,
					TrackingSocketTransport.Udp)
			});

			// Act
			var plan = new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			plan.Listeners.Should().HaveCount(2);
			plan.Listeners.Select(listener => listener.Transport)
				.Should()
				.BeEquivalentTo(new[]
				{
					TrackingSocketTransport.Tcp,
					TrackingSocketTransport.Udp
				});
		}

		[Test]
		public void Build_EnabledProtocolWithoutModule_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						true,
						5000,
						true,
						5000)
				});
			var registry = new TrackingProtocolModuleRegistry(
				Array.Empty<ITrackingProtocolModule>());

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.WithMessage("*No tracking protocol module is registered*");
		}

		[Test]
		public void Build_DuplicateTcpPort_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-a",
						true,
						true,
						5000,
						false,
						5001),
					new TrackingProtocolListenerSettings(
						"synthetic-b",
						true,
						true,
						5000,
						false,
						5002)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-a",
					TrackingSocketTransport.Tcp),
				new TestProtocolModule(
					"synthetic-b",
					TrackingSocketTransport.Tcp)
			});

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.WithMessage("*more than one Tcp listener*");
		}

		[Test]
		public void Build_HealthPortCollidesWithTcpListener_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				internalHealthPort: 5000,
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						true,
						5000,
						false,
						5001)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-v1",
					TrackingSocketTransport.Tcp)
			});

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new SupportingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.WithMessage("*InternalHealthPort cannot share*");
		}

		[Test]
		public void Build_ListenerImplementationUnavailable_RejectsConfiguration()
		{
			// Arrange
			var settings = CreateSettings(
				protocols: new[]
				{
					new TrackingProtocolListenerSettings(
						"synthetic-v1",
						true,
						true,
						5000,
						false,
						5001)
				});
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule(
					"synthetic-v1",
					TrackingSocketTransport.Tcp)
			});

			// Act
			Action act = () => new TrackingListenerPlanBuilder().Build(
				settings,
				registry,
				new UnavailableTrackingListenerFactory());

			// Assert
			act.Should().Throw<TrackingGatewayConfigurationException>()
				.WithMessage("*No socket listener implementation is registered*");
		}

		private static TrackingGatewaySettings CreateSettings(
			bool trackingEnabled = true,
			bool nativeGatewayEnabled = true,
			string credentialPepper = "test-pepper",
			int maxFrameBytes = 65536,
			int internalHealthPort = 8080,
			IEnumerable<TrackingProtocolListenerSettings> protocols = null)
		{
			return new TrackingGatewaySettings(
				trackingEnabled,
				nativeGatewayEnabled,
				credentialPepper,
				tcpIdleTimeoutSeconds: 300,
				maxFrameBytes,
				maxConnections: 5000,
				maxConnectionsPerIp: 100,
				gracefulShutdownSeconds: 30,
				internalHealthPort,
				protocols);
		}

		private sealed class SupportingListenerFactory :
			ITrackingListenerFactory
		{
			public bool Supports(TrackingListenerDefinition definition)
			{
				return true;
			}

			public ITrackingListener Create(
				TrackingListenerDefinition definition)
			{
				throw new NotSupportedException();
			}
		}

		private sealed class TestProtocolModule : ITrackingProtocolModule
		{
			public TestProtocolModule(
				string protocolKey,
				params TrackingSocketTransport[] supportedTransports)
			{
				ProtocolKey = protocolKey;
				SupportedTransports =
					new HashSet<TrackingSocketTransport>(supportedTransports);
			}

			public string ProtocolKey { get; }
			public IReadOnlySet<TrackingSocketTransport> SupportedTransports { get; }

			public ITrackingProtocolSession CreateSession(
				TrackingSessionContext context)
			{
				return new TestProtocolSession();
			}
		}

		private sealed class TestProtocolSession : ITrackingProtocolSession
		{
			public ProtocolParseResult Parse(
				ref ReadOnlySequence<byte> input)
			{
				return new ProtocolParseResult
				{
					Status = ProtocolParseStatus.NeedMoreData,
					Consumed = input.Start,
					Examined = input.End
				};
			}

			public ReadOnlyMemory<byte> BuildResponse(
				ProtocolMessage message,
				TrackingAcceptance acceptance)
			{
				return ReadOnlyMemory<byte>.Empty;
			}
		}
	}
}
