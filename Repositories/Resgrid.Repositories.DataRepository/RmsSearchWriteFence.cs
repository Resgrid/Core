using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>A preloaded projection may outlive a purge. Its Lucene write must finish before retention can take the department lock.</summary>
	public sealed class RmsSearchWriteFence : RmsRepositoryBase<RmsRecordSearchProjection>, IRmsSearchWriteFence
	{
		public RmsSearchWriteFence(IConnectionProvider connections, SqlConfiguration configuration, IUnitOfWork unit, IQueryFactory queries)
			: base(connections, configuration, unit, queries) { }

		public async Task<int> WithLiveSourceAsync(RecordsSearchDocumentSource source, Func<RecordsSearchDocumentSource, int> write, CancellationToken cancellationToken = default)
		{
			if (source?.Projection == null || write == null) throw new ArgumentException("A search source and index writer are required.");
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("Index writes require their own short retention transaction.");
			var requested = source.Projection;
			if (requested.DepartmentId <= 0 || string.IsNullOrWhiteSpace(requested.SourceId)) throw new ArgumentException("The search source has no department or record identity.");
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await LockRecordsDepartmentAsync(requested.DepartmentId, cancellationToken);
				var current = await QueryFirstOrDefaultAsync<RmsRecordSearchProjection>(
					$"SELECT * FROM {Tbl("RmsRecordSearchProjections")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("SourceType")}={P}SourceType AND {Col("SourceId")}={P}SourceId",
					new { requested.DepartmentId, requested.SourceType, requested.SourceId }, cancellationToken);
				RecordsSearchDocumentSource authorized;
				if (current == null || current.DeletedOn.HasValue)
				{
					// Preserve only the deletion key. A preloaded narrative or summary never enters this callback.
					authorized = new RecordsSearchDocumentSource { Projection = new RmsRecordSearchProjection
					{ DepartmentId = requested.DepartmentId, SourceType = requested.SourceType, SourceId = requested.SourceId, DeletedOn = DateTime.UtcNow } };
				}
				else
				{
					if (current.SourceType == (int)RmsSearchSourceType.Record)
						await LockLiveContentParentAsync(current.DepartmentId, current.SourceId, cancellationToken);
					if (current.RmsRecordSearchProjectionId != requested.RmsRecordSearchProjectionId || current.RowVersion != requested.RowVersion || current.ModifiedOn != requested.ModifiedOn)
						throw new InvalidOperationException("The search source changed after it was loaded; retry from the committed checkpoint.");
					authorized = new RecordsSearchDocumentSource { Projection = current, Narrative = source.Narrative, Generation = source.Generation };
				}
				cancellationToken.ThrowIfCancellationRequested();
				var count = write(authorized);
				UnitOfWork.CommitChanges();
				return count;
			}
			catch { UnitOfWork.DiscardChanges(); throw; }
		}
	}
}
