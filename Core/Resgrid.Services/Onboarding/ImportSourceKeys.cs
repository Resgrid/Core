using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The source-system instance key that scopes the external-id map (wizard plan sections 11.3 and 12.1): a data set's
	/// declared source, else the earlier import its workspace continues (section 5.2), else the workspace's own instance.
	/// Planning, checking references and committing all read the key here so they never disagree.
	/// </summary>
	public static class ImportSourceKeys
	{
		/// <summary>The key of files with no declared source, and of records created by "Create new".</summary>
		public static string Workspace(string workspaceId) => "workspace:" + workspaceId;

		public static async Task<string> ForDatasetAsync(IImportSourcesRepository sources, ImportDataset dataset, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(dataset.SourceId))
			{
				var source = await sources.GetByIdAsync(dataset.DepartmentId, dataset.SourceId, cancellationToken);
				if (!string.IsNullOrWhiteSpace(source?.SourceSystemKey))
					return source.SourceSystemKey;
			}

			var continuation = await ContinuationAsync(sources, dataset.DepartmentId, dataset.WorkspaceId, cancellationToken);
			return string.IsNullOrWhiteSpace(continuation?.SourceSystemKey) ? Workspace(dataset.WorkspaceId) : continuation.SourceSystemKey;
		}

		/// <summary>The workspace's continuation source, or null.</summary>
		public static async Task<ImportSource> ContinuationAsync(IImportSourcesRepository sources, int departmentId, string workspaceId,
			CancellationToken cancellationToken) =>
			(await sources.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
			.FirstOrDefault(s => string.Equals(s.ProductKey, ImportSourceProducts.Continuation, StringComparison.OrdinalIgnoreCase));
	}
}
