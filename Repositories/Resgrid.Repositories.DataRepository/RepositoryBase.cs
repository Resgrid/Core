using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Transactions;
using EntityFramework.BulkInsert.Extensions;

namespace Resgrid.Repositories.DataRepository
{
	public class RepositoryBase<T> : IDisposable, IRepository<T> where T : class, IEntity
	{
		/* 
		 * <KeithStyleNovella> 
		 * 
		 * Originally this repo used IDbContext
		 * and IDbSet for entity and context control. This worked great, until
		 * multiple long lasting contexts and other systems all were changing the
		 * same underlying database. There was no way using IDbContext and IDbSet
		 * to force a refresh of the L2 ORM cache that EntityFramework's context
		 * or DbSet were holding on to.
		 * 
		 * So changing to ObjectContext and ObjectSet allowed the setting of the
		 * MergeOption.OverwriteChanges on the set, which will always pick up 
		 * changes from the backing store.
		 * 
		 * Again even with multiple systems the contexts should be short lived
		 * enough in the Unit of Work pattern that the websites use that it didn't
		 * seem to be an issue for them, but it was a problem for the now 2 backend
		 * workers on different systems modifying and looking at the same table.
		 * 
		 * Additionally ObjectSet doesn't support the Find() method that DbSet has,
		 * seems like ObjectSet is at a lower level then DbSet. Because of that
		 * GetById has to die, currently I've pushed that up one level into the
		 * actual repo implementations, but f-it the services can just use GetAll()
		 * and Linq off of that.
		 * 
		 * </KeithStyleNovella>
		 */
		private static readonly object _syncRoot;
		protected ObjectContext context;
		protected DbContext dbContext;
		protected ObjectSet<T> entities;
		protected readonly bool IsSqlCe;
		protected Database db;
		private readonly IISolationLevel _isolationLevel;

		public RepositoryBase(IDbContext context, IISolationLevel isolationLevel)
		{
			_isolationLevel = isolationLevel;

			this.context = ((IObjectContextAdapter)context).ObjectContext;
			ObjectSet<T> set = this.context.CreateObjectSet<T>();
			set.MergeOption = MergeOption.OverwriteChanges;
			IsSqlCe = context.IsSqlCe();

			if (!IsSqlCe)
				this.context.CommandTimeout = 300;


			db = ((DbContext)context).Database;
			dbContext = ((DbContext) context);

			entities = set;

			// Uncomment the below if you want to see all hell break loose! Do not deploy to prod. -SJ
			//this.context.ObjectStateManager.ObjectStateManagerChanged += (sender, e) =>
			//{
			//	Trace.WriteLine(string.Format("{0}, {1}", e.Action, e.Element));
			//};
		}

		public virtual IQueryable<T> GetAll()
		{
			return entities;
		}

		public virtual void SaveOrUpdate(T entity)
		{
			if ((entity.Id is Guid && ((Guid)entity.Id) == Guid.Empty) ||
				(entity.Id is int && ((int)entity.Id) == 0))
			{
				entities.AddObject(entity);
			}

			context.SaveChanges();
		}

		public virtual async void SaveOrUpdateAsync(T entity, CancellationToken cancellationToken = default(CancellationToken))
		{
			if ((entity.Id is Guid && ((Guid)entity.Id) == Guid.Empty) ||
				(entity.Id is int && ((int)entity.Id) == 0))
			{
				entities.AddObject(entity);
			}

			await context.SaveChangesAsync(cancellationToken);
		}

		public virtual void DeleteOnSubmit(T entity)
		{
			entities.DeleteObject(entity);
			context.SaveChanges();
		}

		public virtual async void DeleteOnSubmitAsync(T entity, CancellationToken cancellationToken = default(CancellationToken))
		{
			entities.DeleteObject(entity);
			await context.SaveChangesAsync(cancellationToken);
		}

		public virtual void DeleteAll(IEnumerable<T> entitesToDelete)
		{
			foreach (var entity in entitesToDelete)
				entities.DeleteObject(entity);

			context.SaveChanges();
		}

		public virtual async void DeleteAllAsync(IEnumerable<T> entitesToDelete, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var entity in entitesToDelete)
				entities.DeleteObject(entity);

			await context.SaveChangesAsync(cancellationToken);
		}

		public virtual void SaveOrUpdateAll(IEnumerable<T> entitiesToAdd)
		{
			foreach (var entity in entitiesToAdd)
			{
				if ((entity.Id is Guid && ((Guid)entity.Id) == Guid.Empty) ||
				(entity.Id is int && ((int)entity.Id) == 0))
				{
					entities.AddObject(entity);
				}
			}

			context.SaveChanges();
		}

		/// <summary>
		/// Still trying to get this to work
		/// </summary>
		/// <param name="entitiesToAdd"></param>
		public virtual void BulkInsert(IEnumerable<T> entitiesToAdd)
		{
			EntityFramework.BulkInsert.ProviderFactory.Register<EntityFramework.BulkInsert.Providers.EfSqlBulkInsertProviderWithMappedDataReader>("System.Data.SqlClient.SqlConnection");

			dbContext.BulkInsert(entities);
			dbContext.SaveChanges();
		}

		public void Dispose()
		{
			if (context != null)
			{
				context.Dispose();
				context = null;
			}

			entities = null;
			db = null;
		}
	}
}