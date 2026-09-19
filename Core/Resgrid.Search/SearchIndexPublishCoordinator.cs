using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;

namespace Resgrid.Search
{
	/// <summary>
	/// Commit-and-publish under the database publish lease (plan R7 writer sequence steps 2–6). The lease is the second
	/// guard after the Recreate rollout strategy; the manifest's conditional PUT is the third. Without an object store
	/// this is just a commit. A manifest conflict resets the local writer from the store and rethrows so the caller's
	/// checkpoint is not advanced; the next sweep re-indexes from the projection tables.
	/// </summary>
	public static class SearchIndexPublishCoordinator
	{
		private static readonly string Owner = $"{Environment.MachineName}:{Environment.ProcessId}";

		public static async Task CommitAndPublishAsync(LuceneIndexHost host, ISearchIndexLeasesRepository leases, CancellationToken cancellationToken)
		{
			host.Commit();
			await PublishAsync(host, leases, cancellationToken);
		}

		public static async Task ExpungeAndPublishAsync(LuceneIndexHost host, ISearchIndexLeasesRepository leases, CancellationToken cancellationToken)
		{
			host.ExpungeDeletes();
			// Erasure is acknowledged by the caller only after this returns: the superseded segment objects must be
			// gone from the bucket as well (plan R7 consequences), so a publish failure here must propagate.
			await PublishAsync(host, leases, cancellationToken);
		}

		public static async Task PublishAsync(LuceneIndexHost host, ISearchIndexLeasesRepository leases, CancellationToken cancellationToken)
		{
			if (host == null || !host.StoreEnabled)
			{
				host?.MaybeRefresh();
				return;
			}

			var leaseHeld = false;
			var duration = TimeSpan.FromSeconds(Math.Max(30, SearchConfig.PublishLeaseSeconds));
			if (leases != null)
			{
				leaseHeld = await leases.TryAcquireAsync(host.IndexName, Owner, duration, DateTime.UtcNow, cancellationToken);
				if (!leaseHeld)
					throw new InvalidOperationException($"Search index '{host.IndexName}' publish lease is held by another writer; not publishing.");
			}

			try
			{
				SearchIndexManifest manifest;
				try
				{
					manifest = await host.PublishAsync(Owner, cancellationToken);
				}
				catch (SearchIndexManifestConflictException ex)
				{
					Logging.LogException(ex, $"Search index '{host.IndexName}' manifest conflict; resetting the local writer from the object store.");
					try { await host.ResetFromStoreAsync(cancellationToken); }
					catch (Exception reset) { Logging.LogException(reset, $"Search index '{host.IndexName}' reset after conflict failed."); }
					throw;
				}

				if (manifest != null && leases != null && leaseHeld)
					await leases.RecordPublishedAsync(host.IndexName, Owner, manifest.Revision, DateTime.UtcNow, cancellationToken);
			}
			finally
			{
				if (leases != null && leaseHeld)
				{
					try { await leases.ReleaseAsync(host.IndexName, Owner, CancellationToken.None); }
					catch (Exception ex) { Logging.LogException(ex, $"Search index '{host.IndexName}' publish lease release failed; it expires on its own."); }
				}
				host.MaybeRefresh();
			}
		}
	}
}
