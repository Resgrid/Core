using System;
using System.Data.Common;

namespace Resgrid.Model.Repositories.Queries
{
	public interface IUnitOfWork : IDisposable
	{
		DbTransaction Transaction { get; }
		DbConnection Connection { get; }
		DbConnection CreateOrGetConnection();
		void DiscardChanges();
		void CommitChanges();
	}
}
