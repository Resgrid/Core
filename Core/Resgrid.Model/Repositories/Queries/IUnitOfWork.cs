using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories.Queries
{
	public interface IUnitOfWork : IDisposable
	{
		DbTransaction Transaction { get; }
		DbConnection Connection { get; }
		DbConnection CreateOrGetConnection();
		Task<DbConnection> CreateOrGetConnectionAsync(CancellationToken cancellationToken = default(CancellationToken));
		void DiscardChanges();
		void CommitChanges();
	}
}
