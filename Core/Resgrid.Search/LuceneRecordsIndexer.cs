using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Search
{
	/// <summary>
	/// Write side of the records index: upsert by document key, delete by key or by department, explicit commit.
	/// Holds no state of its own; the host owns the single writer.
	/// </summary>
	public class LuceneRecordsIndexer : IRecordsSearchIndexer
	{
		private readonly LuceneRecordsIndexHost _host;

		public LuceneRecordsIndexer(LuceneRecordsIndexHost host)
		{
			_host = host ?? throw new ArgumentNullException(nameof(host));
		}

		public Task<int> IndexAsync(IEnumerable<RecordsSearchDocumentSource> documents, CancellationToken cancellationToken = default)
		{
			var writer = _host.GetWriter();
			var count = 0;

			foreach (var source in documents ?? Array.Empty<RecordsSearchDocumentSource>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (source?.Projection == null)
					continue;

				var p = source.Projection;
				var key = RecordsIndexFields.BuildKey(p.DepartmentId, p.SourceType, p.SourceId);

				if (p.DeletedOn.HasValue)
				{
					writer.DeleteDocuments(new Term(RecordsIndexFields.Key, key));
					continue;
				}

				writer.UpdateDocument(new Term(RecordsIndexFields.Key, key), RecordsSearchDocumentBuilder.Build(source));
				count++;
			}

			return Task.FromResult(count);
		}

		public Task DeleteAsync(int departmentId, int sourceType, string sourceId, CancellationToken cancellationToken = default)
		{
			_host.GetWriter().DeleteDocuments(new Term(RecordsIndexFields.Key, RecordsIndexFields.BuildKey(departmentId, sourceType, sourceId)));
			return Task.CompletedTask;
		}

		public Task DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			_host.GetWriter().DeleteDocuments(new Term(RecordsIndexFields.DepartmentId, departmentId.ToString()));
			return Task.CompletedTask;
		}

		public Task CommitAsync(CancellationToken cancellationToken = default)
		{
			_host.GetWriter().Commit();
			_host.MaybeRefresh();
			return Task.CompletedTask;
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
