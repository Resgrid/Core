using Resgrid.Model.Repositories.Queries;
using System.Data.Common;

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
		public void CommitChanges() { }
		public void DiscardChanges() { }
		public void Dispose() { }
	}
}

