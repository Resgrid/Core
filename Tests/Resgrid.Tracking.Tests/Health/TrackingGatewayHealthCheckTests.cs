using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;
using Resgrid.Model.Tracking;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Health;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.Tracking.Tests.Health
{
	[TestFixture]
	public class TrackingGatewayHealthCheckTests
	{
		[Test]
		public async Task CheckHealthAsync_RequiredListenerNotBound_IsUnhealthy()
		{
			// Arrange
			var definition = new TrackingListenerDefinition(
				"synthetic-v1",
				TrackingSocketTransport.Tcp,
				5000);
			var state = new TrackingGatewayReadinessState();
			state.Initialize(new TrackingListenerPlan(new[] { definition }));
			var healthCheck = new TrackingGatewayReadinessHealthCheck(state);

			// Act
			var result = await healthCheck.CheckHealthAsync(
				new HealthCheckContext());

			// Assert
			result.Status.Should().Be(HealthStatus.Unhealthy);
		}

		[Test]
		public async Task CheckHealthAsync_AllRequiredListenersBound_IsHealthy()
		{
			// Arrange
			var definition = new TrackingListenerDefinition(
				"synthetic-v1",
				TrackingSocketTransport.Tcp,
				5000);
			var state = new TrackingGatewayReadinessState();
			state.Initialize(new TrackingListenerPlan(new[] { definition }));
			state.MarkBound(definition);
			var healthCheck = new TrackingGatewayReadinessHealthCheck(state);

			// Act
			var result = await healthCheck.CheckHealthAsync(
				new HealthCheckContext());

			// Assert
			result.Status.Should().Be(HealthStatus.Healthy);
		}

		[Test]
		public void MetricsWriter_ReadinessSnapshot_EmitsBoundedGatewayGauges()
		{
			// Arrange
			var snapshot = new TrackingGatewayReadinessSnapshot(
				expectedListeners: 2,
				boundListeners: 1,
				isReady: false,
				hasFailure: false);

			// Act
			var metrics = TrackingGatewayMetricsWriter.Write(snapshot);

			// Assert
			metrics.Should().Contain(
				"resgrid_tracker_gateway_listeners_expected 2");
			metrics.Should().Contain(
				"resgrid_tracker_gateway_listeners_bound 1");
			metrics.Should().Contain(
				"resgrid_tracker_gateway_ready 0");
			metrics.Should().NotContain(
				"resgrid_tracker_gateway_connections_limit");
		}

		[Test]
		public void MetricsWriter_ConfiguredConnectionLimit_EmitsCapacityGauge()
		{
			// Arrange
			var snapshot = new TrackingGatewayReadinessSnapshot(
				expectedListeners: 1,
				boundListeners: 1,
				isReady: true,
				hasFailure: false);

			// Act
			var metrics = TrackingGatewayMetricsWriter.Write(
				snapshot,
				connectionLimit: 2500);

			// Assert
			metrics.Should().Contain(
				"resgrid_tracker_gateway_connections_limit 2500");
		}

		[Test]
		public void MetricsWriter_TrackingActivity_EmitsBoundedLabelsAndHistograms()
		{
			// Arrange
			var definition = new TrackingListenerDefinition(
				"synthetic-v1",
				TrackingSocketTransport.Tcp,
				5000);
			var activity = new TrackingGatewayMetrics();
			activity.ConnectionStarted(definition);
			activity.ConnectionCompleted(
				definition,
				"completed");
			activity.RecordIngressMessage(
				definition,
				new ProtocolMessage
				{
					MessageType = ProtocolMessageType.Positions,
					Positions =
						new List<CanonicalTrackingPosition>
						{
							new CanonicalTrackingPosition()
						}
				},
				new TrackingAcceptance
				{
					Status = TrackingAcceptanceStatus.Accepted,
					AcceptedPositions = 1
				});
			activity.RecordParseFailure(
				definition.ProtocolKey,
				"device-specific-parser-reason");
			activity.RecordAuthFailure(
				definition.Transport,
				"device-specific-auth-reason");
			activity.ObserveQueuePublishDuration(
				definition.Transport,
				TimeSpan.FromMilliseconds(50));
			activity.ObserveFrameBytes(
				definition.ProtocolKey,
				128);
			activity.ObserveSessionDuration(
				definition.ProtocolKey,
				TimeSpan.FromSeconds(2));

			// Act
			var metrics = TrackingGatewayMetricsWriter.Write(
				new TrackingGatewayReadinessSnapshot(
					expectedListeners: 1,
					boundListeners: 1,
					isReady: true,
					hasFailure: false),
				activity);

			// Assert
			metrics.Should().Contain(
				"resgrid_tracking_connections_current{protocol=\"synthetic-v1\",transport=\"tcp\"} 0");
			metrics.Should().Contain(
				"resgrid_tracking_connections_total{protocol=\"synthetic-v1\",outcome=\"completed\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_ingress_messages_total{transport=\"tcp\",protocol=\"synthetic-v1\",outcome=\"accepted\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_positions_total{transport=\"tcp\",protocol=\"synthetic-v1\",outcome=\"accepted\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_parse_failures_total{protocol=\"synthetic-v1\",reason=\"other\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_auth_failures_total{transport=\"tcp\",reason=\"other\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_queue_publish_duration_seconds_count{transport=\"tcp\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_frame_bytes_count{protocol=\"synthetic-v1\"} 1");
			metrics.Should().Contain(
				"resgrid_tracking_session_duration_seconds_count{protocol=\"synthetic-v1\"} 1");
			metrics.Should().NotContain(
				"device-specific");
		}
	}
}
