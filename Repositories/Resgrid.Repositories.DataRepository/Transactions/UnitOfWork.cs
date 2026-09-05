using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;


namespace Resgrid.Repositories.DataRepository.Transactions
{
	public class UnitOfWork : IUnitOfWork
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SemaphoreSlim _semaphore;
		public UnitOfWork(IConnectionProvider connProvider)
		{
			_connectionProvider = connProvider;
			_semaphore = new SemaphoreSlim(1);
		}

		public DbTransaction Transaction { get; private set; }

		public DbConnection Connection { get; private set; }

		public void CommitChanges() => Complete(true);

		public DbConnection CreateOrGetConnection()
		{
			_semaphore.Wait();

			try
			{
				if (Connection == null)
				{
					Connection = _connectionProvider.Create();
					Connection.Open();
					Transaction = Connection.BeginTransaction();
				}
				return Connection;
			}
			catch { Reset(); throw; }
			finally { _semaphore.Release(); }
		}

		public async Task<DbConnection> CreateOrGetConnectionAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			await _semaphore.WaitAsync(cancellationToken);

			try
			{
				if (Connection == null)
				{
					Connection = _connectionProvider.Create();
					await Connection.OpenAsync(cancellationToken);

					Transaction = Connection.BeginTransaction();
				}

				return Connection;
			}
			catch { Reset(); throw; }
			finally
			{
				_semaphore.Release();
			}
		}

		public void DiscardChanges() => Complete(false);

		private void Complete(bool commit)
		{
			_semaphore.Wait();
			try
			{
				try { if (commit) Transaction?.Commit(); else Transaction?.Rollback(); }
				finally { Reset(); }
			}
			finally { _semaphore.Release(); }
		}

		private void Reset()
		{
			var transaction = Transaction;
			var connection = Connection;
			Transaction = null;
			Connection = null;
			try { transaction?.Dispose(); }
			finally { connection?.Dispose(); }
		}

		public void Dispose()
		{
			_semaphore.Wait();
			try { Reset(); }
			finally { _semaphore.Release(); }
		}
	}
}
