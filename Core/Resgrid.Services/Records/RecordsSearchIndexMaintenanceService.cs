using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Worker command 44 (RMS plan section 5.10): per activated department, compare the stored generation key with
	/// (schemaVersion, protectedCatalogVersion, policyEpoch); rebuild on mismatch, otherwise catch up documents
	/// modified since the last sweep. Narrative is indexed only for unprotected departments that opted in, and is
	/// dropped by the rebuild an enrollment triggers (the degrade path). The projection table stays the source of
	/// truth; the index is fully rebuildable from it.
	/// </summary>
	public class RecordsSearchIndexMaintenanceService : IRecordsSearchIndexMaintenanceService
	{
		private readonly IRmsDepartmentCutoversRepository _cutovers;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IRmsSearchIndexStatesRepository _states;
		private readonly IRmsOperationalRecordDetailsRepository _details;
		private readonly IRecordsSearchIndexer _indexer;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDepartmentSettingsService _departmentSettings;

		public RecordsSearchIndexMaintenanceService(IRmsDepartmentCutoversRepository cutovers, IRmsRecordSearchProjectionsRepository projections,
			IRmsSearchIndexStatesRepository states, IRmsOperationalRecordDetailsRepository details, IRecordsSearchIndexer indexer,
			IDepartmentDataProtectionService dataProtection, IDepartmentSettingsService departmentSettings)
		{
			_cutovers = cutovers;
			_projections = projections;
			_states = states;
			_details = details;
			_indexer = indexer;
			_dataProtection = dataProtection;
			_departmentSettings = departmentSettings;
		}

		public async Task<RecordsSearchIndexSweepResult> SweepAsync(CancellationToken cancellationToken = default)
		{
			var result = new RecordsSearchIndexSweepResult();
			if (!SearchConfig.Enabled)
			{
				result.Skipped = true;
				result.Message = "Search host disabled.";
				return result;
			}

			var active = (await _cutovers.GetActiveAsync())?.ToList() ?? new List<RmsDepartmentCutover>();
			var rebuilds = 0;

			foreach (var cutover in active)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.DepartmentsChecked++;

				try
				{
					var generation = await ComputeGenerationAsync(cutover.DepartmentId);
					var state = await _states.GetAsync(cutover.DepartmentId, RmsSearchIndexState.RecordsIndexName);
					var needsRebuild = state == null || state.State != (int)RmsSearchIndexBuildState.Ready || !string.Equals(state.Generation, generation, StringComparison.Ordinal);

					if (needsRebuild)
					{
						if (rebuilds >= Math.Max(1, SearchConfig.MaxRebuildsPerSweep))
							continue;

						rebuilds++;
						await RebuildAsync(cutover.DepartmentId, generation, state, result, cancellationToken);
					}
					else
					{
						await CatchUpAsync(cutover.DepartmentId, generation, state, result, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Records search index maintenance failed for department {cutover.DepartmentId}.");
				}
			}

			if (result.DocumentsIndexed > 0 || result.DocumentsDeleted > 0 || result.DepartmentsRebuilt > 0)
				await _indexer.CommitAsync(cancellationToken);

			result.Message = $"Checked {result.DepartmentsChecked} department(s); rebuilt {result.DepartmentsRebuilt}; indexed {result.DocumentsIndexed}; deleted {result.DocumentsDeleted}; errors {result.Errors}.";
			return result;
		}

		public async Task<RecordsSearchIndexSweepResult> RebuildDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var result = new RecordsSearchIndexSweepResult { DepartmentsChecked = 1 };
			if (!SearchConfig.Enabled)
			{
				result.Skipped = true;
				result.Message = "Search host disabled.";
				return result;
			}

			var generation = await ComputeGenerationAsync(departmentId);
			var state = await _states.GetAsync(departmentId, RmsSearchIndexState.RecordsIndexName);
			await RebuildAsync(departmentId, generation, state, result, cancellationToken);
			await _indexer.CommitAsync(cancellationToken);
			result.Message = $"Rebuilt department {departmentId}: {result.DocumentsIndexed} document(s).";
			return result;
		}

		private async Task RebuildAsync(int departmentId, string generation, RmsSearchIndexState state, RecordsSearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			state = state ?? new RmsSearchIndexState { DepartmentId = departmentId, IndexName = RmsSearchIndexState.RecordsIndexName, CreatedOn = now };
			state.State = (int)RmsSearchIndexBuildState.Rebuilding;
			state.Generation = generation;
			ApplyGeneration(state, generation);
			state.ModifiedOn = now;
			state = await _states.SaveOrUpdateAsync(state, cancellationToken, true);

			try
			{
				await _indexer.DeleteDepartmentAsync(departmentId, cancellationToken);

				var includeNarrative = await NarrativeAllowedAsync(departmentId);
				DateTime? lastModified = null;
				var indexed = 0;
				var skip = 0;
				var batch = Math.Max(50, SearchConfig.IndexBatchSize);

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var page = (await _projections.QueryAsync(departmentId, new RmsRecordQuery { IncludeLegacy = true, Skip = skip, Take = batch }))?.ToList() ?? new List<RmsRecordSearchProjection>();
					if (page.Count == 0)
						break;

					var sources = await BuildSourcesAsync(departmentId, page, generation, includeNarrative);
					indexed += await _indexer.IndexAsync(sources, cancellationToken);
					lastModified = Max(lastModified, page.Max(p => p.ModifiedOn));

					skip += page.Count;
					if (page.Count < batch)
						break;
				}

				state.State = (int)RmsSearchIndexBuildState.Ready;
				state.DocumentCount = indexed;
				state.LastRebuiltOn = DateTime.UtcNow;
				state.LastIndexedModifiedOn = lastModified;
				state.ModifiedOn = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(state, cancellationToken, true);

				result.DepartmentsRebuilt++;
				result.DocumentsIndexed += indexed;
			}
			catch
			{
				state.State = (int)RmsSearchIndexBuildState.Failed;
				state.ModifiedOn = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(state, CancellationToken.None, true);
				throw;
			}
		}

		private async Task CatchUpAsync(int departmentId, string generation, RmsSearchIndexState state, RecordsSearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			var includeNarrative = await NarrativeAllowedAsync(departmentId);
			var since = state.LastIndexedModifiedOn;
			var batch = Math.Max(50, SearchConfig.IndexBatchSize);
			var touched = false;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var page = (await _projections.GetModifiedSinceAsync(departmentId, since, batch))?.ToList() ?? new List<RmsRecordSearchProjection>();
				if (page.Count == 0)
					break;

				var deleted = page.Where(p => p.DeletedOn.HasValue).ToList();
				var live = page.Where(p => !p.DeletedOn.HasValue).ToList();

				foreach (var gone in deleted)
					await _indexer.DeleteAsync(departmentId, gone.SourceType, gone.SourceId, cancellationToken);

				var indexed = await _indexer.IndexAsync(await BuildSourcesAsync(departmentId, live, generation, includeNarrative), cancellationToken);
				result.DocumentsIndexed += indexed;
				result.DocumentsDeleted += deleted.Count;
				touched = true;

				var pageMax = page.Max(p => p.ModifiedOn);
				if (since.HasValue && pageMax <= since.Value)
					break;
				since = pageMax;

				if (page.Count < batch)
					break;
			}

			if (touched)
			{
				state.LastIndexedModifiedOn = since;
				state.DocumentCount = await _indexer.CountDocumentsAsync(departmentId);
				state.ModifiedOn = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(state, cancellationToken, true);
			}
		}

		private async Task<List<RecordsSearchDocumentSource>> BuildSourcesAsync(int departmentId, List<RmsRecordSearchProjection> projections, string generation, bool includeNarrative)
		{
			var sources = new List<RecordsSearchDocumentSource>(projections.Count);
			foreach (var projection in projections)
			{
				string narrative = null;
				if (includeNarrative && !projection.IsLegacy && !projection.DeletedOn.HasValue)
				{
					try
					{
						narrative = (await _details.GetDraftAsync(departmentId, projection.SourceId))?.Narrative;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Narrative lookup failed for record {projection.SourceId}; indexing metadata only.");
					}
				}

				sources.Add(new RecordsSearchDocumentSource { Projection = projection, Narrative = narrative, Generation = generation });
			}

			return sources;
		}

		/// <summary>Narrative search is available to unprotected departments that opted in and withdrawn on enrollment (plan section 5.10).</summary>
		private async Task<bool> NarrativeAllowedAsync(int departmentId)
		{
			try
			{
				if (await _dataProtection.IsProtectionEnforcedAsync(departmentId))
					return false;

				var config = await _departmentSettings.GetRecordsSearchConfigAsync(departmentId, true);
				return config != null && config.IndexNarrative;
			}
			catch (Exception ex)
			{
				// Unknown protection state never widens exposure: index metadata only.
				Logging.LogException(ex, $"Narrative indexing eligibility could not be determined for department {departmentId}; indexing metadata only.");
				return false;
			}
		}

		private async Task<string> ComputeGenerationAsync(int departmentId)
		{
			var catalogVersion = 0;
			long policyEpoch = 0;
			try { catalogVersion = await _dataProtection.GetPinnedCatalogVersionAsync(departmentId); } catch (Exception ex) { Logging.LogException(ex); }
			try { policyEpoch = (await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId))?.PolicyEpoch ?? 0; } catch (Exception ex) { Logging.LogException(ex); }
			return RecordsSearchGeneration.Compute(catalogVersion, policyEpoch);
		}

		private static void ApplyGeneration(RmsSearchIndexState state, string generation)
		{
			var parts = (generation ?? string.Empty).Split('.');
			state.SchemaVersion = parts.Length > 0 && int.TryParse(parts[0], out var schema) ? schema : RecordsSearchGeneration.SchemaVersion;
			state.ProtectedCatalogVersion = parts.Length > 1 && int.TryParse(parts[1], out var catalog) ? catalog : 0;
			state.PolicyEpoch = parts.Length > 2 && long.TryParse(parts[2], out var epoch) ? epoch : 0;
		}

		private static DateTime? Max(DateTime? a, DateTime b)
		{
			return !a.HasValue || b > a.Value ? b : a;
		}
	}
}
