using System.Globalization;
using System.Text;

namespace Resgrid.TrackerGateway.Health
{
	public static class TrackingGatewayMetricsWriter
	{
		public static string Write(
			TrackingGatewayReadinessSnapshot snapshot,
			TrackingGatewayMetrics metrics = null,
			int? connectionLimit = null)
		{
			var builder = new StringBuilder();
			builder.AppendLine(
				"# HELP resgrid_tracker_gateway_listeners_expected Required tracking socket listeners.");
			builder.AppendLine(
				"# TYPE resgrid_tracker_gateway_listeners_expected gauge");
			builder.Append("resgrid_tracker_gateway_listeners_expected ");
			builder.AppendLine(
				snapshot.ExpectedListeners.ToString(CultureInfo.InvariantCulture));
			builder.AppendLine(
				"# HELP resgrid_tracker_gateway_listeners_bound Currently bound tracking socket listeners.");
			builder.AppendLine(
				"# TYPE resgrid_tracker_gateway_listeners_bound gauge");
			builder.Append("resgrid_tracker_gateway_listeners_bound ");
			builder.AppendLine(
				snapshot.BoundListeners.ToString(CultureInfo.InvariantCulture));
			builder.AppendLine(
				"# HELP resgrid_tracker_gateway_ready Whether all required listeners are ready.");
			builder.AppendLine("# TYPE resgrid_tracker_gateway_ready gauge");
			builder.Append("resgrid_tracker_gateway_ready ");
			builder.AppendLine(snapshot.IsReady ? "1" : "0");
			if (connectionLimit > 0)
			{
				builder.AppendLine(
					"# HELP resgrid_tracker_gateway_connections_limit Configured global tracking session admission limit.");
				builder.AppendLine(
					"# TYPE resgrid_tracker_gateway_connections_limit gauge");
				builder.Append(
					"resgrid_tracker_gateway_connections_limit ");
				builder.AppendLine(
					connectionLimit.Value.ToString(
						CultureInfo.InvariantCulture));
			}
			metrics?.AppendPrometheus(builder);
			return builder.ToString();
		}
	}
}
