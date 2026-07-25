using Resgrid.Model.Repositories.Queries;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// No-op unit of work for tests. Avoids real SQL connections and transactions.
	/// </summary>
	public sealed class MockUnitOfWork : IUnitOfWork
	{
		public DbTransaction Transaction => null;
		public DbConnection Connection => null;

		public DbConnection CreateOrGetConnection() => null;
		public Task<DbConnection> CreateOrGetConnectionAsync(CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult<DbConnection>(null);
		public void CommitChanges() { }
		public void DiscardChanges() { }
		public void Dispose() { }
	}
}

