using System;
using Resgrid.Repositories.DataRepository.Contexts;

namespace Resgrid.Repositories.DataRepository
{
	public class DatabaseFactory : Disposable, IDatabaseFactory
	{
		private DataContext db;

		///
		/// Returns the active database object context instance or creates a new instance if one doesn't exist
		/// already.
		/// 
		/// A OpendeskDb object (which inherits from DbContext).
		public DataContext Get()
		{
			return db ?? (db = new DataContext());
		}

		protected override void DisposeCore()
		{
			if (db != null)
				db.Dispose();
		}
	}

	///
	/// The Disposable class is a managed disposable resource that can be explicitly called within other classes.
	/// 
	public class Disposable : IDisposable
	{
		private bool isDisposed;

		~Disposable()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (!isDisposed && disposing)
			{
				DisposeCore();
			}

			isDisposed = true;
		}

		protected virtual void DisposeCore()
		{
		}
	}
}