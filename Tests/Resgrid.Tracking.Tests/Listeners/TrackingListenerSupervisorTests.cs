using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;
using Resgrid.TrackerGateway.Listeners;

namespace Resgrid.Tracking.Tests.Listeners
{
	[TestFixture]
	public class TrackingListenerSupervisorTests
	{
		[Test]
		public async Task StartAndStopAsync_ListenersBindAndStop_UpdatesReadiness()
		{
			// Arrange
			var plan = new TrackingListenerPlan(new[]
			{
				new TrackingListenerDefinition(
					"synthetic-v1",
					TrackingSocketTransport.Tcp,
					5000),
				new TrackingListenerDefinition(
					"synthetic-v1",
					TrackingSocketTransport.Udp,
					5000)
			});
			var settings = new TrackingGatewaySettings(
				trackingEnabled: true,
				nativeGatewayEnabled: true,
				credentialPepper: "test-pepper",
				tcpIdleTimeoutSeconds: 300,
				maxFrameBytes: 65536,
				maxConnections: 5000,
				maxConnectionsPerIp: 100,
				gracefulShutdownSeconds: 5,
				internalHealthPort: 8080,
				protocols: Array.Empty<TrackingProtocolListenerSettings>());
			var factory = new TestTrackingListenerFactory();
			var readiness = new TrackingGatewayReadinessState();
			var supervisor = new TrackingListenerSupervisor(
				plan,
				factory,
				settings,
				readiness,
				NullLogger<TrackingListenerSupervisor>.Instance);

			// Act
			await supervisor.StartAsync(CancellationToken.None);
			await factory.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

			// Assert
			readiness.GetSnapshot().IsReady.Should().BeTrue();
			readiness.GetSnapshot().BoundListeners.Should().Be(2);

			// Act
			await supervisor.StopAsync(CancellationToken.None);

			// Assert
			readiness.GetSnapshot().IsReady.Should().BeFalse();
			readiness.GetSnapshot().BoundListeners.Should().Be(0);
			factory.Listeners.Should().OnlyContain(listener => listener.StopCount == 1);
		}

		private sealed class TestTrackingListenerFactory :
			ITrackingListenerFactory
		{
			private readonly object _syncRoot = new object();

			public List<TestTrackingListener> Listeners { get; } =
				new List<TestTrackingListener>();
			public TaskCompletionSource AllStarted { get; } =
				new TaskCompletionSource(
					TaskCreationOptions.RunContinuationsAsynchronously);

			public bool Supports(TrackingListenerDefinition definition)
			{
				return true;
			}

			public ITrackingListener Create(
				TrackingListenerDefinition definition)
			{
				var listener = new TestTrackingListener(
					definition,
					OnStarted);
				lock (_syncRoot)
				{
					Listeners.Add(listener);
				}

				return listener;
			}

			private void OnStarted()
			{
				lock (_syncRoot)
				{
					if (Listeners.Count == 2 &&
					    Listeners.TrueForAll(listener => listener.IsBound))
						AllStarted.TrySetResult();
				}
			}
		}

		private sealed class TestTrackingListener : ITrackingListener
		{
			private readonly Action _onStarted;
			private readonly TaskCompletionSource _completion =
				new TaskCompletionSource(
					TaskCreationOptions.RunContinuationsAsynchronously);

			public TestTrackingListener(
				TrackingListenerDefinition definition,
				Action onStarted)
			{
				Definition = definition;
				_onStarted = onStarted;
			}

			public TrackingListenerDefinition Definition { get; }
			public bool IsBound { get; private set; }
			public Task Completion => _completion.Task;
			public int StopCount { get; private set; }

			public Task StartAsync(CancellationToken cancellationToken)
			{
				IsBound = true;
				_onStarted();
				return Task.CompletedTask;
			}

			public Task StopAsync(CancellationToken cancellationToken)
			{
				StopCount++;
				IsBound = false;
				_completion.TrySetResult();
				return Task.CompletedTask;
			}
		}
	}
}
