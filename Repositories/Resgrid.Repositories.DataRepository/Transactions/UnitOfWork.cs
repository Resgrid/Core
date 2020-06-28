using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using System.Data.Common;
using System.Threading;


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

		public void CommitChanges() => Transaction?.Commit();

		public DbConnection CreateOrGetConnection()
		{
			_semaphore.Wait();

			if (Connection == null)
			{
				Connection = _connectionProvider.Create();
				Connection.Open();

				Transaction = Connection.BeginTransaction();
			}

			_semaphore.Release();

			return Connection;
		}

		public void DiscardChanges() => Transaction?.Rollback();

		public void Dispose()
		{
			Transaction?.Dispose();
			Connection?.Close();
			Connection?.Dispose();
		}
	}
}
