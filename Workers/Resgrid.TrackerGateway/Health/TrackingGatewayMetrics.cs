using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Resgrid.Providers.Tracking.Protocols;
using Resgrid.TrackerGateway.Hosting;

namespace Resgrid.TrackerGateway.Health
{
	public sealed class TrackingGatewayMetrics
	{
		private static readonly double[] QueuePublishDurationBuckets =
		{
			0.005,
			0.01,
			0.025,
			0.05,
			0.1,
			0.25,
			0.5,
			1,
			2.5,
			5,
			10
		};

		private static readonly double[] FrameByteBuckets =
		{
			64,
			128,
			256,
			512,
			1024,
			4096,
			16384,
			65536,
			262144,
			1048576
		};

		private static readonly double[] SessionDurationBuckets =
		{
			1,
			5,
			15,
			30,
			60,
			300,
			900,
			1800,
			3600
		};

		private readonly ConcurrentDictionary<MetricKey, long>
			_currentConnections = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, long>
			_connections = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, long>
			_ingressMessages = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, long>
			_positions = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, long>
			_parseFailures = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, long>
			_authFailures = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, long>
			_forcedShutdowns = new ConcurrentDictionary<MetricKey, long>();
		private readonly ConcurrentDictionary<MetricKey, TrackingHistogram>
			_queuePublishDurations =
				new ConcurrentDictionary<MetricKey, TrackingHistogram>();
		private readonly ConcurrentDictionary<MetricKey, TrackingHistogram>
			_frameBytes =
				new ConcurrentDictionary<MetricKey, TrackingHistogram>();
		private readonly ConcurrentDictionary<MetricKey, TrackingHistogram>
			_sessionDurations =
				new ConcurrentDictionary<MetricKey, TrackingHistogram>();

		public void ConnectionStarted(
			TrackingListenerDefinition definition)
		{
			var key = new MetricKey(
				definition.ProtocolKey,
				Transport(definition.Transport));
			_currentConnections.AddOrUpdate(
				key,
				1,
				(_, current) => current + 1);
		}

		public void ConnectionCompleted(
			TrackingListenerDefinition definition,
			string outcome)
		{
			var currentKey = new MetricKey(
				definition.ProtocolKey,
				Transport(definition.Transport));
			_currentConnections.AddOrUpdate(
				currentKey,
				0,
				(_, current) => Math.Max(0, current - 1));
			Increment(
				_connections,
				new MetricKey(
					definition.ProtocolKey,
					ConnectionOutcome(outcome)));
		}

		public void ConnectionRejected(
			TrackingListenerDefinition definition)
		{
			Increment(
				_connections,
				new MetricKey(
					definition.ProtocolKey,
					"admission-rejected"));
		}

		public void RecordIngressMessage(
			TrackingListenerDefinition definition,
			ProtocolMessage message,
			TrackingAcceptance acceptance)
		{
			var outcome = AcceptanceOutcome(acceptance);
			Increment(
				_ingressMessages,
				new MetricKey(
					Transport(definition.Transport),
					definition.ProtocolKey,
					outcome));

			if (message?.MessageType != ProtocolMessageType.Positions)
				return;

			var positionCount = acceptance?.Status ==
			                    TrackingAcceptanceStatus.Accepted
				? acceptance.AcceptedPositions
				: message.Positions?.Count ?? 0;
			if (positionCount <= 0)
				return;

			Increment(
				_positions,
				new MetricKey(
					Transport(definition.Transport),
					definition.ProtocolKey,
					outcome),
				positionCount);
		}

		public void RecordParseFailure(
			string protocolKey,
			string reason)
		{
			Increment(
				_parseFailures,
				new MetricKey(
					protocolKey,
					ParseFailureReason(reason)));
		}

		public void RecordAuthFailure(
			TrackingSocketTransport transport,
			string reason)
		{
			Increment(
				_authFailures,
				new MetricKey(
					Transport(transport),
					AuthFailureReason(reason)));
		}

		public void ObserveQueuePublishDuration(
			TrackingSocketTransport transport,
			TimeSpan duration)
		{
			Observe(
				_queuePublishDurations,
				new MetricKey(Transport(transport)),
				QueuePublishDurationBuckets,
				duration.TotalSeconds);
		}

		public void ObserveFrameBytes(
			string protocolKey,
			int frameBytes)
		{
			if (frameBytes < 0)
				return;

			Observe(
				_frameBytes,
				new MetricKey(protocolKey),
				FrameByteBuckets,
				frameBytes);
		}

		public void ObserveSessionDuration(
			string protocolKey,
			TimeSpan duration)
		{
			Observe(
				_sessionDurations,
				new MetricKey(protocolKey),
				SessionDurationBuckets,
				duration.TotalSeconds);
		}

		public void RecordForcedShutdown(
			TrackingListenerDefinition definition)
		{
			Increment(
				_forcedShutdowns,
				new MetricKey(
					definition.ProtocolKey,
					Transport(definition.Transport)));
		}

		internal void AppendPrometheus(StringBuilder builder)
		{
			AppendMetricHeader(
				builder,
				"resgrid_tracking_connections_current",
				"Current admitted tracking sessions.",
				"gauge");
			AppendSamples(
				builder,
				"resgrid_tracking_connections_current",
				_currentConnections,
				"protocol",
				"transport");

			AppendMetricHeader(
				builder,
				"resgrid_tracking_connections_total",
				"Tracking session attempts by terminal outcome.",
				"counter");
			AppendSamples(
				builder,
				"resgrid_tracking_connections_total",
				_connections,
				"protocol",
				"outcome");

			AppendMetricHeader(
				builder,
				"resgrid_tracking_ingress_messages_total",
				"Parsed tracking messages by canonical ingress outcome.",
				"counter");
			AppendSamples(
				builder,
				"resgrid_tracking_ingress_messages_total",
				_ingressMessages,
				"transport",
				"protocol",
				"outcome");

			AppendMetricHeader(
				builder,
				"resgrid_tracking_positions_total",
				"Tracking positions by canonical ingress outcome.",
				"counter");
			AppendSamples(
				builder,
				"resgrid_tracking_positions_total",
				_positions,
				"transport",
				"protocol",
				"outcome");

			AppendMetricHeader(
				builder,
				"resgrid_tracking_parse_failures_total",
				"Tracking protocol parse failures by bounded reason.",
				"counter");
			AppendSamples(
				builder,
				"resgrid_tracking_parse_failures_total",
				_parseFailures,
				"protocol",
				"reason");

			AppendMetricHeader(
				builder,
				"resgrid_tracking_auth_failures_total",
				"Native tracking mapping and source-policy failures.",
				"counter");
			AppendSamples(
				builder,
				"resgrid_tracking_auth_failures_total",
				_authFailures,
				"transport",
				"reason");

			AppendHistogram(
				builder,
				"resgrid_tracking_queue_publish_duration_seconds",
				"Canonical position ingress duration before acknowledgement.",
				_queuePublishDurations,
				"transport");
			AppendHistogram(
				builder,
				"resgrid_tracking_frame_bytes",
				"Complete native tracking frame size in bytes.",
				_frameBytes,
				"protocol");
			AppendHistogram(
				builder,
				"resgrid_tracking_session_duration_seconds",
				"Native TCP tracking session duration.",
				_sessionDurations,
				"protocol");

			AppendMetricHeader(
				builder,
				"resgrid_tracking_shutdown_forced_total",
				"Listener shutdowns that exhausted the graceful drain deadline.",
				"counter");
			AppendSamples(
				builder,
				"resgrid_tracking_shutdown_forced_total",
				_forcedShutdowns,
				"protocol",
				"transport");
		}

		private static void Increment(
			ConcurrentDictionary<MetricKey, long> counters,
			MetricKey key,
			long value = 1)
		{
			counters.AddOrUpdate(
				key,
				value,
				(_, current) => current + value);
		}

		private static void Observe(
			ConcurrentDictionary<MetricKey, TrackingHistogram> histograms,
			MetricKey key,
			double[] buckets,
			double value)
		{
			histograms.GetOrAdd(
					key,
					_ => new TrackingHistogram(buckets))
				.Observe(Math.Max(0, value));
		}

		private static string Transport(
			TrackingSocketTransport transport)
		{
			return transport switch
			{
				TrackingSocketTransport.Tcp => "tcp",
				TrackingSocketTransport.Udp => "udp",
				_ => "unknown"
			};
		}

		private static string AcceptanceOutcome(
			TrackingAcceptance acceptance)
		{
			return acceptance?.Status switch
			{
				TrackingAcceptanceStatus.Accepted => "accepted",
				TrackingAcceptanceStatus.Rejected => "rejected",
				TrackingAcceptanceStatus.Unavailable => "unavailable",
				_ => "unknown"
			};
		}

		private static string ConnectionOutcome(string outcome)
		{
			return outcome switch
			{
				"completed" => "completed",
				"cancelled" => "cancelled",
				"failed" => "failed",
				_ => "unknown"
			};
		}

		private static string ParseFailureReason(string reason)
		{
			return reason switch
			{
				"close-session" => "close-session",
				"frame-too-large" => "frame-too-large",
				"incomplete-datagram" => "incomplete-datagram",
				"invalid-result" => "invalid-result",
				"malformed" => "malformed",
				"parser-exception" => "parser-exception",
				"unsupported" => "unsupported",
				_ => "other"
			};
		}

		private static string AuthFailureReason(string reason)
		{
			return reason switch
			{
				"device-not-found" => "device-not-found",
				"identifier-changed" => "identifier-changed",
				"identifier-required" => "identifier-required",
				"mapping-unavailable" => "mapping-unavailable",
				"source-not-allowed" => "source-not-allowed",
				_ => "other"
			};
		}

		private static void AppendMetricHeader(
			StringBuilder builder,
			string name,
			string help,
			string type)
		{
			builder.Append("# HELP ");
			builder.Append(name);
			builder.Append(' ');
			builder.AppendLine(help);
			builder.Append("# TYPE ");
			builder.Append(name);
			builder.Append(' ');
			builder.AppendLine(type);
		}

		private static void AppendSamples(
			StringBuilder builder,
			string name,
			ConcurrentDictionary<MetricKey, long> samples,
			params string[] labelNames)
		{
			foreach (var sample in samples
				         .OrderBy(item => item.Key.SortKey,
					         StringComparer.Ordinal))
			{
				AppendSamplePrefix(
					builder,
					name,
					sample.Key,
					labelNames);
				builder.AppendLine(
					sample.Value.ToString(
						CultureInfo.InvariantCulture));
			}
		}

		private static void AppendHistogram(
			StringBuilder builder,
			string name,
			string help,
			ConcurrentDictionary<MetricKey, TrackingHistogram> histograms,
			params string[] labelNames)
		{
			AppendMetricHeader(
				builder,
				name,
				help,
				"histogram");
			foreach (var histogram in histograms
				         .OrderBy(item => item.Key.SortKey,
					         StringComparer.Ordinal))
			{
				var snapshot = histogram.Value.GetSnapshot();
				long cumulativeCount = 0;
				for (var index = 0;
				     index < snapshot.Buckets.Length;
				     index++)
				{
					cumulativeCount += snapshot.BucketCounts[index];
					AppendHistogramBucketPrefix(
						builder,
						name,
						histogram.Key,
						labelNames,
						snapshot.Buckets[index].ToString(
							"0.#################",
							CultureInfo.InvariantCulture));
					builder.AppendLine(
						cumulativeCount.ToString(
							CultureInfo.InvariantCulture));
				}

				cumulativeCount +=
					snapshot.BucketCounts[snapshot.Buckets.Length];
				AppendHistogramBucketPrefix(
					builder,
					name,
					histogram.Key,
					labelNames,
					"+Inf");
				builder.AppendLine(
					cumulativeCount.ToString(
						CultureInfo.InvariantCulture));

				AppendSamplePrefix(
					builder,
					name + "_sum",
					histogram.Key,
					labelNames);
				builder.AppendLine(
					snapshot.Sum.ToString(
						"G17",
						CultureInfo.InvariantCulture));
				AppendSamplePrefix(
					builder,
					name + "_count",
					histogram.Key,
					labelNames);
				builder.AppendLine(
					snapshot.Count.ToString(
						CultureInfo.InvariantCulture));
			}
		}

		private static void AppendHistogramBucketPrefix(
			StringBuilder builder,
			string name,
			MetricKey key,
			IReadOnlyList<string> labelNames,
			string upperBound)
		{
			builder.Append(name);
			builder.Append("_bucket{");
			AppendLabels(
				builder,
				key,
				labelNames);
			if (labelNames.Count > 0)
				builder.Append(',');
			builder.Append("le=\"");
			builder.Append(upperBound);
			builder.Append("\"} ");
		}

		private static void AppendSamplePrefix(
			StringBuilder builder,
			string name,
			MetricKey key,
			IReadOnlyList<string> labelNames)
		{
			builder.Append(name);
			if (labelNames.Count > 0)
			{
				builder.Append('{');
				AppendLabels(
					builder,
					key,
					labelNames);
				builder.Append('}');
			}

			builder.Append(' ');
		}

		private static void AppendLabels(
			StringBuilder builder,
			MetricKey key,
			IReadOnlyList<string> labelNames)
		{
			for (var index = 0;
			     index < labelNames.Count;
			     index++)
			{
				if (index > 0)
					builder.Append(',');
				builder.Append(labelNames[index]);
				builder.Append("=\"");
				builder.Append(EscapeLabel(key.Values[index]));
				builder.Append('"');
			}
		}

		private static string EscapeLabel(string value)
		{
			return (value ?? string.Empty)
				.Replace("\\", "\\\\")
				.Replace("\n", "\\n")
				.Replace("\"", "\\\"");
		}

		private readonly struct MetricKey : IEquatable<MetricKey>
		{
			public MetricKey(params string[] values)
			{
				Values = values ?? Array.Empty<string>();
				SortKey = string.Join(
					"\u001f",
					Values);
			}

			public string[] Values { get; }
			public string SortKey { get; }

			public bool Equals(MetricKey other)
			{
				return Values.SequenceEqual(
					other.Values,
					StringComparer.Ordinal);
			}

			public override bool Equals(object obj)
			{
				return obj is MetricKey other &&
				       Equals(other);
			}

			public override int GetHashCode()
			{
				var hash = new HashCode();
				foreach (var value in Values)
				{
					hash.Add(
						value,
						StringComparer.Ordinal);
				}

				return hash.ToHashCode();
			}
		}

		private sealed class TrackingHistogram
		{
			private readonly object _syncRoot = new object();
			private readonly double[] _buckets;
			private readonly long[] _bucketCounts;
			private long _count;
			private double _sum;

			public TrackingHistogram(double[] buckets)
			{
				_buckets = buckets;
				_bucketCounts =
					new long[buckets.Length + 1];
			}

			public void Observe(double value)
			{
				lock (_syncRoot)
				{
					var bucketIndex = _buckets.Length;
					for (var index = 0;
					     index < _buckets.Length;
					     index++)
					{
						if (value <= _buckets[index])
						{
							bucketIndex = index;
							break;
						}
					}

					_bucketCounts[bucketIndex]++;
					_count++;
					_sum += value;
				}
			}

			public TrackingHistogramSnapshot GetSnapshot()
			{
				lock (_syncRoot)
				{
					return new TrackingHistogramSnapshot(
						_buckets,
						(long[])_bucketCounts.Clone(),
						_count,
						_sum);
				}
			}
		}

		private sealed class TrackingHistogramSnapshot
		{
			public TrackingHistogramSnapshot(
				double[] buckets,
				long[] bucketCounts,
				long count,
				double sum)
			{
				Buckets = buckets;
				BucketCounts = bucketCounts;
				Count = count;
				Sum = sum;
			}

			public double[] Buckets { get; }
			public long[] BucketCounts { get; }
			public long Count { get; }
			public double Sum { get; }
		}
	}
}
