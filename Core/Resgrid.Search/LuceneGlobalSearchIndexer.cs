using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Search
{
	/// <summary>
	/// Write side of the global index: upsert by document key, delete by key or by department, commit-and-publish. Holds
	/// no state of its own; the host owns the single writer. Only the worker process resolves this in anger.
	/// </summary>
	public class LuceneGlobalSearchIndexer : IGlobalSearchIndexer
	{
		private readonly LuceneGlobalIndexHost _host;
		private readonly ISearchIndexLeasesRepository _leases;

		public LuceneGlobalSearchIndexer(LuceneGlobalIndexHost host)
			: this(host, null)
		{
		}

		public LuceneGlobalSearchIndexer(LuceneGlobalIndexHost host, ISearchIndexLeasesRepository leases)
		{
			_host = host ?? throw new ArgumentNullException(nameof(host));
			_leases = leases;
		}

		public bool IndexExists => _host.IndexExists;

		public Task<int> IndexAsync(IEnumerable<SearchProjection> projections, string generation, CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var projection in projections ?? Array.Empty<SearchProjection>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (projection == null || string.IsNullOrWhiteSpace(projection.EntityType) || string.IsNullOrWhiteSpace(projection.EntityId))
					continue;

				var key = SearchProjection.BuildKey(projection.DepartmentId, projection.EntityType, projection.EntityId);
				count += _host.Write(writer =>
				{
					if (projection.DeletedOn.HasValue)
					{
						writer.DeleteDocuments(new Term(GlobalIndexFields.Key, key));
						return 0;
					}
					writer.UpdateDocument(new Term(GlobalIndexFields.Key, key), GlobalSearchDocumentBuilder.Build(projection, generation));
					return 1;
				});
			}

			return Task.FromResult(count);
		}

		public Task DeleteAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_host.Write(writer => { writer.DeleteDocuments(new Term(GlobalIndexFields.Key, SearchProjection.BuildKey(departmentId, entityType, entityId))); return 0; });
			return Task.CompletedTask;
		}

		public Task DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_host.Write(writer => { writer.DeleteDocuments(new Term(GlobalIndexFields.DepartmentId, departmentId.ToString())); return 0; });
			return Task.CompletedTask;
		}

		public Task CommitAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return SearchIndexPublishCoordinator.CommitAndPublishAsync(_host, _leases, cancellationToken);
		}

		public Task ExpungeDeletesAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return SearchIndexPublishCoordinator.ExpungeAndPublishAsync(_host, _leases, cancellationToken);
		}

		public Task<int> CountDocumentsAsync(int departmentId)
		{
			var manager = _host.GetSearcherManager();
			if (manager == null)
				return Task.FromResult(0);

			_host.MaybeRefresh();
			var searcher = manager.Acquire();
			try
			{
				var hits = searcher.Search(new TermQuery(new Term(GlobalIndexFields.DepartmentId, departmentId.ToString())), 1);
				return Task.FromResult(hits.TotalHits);
			}
			finally
			{
				manager.Release(searcher);
			}
		}
	}
}
