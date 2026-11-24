using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>CAD email update ledger (M0330), the call lookups by CAD call ID, and the per-ID lock.</summary>
	public class CallEmailUpdatesRepository : RmsRepositoryBase<CallEmailUpdate>, ICallEmailUpdatesRepository
	{
		/// <summary>pg_try_advisory_lock key space for CAD call IDs (two-int form: space, hash).</summary>
		private const int CadCallLockSpace = 47_330;

		public CallEmailUpdatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<IEnumerable<CallEmailUpdate>> GetByCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default)
		{
			var sql = $"SELECT * FROM {Tbl("CallEmailUpdates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId " +
				$"ORDER BY {Col("ReceivedOn")}";

			return await QueryAsync<CallEmailUpdate>(sql, new { DepartmentId = departmentId, CallId = callId }, cancellationToken);
		}

		public async Task<IEnumerable<CallEmailUpdate>> GetByMessageAsync(int departmentId, string cadCallId, string messageId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(cadCallId) || string.IsNullOrWhiteSpace(messageId))
				return Enumerable.Empty<CallEmailUpdate>();

			// IX_CallEmailUpdates_Department_CadCallId narrows it; an email always reads as the same CAD call ID.
			var sql = $"SELECT * FROM {Tbl("CallEmailUpdates")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CadCallId")} = {CaseInsensitive("CadCallId")} " +
				$"AND {Col("MessageId")} = {CaseInsensitive("MessageId")} ORDER BY {Col("ReceivedOn")}";

			return await QueryAsync<CallEmailUpdate>(sql, new { DepartmentId = departmentId, CadCallId = cadCallId.Trim(), MessageId = messageId }, cancellationToken);
		}

		public async Task<bool> SetOutcomeAsync(string callEmailUpdateId, int fromOutcome, int toOutcome, CancellationToken cancellationToken = default)
		{
			var sql = $"UPDATE {Tbl("CallEmailUpdates")} SET {Col("Outcome")} = {P}ToOutcome WHERE {Col("CallEmailUpdateId")} = {CaseInsensitive("Id")} " +
				$"AND {Col("Outcome")} = {P}FromOutcome";

			return await ExecuteAsync(sql, new { Id = callEmailUpdateId, FromOutcome = fromOutcome, ToOutcome = toOutcome }, cancellationToken) == 1;
		}

		public async Task<IEnumerable<Call>> GetOpenCallsForCadCallIdAsync(int departmentId, string cadCallId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(cadCallId))
				return Enumerable.Empty<Call>();

			var sql = $"SELECT * FROM {Tbl("Calls")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} " +
				$"AND {Col("State")} IN ({(int)CallStates.Active}, {(int)CallStates.Pending}) AND ({KeyMatch()})";

			return await RunAsync(conn => conn.QueryAsync<Call>(new Dapper.CommandDefinition(sql, KeyParameters(departmentId, cadCallId),
				UnitOfWork?.Transaction, cancellationToken: cancellationToken)), cancellationToken);
		}

		public async Task<IEnumerable<Call>> GetClosedCallsForCadCallIdAsync(int departmentId, string cadCallId, DateTime closedSinceUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(cadCallId))
				return Enumerable.Empty<Call>();

			var sql = $"SELECT * FROM {Tbl("Calls")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} " +
				$"AND {Col("State")} NOT IN ({(int)CallStates.Active}, {(int)CallStates.Pending}) AND {Col("ClosedOn")} >= {P}ClosedSince AND ({KeyMatch()})";

			var parameters = KeyParameters(departmentId, cadCallId);
			parameters.Add("ClosedSince", DatabaseTimestamp(closedSinceUtc));

			return await RunAsync(conn => conn.QueryAsync<Call>(new Dapper.CommandDefinition(sql, parameters, UnitOfWork?.Transaction,
				cancellationToken: cancellationToken)), cancellationToken);
		}

		public async Task<IAsyncDisposable> AcquireCadCallLockAsync(int departmentId, string cadCallId, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			var resource = "Resgrid.CadEmail." + departmentId.ToString(CultureInfo.InvariantCulture) + "." + Sha256Hex(cadCallId ?? string.Empty);
			var connection = ConnectionProvider.Create();
			try
			{
				await connection.OpenAsync(cancellationToken);

				if (IsPostgres)
				{
					var key = StableHash(resource);
					var waited = System.Diagnostics.Stopwatch.StartNew();
					while (true)
					{
						if (await connection.ExecuteScalarAsync<bool>(new Dapper.CommandDefinition("SELECT pg_try_advisory_lock(@Space, @Key)",
								new { Space = CadCallLockSpace, Key = key }, cancellationToken: cancellationToken)))
							return new SessionLock(connection, async c => await c.ExecuteScalarAsync<bool>("SELECT pg_advisory_unlock(@Space, @Key)", new { Space = CadCallLockSpace, Key = key }));

						if (waited.Elapsed >= timeout)
						{
							await connection.DisposeAsync();
							return null;
						}

						await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(250, Math.Max(10, (timeout - waited.Elapsed).TotalMilliseconds))), cancellationToken);
					}
				}

				// sp_getapplock answers 0 or 1 when granted and a negative code on timeout, cancellation or deadlock.
				var result = await connection.ExecuteScalarAsync<int>(new Dapper.CommandDefinition(
					"DECLARE @Result int; EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @TimeoutMs; SELECT @Result;",
					new { Resource = resource, TimeoutMs = (int)Math.Max(0, timeout.TotalMilliseconds) },
					commandTimeout: (int)Math.Ceiling(timeout.TotalSeconds) + 30, cancellationToken: cancellationToken));
				if (result < 0)
				{
					await connection.DisposeAsync();
					return null;
				}

				return new SessionLock(connection, async c => await c.ExecuteAsync(
					"EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';", new { Resource = resource }));
			}
			catch
			{
				await connection.DisposeAsync();
				throw;
			}
		}

		private string False => IsPostgres ? "false" : "0";

		/// <summary>The call's external identifier or incident number is the ID, or a ledger row ties the call to it.</summary>
		private string KeyMatch()
		{
			return $"LOWER(TRIM({Col("ExternalIdentifier")})) = LOWER({P}CadCallId) OR LOWER(TRIM({Col("IncidentNumber")})) = LOWER({P}CadCallId) " +
				$"OR {Col("CallId")} IN (SELECT u.{Col("CallId")} FROM {Tbl("CallEmailUpdates")} u WHERE u.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND u.{Col("CadCallId")} = {CaseInsensitive("CadCallId")} AND u.{Col("CallId")} IS NOT NULL)";
		}

		private static DynamicParameters KeyParameters(int departmentId, string cadCallId)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("CadCallId", cadCallId.Trim());
			return parameters;
		}

		private static string Sha256Hex(string value)
		{
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
			return string.Concat(bytes.Take(16).Select(b => b.ToString("x2")));
		}

		private static int StableHash(string value)
		{
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
			return BitConverter.ToInt32(bytes, 0);
		}

		/// <summary>The held lock: releases it, then closes its connection (a pooled connection must not keep a session lock).</summary>
		private sealed class SessionLock : IAsyncDisposable
		{
			private DbConnection _connection;
			private readonly Func<DbConnection, Task> _release;

			public SessionLock(DbConnection connection, Func<DbConnection, Task> release)
			{
				_connection = connection;
				_release = release;
			}

			public async ValueTask DisposeAsync()
			{
				var connection = Interlocked.Exchange(ref _connection, null);
				if (connection == null)
					return;

				try
				{
					await _release(connection);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Releasing a CAD email call lock");

					// The lock may still be held by this session: a pooled connection keeps its session (and a session lock) when it
					// is handed back, so the pool is told to discard it instead; the session ends and the server drops the lock.
					DiscardSession(connection);
				}
				finally
				{
					await connection.DisposeAsync();
				}
			}

			private static void DiscardSession(DbConnection connection)
			{
				try
				{
					if (connection is Npgsql.NpgsqlConnection postgres)
						Npgsql.NpgsqlConnection.ClearPool(postgres);
					else if (connection is Microsoft.Data.SqlClient.SqlConnection sqlServer)
						Microsoft.Data.SqlClient.SqlConnection.ClearPool(sqlServer);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Discarding the session of a CAD email call lock that could not be released");
				}
			}
		}
	}
}
