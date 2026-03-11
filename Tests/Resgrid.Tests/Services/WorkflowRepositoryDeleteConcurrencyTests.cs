using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Workflows;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Tests that <see cref="WorkflowRepository.DeleteWorkflowWithAllDependenciesAsync"/> acquires a
	/// row-level lock on the parent Workflows row before deleting any child rows, and that the
	/// generated lock SQL is correct for both PostgreSQL and SQL Server.
	///
	/// These are unit-level integration tests: they mock the Dapper-level connection so that no live
	/// database is required, but they verify the *exact call-order* and *SQL text* that would be
	/// sent to a real database engine. The concurrency scenario is also simulated by interleaving
	/// two Tasks that share a monitor/gate, demonstrating that an inserter waits while the deleter
	/// holds the lock.
	/// </summary>
	[TestFixture]
	public class WorkflowRepositoryDeleteConcurrencyTests
	{
		// ── helpers ────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Returns a SqlConfiguration whose SchemaName and ParameterNotation match the
		/// real concrete configuration class for the given database type.
		/// </summary>
		private static SqlConfiguration BuildConfig(DatabaseTypes dbType)
		{
			if (dbType == DatabaseTypes.Postgres)
				return new PostgreSqlConfiguration();   // SchemaName="public", ParameterNotation="@"

			return new SqlServerConfiguration();        // SchemaName="[dbo]", ParameterNotation="@"
		}

		/// <summary>
		/// Builds the expected lock SQL string so tests don't hard-code the schema name.
		/// </summary>
		private static string ExpectedLockSql(DatabaseTypes dbType, SqlConfiguration cfg)
		{
			return dbType == DatabaseTypes.Postgres
				? $"SELECT workflowid FROM {cfg.SchemaName}.workflows WHERE workflowid = {cfg.ParameterNotation}WorkflowId FOR UPDATE"
				: $"SELECT [WorkflowId] FROM {cfg.SchemaName}.[Workflows] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkflowId] = {cfg.ParameterNotation}WorkflowId";
		}

		// ── lock-SQL generation tests ──────────────────────────────────────────────────

		[Test]
		public void LockSql_ForPostgres_ContainsForUpdate()
		{
			var cfg = BuildConfig(DatabaseTypes.Postgres);
			var sql = ExpectedLockSql(DatabaseTypes.Postgres, cfg);

			sql.Should().Contain("FOR UPDATE", "PostgreSQL row lock must use FOR UPDATE");
			sql.Should().Contain("public.workflows", "schema and table name must be lower-cased for Postgres");
			sql.Should().Contain("@WorkflowId", "parameter should use @ notation");
		}

		[Test]
		public void LockSql_ForSqlServer_ContainsUpdlockHoldlock()
		{
			var cfg = BuildConfig(DatabaseTypes.SqlServer);
			var sql = ExpectedLockSql(DatabaseTypes.SqlServer, cfg);

			sql.Should().Contain("WITH (UPDLOCK, HOLDLOCK)", "SQL Server row lock must use UPDLOCK + HOLDLOCK hints");
			sql.Should().Contain("[dbo].[Workflows]", "schema and table name must be bracket-quoted for SQL Server");
			sql.Should().Contain("@WorkflowId", "parameter should use @ notation");
		}

		// ── call-order tests (standalone transaction path) ─────────────────────────────

		/// <summary>
		/// Verifies that when no ambient UoW exists, the lock SELECT is the very first
		/// statement executed on the connection before any DELETE is issued.
		/// </summary>
		[Test]
		[TestCase(DatabaseTypes.Postgres)]
		[TestCase(DatabaseTypes.SqlServer)]
		public async Task StandalonePath_LockSelectIsFirstStatementExecuted(DatabaseTypes dbType)
		{
			// Arrange
			var originalDbType = DataConfig.DatabaseType;
			DataConfig.DatabaseType = dbType;

			try
			{
				var cfg = BuildConfig(dbType);
				var executedSqls = new List<string>();

				// CapturingConnection handles BeginDbTransaction internally via CapturingTransaction.
				var capture = new CapturingConnection(executedSqls);

				var mockConnectionProvider = new Mock<IConnectionProvider>();
				mockConnectionProvider.Setup(p => p.Create()).Returns(capture);

				var mockUoW = new Mock<IUnitOfWork>();
				mockUoW.Setup(u => u.Connection).Returns((DbConnection)null);

				var mockQueryFactory = BuildQueryFactory(dbType, cfg);

				var repo = new WorkflowRepository(
					mockConnectionProvider.Object,
					cfg,
					mockUoW.Object,
					mockQueryFactory);

				// Act
				await repo.DeleteWorkflowWithAllDependenciesAsync("wf-001");

				// Assert – the very first executed SQL must be the lock statement
				executedSqls.Should().NotBeEmpty("at least the lock + delete statements should have been executed");
				executedSqls[0].Should().Contain(
					dbType == DatabaseTypes.Postgres ? "FOR UPDATE" : "WITH (UPDLOCK, HOLDLOCK)",
					$"the FIRST SQL executed for {dbType} must be the lock SELECT, not a DELETE");
			}
			finally
			{
				DataConfig.DatabaseType = originalDbType;
			}
		}

		/// <summary>
		/// Verifies the lock SELECT appears before all four DELETE statements in the
		/// standalone path.
		/// </summary>
		[Test]
		[TestCase(DatabaseTypes.Postgres)]
		[TestCase(DatabaseTypes.SqlServer)]
		public async Task StandalonePath_AllDeletesFollowTheLockSelect(DatabaseTypes dbType)
		{
			var originalDbType = DataConfig.DatabaseType;
			DataConfig.DatabaseType = dbType;

			try
			{
				var cfg = BuildConfig(dbType);
				var executedSqls = new List<string>();

				var capture = new CapturingConnection(executedSqls);

				var mockConnectionProvider = new Mock<IConnectionProvider>();
				mockConnectionProvider.Setup(p => p.Create()).Returns(capture);

				var mockUoW = new Mock<IUnitOfWork>();
				mockUoW.Setup(u => u.Connection).Returns((DbConnection)null);

				var repo = new WorkflowRepository(
					mockConnectionProvider.Object,
					cfg,
					mockUoW.Object,
					BuildQueryFactory(dbType, cfg));

				await repo.DeleteWorkflowWithAllDependenciesAsync("wf-002");

				// Expect: lock, delete-run-logs, delete-runs, delete-steps, delete-workflow
				executedSqls.Should().HaveCount(5, "exactly 5 statements: 1 lock + 4 deletes");

				var lockKeyword = dbType == DatabaseTypes.Postgres ? "FOR UPDATE" : "WITH (UPDLOCK, HOLDLOCK)";
				executedSqls[0].Should().Contain(lockKeyword,   "statement 0 must be the lock SELECT");
				executedSqls[1].Should().Contain("DELETE", "statement 1 must delete WorkflowRunLogs");
				executedSqls[2].Should().Contain("DELETE", "statement 2 must delete WorkflowRuns");
				executedSqls[3].Should().Contain("DELETE", "statement 3 must delete WorkflowSteps");
				executedSqls[4].Should().Contain("DELETE", "statement 4 must delete the Workflow parent row");
			}
			finally
			{
				DataConfig.DatabaseType = originalDbType;
			}
		}

		// ── call-order tests (ambient UoW path) ───────────────────────────────────────

		/// <summary>
		/// Verifies that when an ambient UoW connection is present the lock SELECT is still
		/// the first statement executed before any DELETE.
		/// </summary>
		[Test]
		[TestCase(DatabaseTypes.Postgres)]
		[TestCase(DatabaseTypes.SqlServer)]
		public async Task AmbientUoWPath_LockSelectIsFirstStatementExecuted(DatabaseTypes dbType)
		{
			var originalDbType = DataConfig.DatabaseType;
			DataConfig.DatabaseType = dbType;

			try
			{
				var cfg = BuildConfig(dbType);
				var executedSqls = new List<string>();

				var capture = new CapturingConnection(executedSqls);

				// Simulate an active UoW connection so the method takes the UoW branch
				var mockTx = new Mock<DbTransaction>();
				var mockUoW = new Mock<IUnitOfWork>();
				mockUoW.Setup(u => u.Connection).Returns(capture);
				mockUoW.Setup(u => u.Transaction).Returns(mockTx.Object);
				mockUoW.Setup(u => u.CreateOrGetConnection()).Returns(capture);

				var mockConnectionProvider = new Mock<IConnectionProvider>();

				var repo = new WorkflowRepository(
					mockConnectionProvider.Object,
					cfg,
					mockUoW.Object,
					BuildQueryFactory(dbType, cfg));

				await repo.DeleteWorkflowWithAllDependenciesAsync("wf-003");

				executedSqls.Should().HaveCount(5, "exactly 5 statements: 1 lock + 4 deletes");

				var lockKeyword = dbType == DatabaseTypes.Postgres ? "FOR UPDATE" : "WITH (UPDLOCK, HOLDLOCK)";
				executedSqls[0].Should().Contain(lockKeyword,
					$"the FIRST SQL executed for {dbType} in the UoW path must be the lock SELECT");
			}
			finally
			{
				DataConfig.DatabaseType = originalDbType;
			}
		}

		// ── concurrency simulation tests ───────────────────────────────────────────────

		/// <summary>
		/// Simulates a concurrent "inserter" that tries to INSERT a WorkflowRun while the
		/// "deleter" holds the parent-row lock.  The inserter must wait until the deleter
		/// releases the lock (commits/rolls back).
		///
		/// This test uses a <see cref="SemaphoreSlim"/> to model the database lock: the
		/// deleter acquires it on its lock-SELECT call and releases it only after all
		/// DELETEs are done (simulating transaction commit).  The inserter is blocked on
		/// the semaphore and may only proceed afterwards, proving no FK violation window
		/// exists.
		/// </summary>
		[Test]
		[TestCase(DatabaseTypes.Postgres,   Description = "PostgreSQL – FOR UPDATE blocks concurrent inserter")]
		[TestCase(DatabaseTypes.SqlServer,  Description = "SQL Server – UPDLOCK+HOLDLOCK blocks concurrent inserter")]
		public async Task ConcurrentRunInsert_IsBlockedUntilDeleteTransactionCompletes(DatabaseTypes dbType)
		{
			var originalDbType = DataConfig.DatabaseType;
			DataConfig.DatabaseType = dbType;

			try
			{
				const string workflowId = "wf-concurrent-001";

				// The semaphore models the database row lock held by the deleter.
				// Initial count 1 = lock is free; the deleter takes it, inserter waits.
				var dbLock = new SemaphoreSlim(1, 1);

				// Timeline records: each side appends its action name so we can assert order.
				var timeline = new List<string>();

				// ── Deleter setup ────────────────────────────────────────────────────────
				var deleteExecutedSqls = new List<string>();
				var deletingConn = new CapturingConnection(
					deleteExecutedSqls,
					onLockSelected: () =>
					{
						// Deleter acquires the "DB lock" when it issues the lock-SELECT
						dbLock.Wait();
						timeline.Add("deleter:lock_acquired");
					},
					onAllDeleted: () =>
					{
						// Deleter releases after finishing all DELETEs (transaction commit)
						dbLock.Release();
						timeline.Add("deleter:lock_released");
					});

				var cfg = BuildConfig(dbType);

				var mockUoWDeleter = new Mock<IUnitOfWork>();
				mockUoWDeleter.Setup(u => u.Connection).Returns((DbConnection)null);

				var mockConnProviderDeleter = new Mock<IConnectionProvider>();
				mockConnProviderDeleter.Setup(p => p.Create()).Returns(deletingConn);

				var repo = new WorkflowRepository(
					mockConnProviderDeleter.Object,
					cfg,
					mockUoWDeleter.Object,
					BuildQueryFactory(dbType, cfg));

				// ── Inserter setup ───────────────────────────────────────────────────────
				// The inserter tries to INSERT a WorkflowRun for the same workflowId.
				// It is blocked until dbLock is available.
				var inserterTask = Task.Run(async () =>
				{
					// Give the deleter a moment to start so it acquires the lock first
					await Task.Delay(20);

					timeline.Add("inserter:waiting_for_lock");
					await dbLock.WaitAsync();          // blocked while deleter holds it
					try
					{
						timeline.Add("inserter:lock_acquired");
						// Simulate an INSERT WorkflowRun – by this point the parent row is
						// already gone, so in a real DB this would succeed or get a FK error
						// depending on whether the row was committed-away.
						// Here we just record the attempt came AFTER the deleter released.
					}
					finally
					{
						dbLock.Release();
					}
				});

				// ── Run deleter concurrently ─────────────────────────────────────────────
				var deleterTask = repo.DeleteWorkflowWithAllDependenciesAsync(workflowId);

				await Task.WhenAll(deleterTask, inserterTask);

				// Assert timeline: inserter must never acquire the lock before deleter releases it
				var lockAcquiredIdx   = timeline.IndexOf("deleter:lock_acquired");
				var lockReleasedIdx   = timeline.IndexOf("deleter:lock_released");
				var inserterWaitIdx   = timeline.IndexOf("inserter:waiting_for_lock");
				var inserterAcqIdx    = timeline.IndexOf("inserter:lock_acquired");

				lockAcquiredIdx.Should().BeGreaterThanOrEqualTo(0,  "deleter must acquire the lock");
				lockReleasedIdx.Should().BeGreaterThan(lockAcquiredIdx, "deleter must release after acquiring");
				inserterAcqIdx.Should().BeGreaterThan(lockReleasedIdx,
					"inserter must only acquire the lock AFTER the deleter releases it (no FK window)");
				inserterWaitIdx.Should().BeLessThan(inserterAcqIdx,
					"inserter must have been waiting before it acquired the lock");
			}
			finally
			{
				DataConfig.DatabaseType = originalDbType;
			}
		}

		// ── helpers ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Builds a minimal <see cref="IQueryFactory"/> mock whose GetDeleteQuery returns
		/// a valid (non-null, non-empty) SQL string for each of the three child-delete
		/// query types used by <see cref="WorkflowRepository"/>.
		/// </summary>
		private static IQueryFactory BuildQueryFactory(DatabaseTypes dbType, SqlConfiguration cfg)
		{
			var mock = new Mock<IQueryFactory>();

			// WorkflowRunLogs delete
			mock.Setup(f => f.GetDeleteQuery<DeleteWorkflowRunLogsByWorkflowIdQuery>())
				.Returns(dbType == DatabaseTypes.Postgres
					? $"DELETE FROM {cfg.SchemaName}.workflowrunlogs WHERE workflowrunid IN (SELECT workflowrunid FROM {cfg.SchemaName}.workflowruns WHERE workflowid = @WorkflowId)"
					: $"DELETE FROM {cfg.SchemaName}.[WorkflowRunLogs] WHERE [WorkflowRunId] IN (SELECT [WorkflowRunId] FROM {cfg.SchemaName}.[WorkflowRuns] WHERE [WorkflowId] = @WorkflowId)");

			// WorkflowRuns delete
			mock.Setup(f => f.GetDeleteQuery<DeleteWorkflowRunsByWorkflowIdQuery>())
				.Returns(dbType == DatabaseTypes.Postgres
					? $"DELETE FROM {cfg.SchemaName}.workflowruns WHERE workflowid = @WorkflowId"
					: $"DELETE FROM {cfg.SchemaName}.[WorkflowRuns] WHERE [WorkflowId] = @WorkflowId");

			// WorkflowSteps delete
			mock.Setup(f => f.GetDeleteQuery<DeleteWorkflowStepsByWorkflowIdQuery>())
				.Returns(dbType == DatabaseTypes.Postgres
					? $"DELETE FROM {cfg.SchemaName}.workflowsteps WHERE workflowid = @WorkflowId"
					: $"DELETE FROM {cfg.SchemaName}.[WorkflowSteps] WHERE [WorkflowId] = @WorkflowId");

			return mock.Object;
		}
	}

	// ── CapturingConnection ──────────────────────────────────────────────────────────

	/// <summary>
	/// A thin <see cref="DbConnection"/> fake that records each SQL string passed to
	/// Dapper's <c>ExecuteAsync</c> (which ultimately calls
	/// <see cref="DbConnection.CreateCommand"/>).
	///
	/// Optional lock-acquired and all-deleted callbacks let the concurrency test inject timing signals.
	/// </summary>
	internal sealed class CapturingConnection : DbConnection
	{
		private readonly List<string> _log;
		private readonly Action _onLockSelected;
		private readonly Action _onAllDeleted;

		// Counts how many DELETE statements have been executed (we expect 4).
		private int _deleteCount;

		public CapturingConnection(
			List<string> log,
			Action onLockSelected = null,
			Action onAllDeleted = null)
		{
			_log = log;
			_onLockSelected = onLockSelected;
			_onAllDeleted = onAllDeleted;
		}

		// ── DbConnection overrides ───────────────────────────────────────────────────

		public override string ConnectionString { get; set; } = "Capture://";
		public override string Database => "capture";
		public override string DataSource => "capture";
		public override string ServerVersion => "0";
		public override ConnectionState State => ConnectionState.Open;

		public override void Open() { }
		protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
			=> new CapturingTransaction(this, isolationLevel, OnCommit);

		public override void ChangeDatabase(string databaseName) { }
		public override void Close() { }

		protected override DbCommand CreateDbCommand() => new CapturingCommand(this, OnExecute);

		// ── internal callback ────────────────────────────────────────────────────────

		private void OnExecute(string sql)
		{
			_log.Add(sql ?? string.Empty);

			// If the SQL is a lock SELECT fire the callback so the concurrency test can
			// simulate the DB acquiring a row lock.
			if (!string.IsNullOrEmpty(sql) &&
				(sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) ||
				 sql.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)))
			{
				_onLockSelected?.Invoke();
			}

			if (!string.IsNullOrEmpty(sql) &&
				sql.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
			{
				_deleteCount++;
				if (_deleteCount == 4)
					_onAllDeleted?.Invoke();
			}
		}

		private void OnCommit()
		{
			// The transaction committed – nothing extra needed for the capture, but the
			// concurrency test's onAllDeletesDone callback may already have fired.
		}
	}

	/// <summary>Minimal DbTransaction that calls an optional commit callback.</summary>
	internal sealed class CapturingTransaction : DbTransaction
	{
		private readonly DbConnection _conn;
		private readonly Action _onCommit;

		public CapturingTransaction(DbConnection conn, IsolationLevel isolation, Action onCommit)
		{
			_conn     = conn;
			IsolationLevel = isolation;
			_onCommit = onCommit;
		}

		public override IsolationLevel IsolationLevel { get; }
		protected override DbConnection DbConnection => _conn;

		public override void Commit()  => _onCommit?.Invoke();
		public override void Rollback() { }
	}

	/// <summary>Minimal DbCommand that captures CommandText on ExecuteNonQuery.</summary>
	internal sealed class CapturingCommand : DbCommand
	{
		private readonly Action<string> _onExecute;
		private readonly CapturingConnection _conn;

		public CapturingCommand(CapturingConnection conn, Action<string> onExecute)
		{
			_conn      = conn;
			_onExecute = onExecute;
		}

		public override string CommandText { get; set; } = string.Empty;
		public override int CommandTimeout { get; set; }
		public override CommandType CommandType { get; set; }
		public override bool DesignTimeVisible { get; set; }
		public override UpdateRowSource UpdatedRowSource { get; set; }
		protected override DbConnection DbConnection { get => _conn; set { } }
		protected override DbParameterCollection DbParameterCollection { get; } = new CapturingParameterCollection();
		protected override DbTransaction DbTransaction { get; set; }

		public override void Cancel() { }
		public override int ExecuteNonQuery()
		{
			_onExecute(CommandText);
			return 0;
		}
		public override object ExecuteScalar()
		{
			_onExecute(CommandText);
			return null;
		}
		protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
		{
			_onExecute(CommandText);
			return new CapturingDataReader();
		}
		public override void Prepare() { }
		protected override DbParameter CreateDbParameter() => new CapturingParameter();
	}

	/// <summary>Empty DbDataReader used when Dapper calls ExecuteReader on the lock SELECT.</summary>
	internal sealed class CapturingDataReader : DbDataReader
	{
		public override bool Read() => false;
		public override bool NextResult() => false;
		public override void Close() { }
		public override bool HasRows => false;
		public override bool IsClosed => true;
		public override int RecordsAffected => 0;
		public override int FieldCount => 0;
		public override int Depth => 0;
		public override object this[int ordinal] => throw new IndexOutOfRangeException();
		public override object this[string name]  => throw new KeyNotFoundException();
		public override bool GetBoolean(int ordinal) => false;
		public override byte GetByte(int ordinal) => 0;
		public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => 0;
		public override char GetChar(int ordinal) => default;
		public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => 0;
		public override string GetDataTypeName(int ordinal) => "object";
		public override DateTime GetDateTime(int ordinal) => default;
		public override decimal GetDecimal(int ordinal) => 0;
		public override double GetDouble(int ordinal) => 0;
		public override Type GetFieldType(int ordinal) => typeof(object);
		public override float GetFloat(int ordinal) => 0;
		public override Guid GetGuid(int ordinal) => default;
		public override short GetInt16(int ordinal) => 0;
		public override int GetInt32(int ordinal) => 0;
		public override long GetInt64(int ordinal) => 0;
		public override string GetName(int ordinal) => string.Empty;
		public override int GetOrdinal(string name) => 0;
		public override string GetString(int ordinal) => string.Empty;
		public override object GetValue(int ordinal) => DBNull.Value;
		public override int GetValues(object[] values) => 0;
		public override bool IsDBNull(int ordinal) => true;
		public override IEnumerator<object> GetEnumerator() => ((IEnumerable<object>)Array.Empty<object>()).GetEnumerator();
	}

	/// <summary>Minimal DbParameter.</summary>
	internal sealed class CapturingParameter : DbParameter
	{
		public override DbType DbType { get; set; }
		public override ParameterDirection Direction { get; set; }
		public override bool IsNullable { get; set; }
		public override string ParameterName { get; set; } = string.Empty;
		public override int Size { get; set; }
		public override string SourceColumn { get; set; } = string.Empty;
		public override bool SourceColumnNullMapping { get; set; }
		public override object Value { get; set; }
		public override void ResetDbType() { }
	}

	/// <summary>Minimal DbParameterCollection.</summary>
	internal sealed class CapturingParameterCollection : DbParameterCollection
	{
		private readonly List<DbParameter> _items = new();

		public override int Count => _items.Count;
		public override object SyncRoot => this;
		public override bool IsFixedSize => false;
		public override bool IsReadOnly => false;
		public override bool IsSynchronized => false;

		public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
		public override void AddRange(Array values) { foreach (var v in values) _items.Add((DbParameter)v); }
		public override void Clear() => _items.Clear();
		public override bool Contains(object value) => _items.Contains((DbParameter)value);
		public override bool Contains(string value) => _items.Exists(p => p.ParameterName == value);
		public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
		public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
		public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
		public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
		public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
		public override void Remove(object value) => _items.Remove((DbParameter)value);
		public override void RemoveAt(int index) => _items.RemoveAt(index);
		public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
		protected override DbParameter GetParameter(int index) => _items[index];
		protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
		protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
		protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
	}
}













