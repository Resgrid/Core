using System;
using System.Buffers;
using System.Collections.Generic;
using Autofac;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Tracking;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.Providers.Tracking.Protocols.Gt06;
using Resgrid.Providers.Tracking.Protocols.Queclink;
using Resgrid.Providers.Tracking.Protocols.Teltonika;

namespace Resgrid.Tracking.Tests.Protocols
{
	[TestFixture]
	public class TrackingProtocolContractTests
	{
		[Test]
		public void ContractVersion_CurrentContract_IsVersionOne()
		{
			TrackingProtocolContract.Version.Should().Be(1);
		}

		[Test]
		public void Registry_MatchingProtocolAndTransport_ResolvesModule()
		{
			// Arrange
			var module = new TestProtocolModule("synthetic-v1", TrackingSocketTransport.Tcp);
			var registry = new TrackingProtocolModuleRegistry(new[] { module });

			// Act
			var resolved = registry.Resolve(" SYNTHETIC-V1 ", TrackingSocketTransport.Tcp);

			// Assert
			resolved.Should().BeSameAs(module);
		}

		[Test]
		public void Registry_UnsupportedTransport_DoesNotResolveModule()
		{
			// Arrange
			var registry = new TrackingProtocolModuleRegistry(new[]
			{
				new TestProtocolModule("synthetic-v1", TrackingSocketTransport.Tcp)
			});

			// Act
			var resolved = registry.TryResolve(
				"synthetic-v1",
				TrackingSocketTransport.Udp,
				out var module);

			// Assert
			resolved.Should().BeFalse();
			module.Should().BeNull();
		}

		[Test]
		public void Registry_DuplicateProtocolKeys_RejectsAmbiguousRegistration()
		{
			// Arrange
			var modules = new ITrackingProtocolModule[]
			{
				new TestProtocolModule("synthetic-v1", TrackingSocketTransport.Tcp),
				new TestProtocolModule("SYNTHETIC-V1", TrackingSocketTransport.Udp)
			};

			// Act
			Action act = () => new TrackingProtocolModuleRegistry(modules);

			// Assert
			act.Should().Throw<InvalidOperationException>()
				.WithMessage("*registered more than once*");
		}

		[Test]
		public void Registry_ModuleWithoutTransport_RejectsRegistration()
		{
			// Arrange
			var module = new TestProtocolModule("synthetic-v1");

			// Act
			Action act = () => new TrackingProtocolModuleRegistry(new[] { module });

			// Assert
			act.Should().Throw<InvalidOperationException>()
				.WithMessage("*at least one transport*");
		}

		[Test]
		public void Registry_ModuleWithUnknownTransport_RejectsRegistration()
		{
			// Arrange
			var module = new TestProtocolModule(
				"synthetic-v1",
				TrackingSocketTransport.Unknown);

			// Act
			Action act = () => new TrackingProtocolModuleRegistry(new[] { module });

			// Assert
			act.Should().Throw<InvalidOperationException>()
				.WithMessage("*unsupported transport*");
		}

		[Test]
		public void TrackingProviderModule_ResolvesRegisteredProtocolModules()
		{
			// Arrange
			var builder = new ContainerBuilder();
			builder.RegisterModule(new TrackingProviderModule());

			// Act
			using var container = builder.Build();
			var registry = container.Resolve<ITrackingProtocolModuleRegistry>();

			// Assert
			registry.Modules.Should().HaveCount(3);
			registry.Resolve(
					TrackingProtocolKeys.Queclink,
					TrackingSocketTransport.Tcp)
				.Should()
				.BeOfType<QueclinkProtocolModule>();
			registry.Resolve(
					TrackingProtocolKeys.Gt06,
					TrackingSocketTransport.Tcp)
				.Should()
				.BeOfType<Gt06ProtocolModule>();
			registry.Resolve(
					TeltonikaCodec8ProtocolModule.Key,
					TrackingSocketTransport.Tcp)
				.Should()
				.BeOfType<TeltonikaCodec8ProtocolModule>();
			registry.Resolve(
					TeltonikaCodec8ProtocolModule.Key,
					TrackingSocketTransport.Udp)
				.Should()
				.BeOfType<TeltonikaCodec8ProtocolModule>();
		}

		private sealed class TestProtocolModule : ITrackingProtocolModule
		{
			public TestProtocolModule(
				string protocolKey,
				params TrackingSocketTransport[] supportedTransports)
			{
				ProtocolKey = protocolKey;
				SupportedTransports = new HashSet<TrackingSocketTransport>(supportedTransports);
			}

			public string ProtocolKey { get; }
			public IReadOnlySet<TrackingSocketTransport> SupportedTransports { get; }

			public ITrackingProtocolSession CreateSession(TrackingSessionContext context)
			{
				return new TestProtocolSession();
			}
		}

		private sealed class TestProtocolSession : ITrackingProtocolSession
		{
			public ProtocolParseResult Parse(ref ReadOnlySequence<byte> input)
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
