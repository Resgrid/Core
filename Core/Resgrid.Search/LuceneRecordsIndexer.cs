using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Search
{
	/// <summary>
	/// Write side of the records index: upsert by document key, delete by key or by department, explicit commit.
	/// Holds no state of its own; the host owns the single writer. Commit and erasure publish to the object store
	/// under the database lease when one is configured (plan R7); tests construct it without a lease repository.
	/// </summary>
	public class LuceneRecordsIndexer : IRecordsSearchIndexer
	{
		private readonly LuceneRecordsIndexHost _host;
		private readonly IRmsSearchWriteFence _fence;
		private readonly ISearchIndexLeasesRepository _leases;

		public LuceneRecordsIndexer(LuceneRecordsIndexHost host, IRmsSearchWriteFence fence)
			: this(host, fence, null)
		{
		}

		public LuceneRecordsIndexer(LuceneRecordsIndexHost host, IRmsSearchWriteFence fence, ISearchIndexLeasesRepository leases)
		{
			_host = host ?? throw new ArgumentNullException(nameof(host));
			_fence = fence ?? throw new ArgumentNullException(nameof(fence));
			_leases = leases;
		}

		public async Task<int> IndexAsync(IEnumerable<RecordsSearchDocumentSource> documents, CancellationToken cancellationToken = default)
		{
			var count = 0;

			foreach (var source in documents ?? Array.Empty<RecordsSearchDocumentSource>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (source?.Projection == null)
					continue;

				count += await _fence.WithLiveSourceAsync(source, current => _host.Write(writer =>
				{
					var p = current.Projection;
					var key = RecordsIndexFields.BuildKey(p.DepartmentId, p.SourceType, p.SourceId);
					if (p.DeletedOn.HasValue)
					{
						writer.DeleteDocuments(new Term(RecordsIndexFields.Key, key));
						return 0;
					}
					writer.UpdateDocument(new Term(RecordsIndexFields.Key, key), RecordsSearchDocumentBuilder.Build(current));
					return 1;
				}), cancellationToken);
			}

			return count;
		}

		public async Task DeleteAsync(int departmentId, int sourceType, string sourceId, CancellationToken cancellationToken = default)
		{
			await _fence.WithLiveSourceAsync(new RecordsSearchDocumentSource { Projection = new RmsRecordSearchProjection
			{ DepartmentId = departmentId, SourceType = sourceType, SourceId = sourceId, DeletedOn = DateTime.UtcNow } }, current =>
			{
				if (!current.Projection.DeletedOn.HasValue) throw new InvalidOperationException("A live record cannot be erased from the index by a stale deletion request.");
				_host.Write(writer => { writer.DeleteDocuments(new Term(RecordsIndexFields.Key, RecordsIndexFields.BuildKey(departmentId, sourceType, sourceId))); return 0; });
				return 0;
			}, cancellationToken);
		}

		public Task DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_host.Write(writer => { writer.DeleteDocuments(new Term(RecordsIndexFields.DepartmentId, departmentId.ToString())); return 0; });
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
				var hits = searcher.Search(new TermQuery(new Term(RecordsIndexFields.DepartmentId, departmentId.ToString())), 1);
				return Task.FromResult(hits.TotalHits);
			}
			finally
			{
				manager.Release(searcher);
			}
		}
	}
}
