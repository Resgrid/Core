using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resgrid.Config;

namespace Resgrid.Web.Services.Health
{
	/// <summary>
	/// Full-health probe: proves the primary SQL database accepts connections and
	/// executes a query. Not part of the shallow /health liveness endpoint.
	/// </summary>
	public sealed class SqlDatabaseHealthCheck : IHealthCheck
	{
		private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

		public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
		{
			using var timeout = new CancellationTokenSource(ProbeTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

			try
			{
				await using var connection = new SqlConnection(DataConfig.ConnectionString);
				await connection.OpenAsync(linked.Token);

				await using var command = connection.CreateCommand();
				command.CommandText = "SELECT 1";
				await command.ExecuteScalarAsync(linked.Token);

				return HealthCheckResult.Healthy("Database connection and query succeeded.");
			}
			catch (OperationCanceledException) when (timeout.IsCancellationRequested)
			{
				return HealthCheckResult.Unhealthy($"Database probe timed out after {ProbeTimeout.TotalSeconds:0} seconds.");
			}
			catch (Exception ex)
			{
				return HealthCheckResult.Unhealthy("Database probe failed.", ex);
			}
		}
	}
}
