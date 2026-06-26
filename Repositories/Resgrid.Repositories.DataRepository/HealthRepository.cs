using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;

namespace Resgrid.Repositories.DataRepository
{
	public class HealthRepository : IHealthRepository
	{
		private readonly IConnectionProvider _connectionProvider;

		public HealthRepository(IConnectionProvider connectionProvider)
		{
			_connectionProvider = connectionProvider;
		}

		public async Task<string> GetDatabaseCurrentTime()
		{
			// Use the configured connection provider (SQL Server OR PostgreSQL) and a portable query.
			// CURRENT_TIMESTAMP is valid on both engines; the old hardcoded SqlConnection + GETDATE() always threw
			// on PostgreSQL-backed datacenters, so the health check reported DatabaseOnline = false there.
			const string query = "SELECT CURRENT_TIMESTAMP";

			try
			{
				using (var db = _connectionProvider.Create())
				{
					var results = await db.QueryAsync<DateTime>(query);
					var timestamp = results.FirstOrDefault();

					return timestamp == default(DateTime) ? null : timestamp.ToString("o");
				}
			}
			catch (Exception ex)
			{
				// Log so a failing health check (DatabaseOnline = false) is diagnosable instead of silently swallowed;
				// keep returning null so the caller still reports the DB as offline rather than throwing.
				Logging.LogException(ex, "HealthRepository.GetDatabaseCurrentTime database connectivity check failed");
				return null;
			}
		}
	}
}
