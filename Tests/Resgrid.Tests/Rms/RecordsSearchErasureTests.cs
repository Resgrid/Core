using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Search;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordsSearchErasureTests
	{
		[Test]
		public async Task Committed_erasure_removes_deleted_documents_after_reopening_the_filesystem_index()
		{
			var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var directory = Path.GetFullPath(Path.Combine(tempRoot, "rms-erasure-" + Guid.NewGuid().ToString("N")));
			System.IO.Directory.CreateDirectory(directory);
			try
			{
				var fence = new Mock<IRmsSearchWriteFence>();
				fence.Setup(f => f.WithLiveSourceAsync(It.IsAny<RecordsSearchDocumentSource>(), It.IsAny<Func<RecordsSearchDocumentSource, int>>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((RecordsSearchDocumentSource source, Func<RecordsSearchDocumentSource, int> write, CancellationToken ct) => write(source));
				using (var host = new LuceneRecordsIndexHost(FSDirectory.Open(directory), true))
				{
					var indexer = new LuceneRecordsIndexer(host, fence.Object);
					await indexer.IndexAsync(new[] { Source("purged", 11), Source("retained", 12) });
					await indexer.CommitAsync();
					await indexer.DeleteAsync(11, (int)RmsSearchSourceType.Record, "purged");
					await indexer.ExpungeDeletesAsync();
				}
				using var files = FSDirectory.Open(directory);
				using var reader = DirectoryReader.Open(files);
				reader.HasDeletions.Should().BeFalse("a live-doc mask alone does not erase the old indexed document");
				reader.MaxDoc.Should().Be(1); reader.NumDocs.Should().Be(1);
				reader.Document(0).Get(RecordsIndexFields.SourceId).Should().Be("retained");
				reader.Document(0).Get(RecordsIndexFields.DepartmentId).Should().Be("12");
			}
			finally
			{
				// Delete only this test's newly created temporary directory, never an index configured by the application.
				if (!directory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(directory).StartsWith("rms-erasure-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test index path.");
				System.IO.Directory.Delete(directory, true);
			}
		}

		private static RecordsSearchDocumentSource Source(string id, int department) => new RecordsSearchDocumentSource
		{
			Projection = new RmsRecordSearchProjection { RmsRecordSearchProjectionId = id, SourceId = id, SourceType = (int)RmsSearchSourceType.Record,
				DepartmentId = department, RecordCreatedOn = new DateTime(2026, 1, 1), DisplaySummary = "private-" + id },
			Narrative = "private narrative " + id
		};
	}
}
