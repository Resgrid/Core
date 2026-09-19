using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services.Search
{
	/// <summary>
	/// Worker command 70 (plan R4 Phase 2, registry §4F): per department with a state row for the global index, compare
	/// the stored generation key with (schemaVersion, protectedCatalogVersion, policyEpoch); rebuild on mismatch, on
	/// an admin request, or when the local index is empty while the state says otherwise (missing-index rule, R7);
	/// otherwise catch up rows modified since the last sweep. A rebuild regenerates the projection rows from the
	/// entity services first, then re-indexes them, so the projection table stays the source of truth and the index is
	/// always rebuildable from it. Departments enter the sweep lazily: the first unified search or an admin request
	/// creates their state row.
	/// </summary>
	public class SearchIndexMaintenanceService : ISearchIndexMaintenanceService
	{
		private readonly ISearchIndexStatesRepository _states;
		private readonly ISearchProjectionsRepository _projections;
		private readonly IGlobalSearchIndexer _indexer;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IFeatureToggleService _featureToggles;
		private readonly ISearchProjectionService _projectionService;
		private readonly ICallsService _calls;
		private readonly IUnitsService _units;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IContactsService _contacts;
		private readonly IMessageService _messages;
		private readonly IDocumentsService _documents;
		private readonly INotesService _notes;

		public SearchIndexMaintenanceService(ISearchIndexStatesRepository states, ISearchProjectionsRepository projections, IGlobalSearchIndexer indexer,
			IDepartmentDataProtectionService dataProtection, IFeatureToggleService featureToggles, ISearchProjectionService projectionService,
			ICallsService calls, IUnitsService units, IUserProfileService profiles, IDepartmentsService departments, IDepartmentGroupsService groups,
			IContactsService contacts, IMessageService messages, IDocumentsService documents, INotesService notes)
		{
			_states = states;
			_projections = projections;
			_indexer = indexer;
			_dataProtection = dataProtection;
			_featureToggles = featureToggles;
			_projectionService = projectionService;
			_calls = calls;
			_units = units;
			_profiles = profiles;
			_departments = departments;
			_groups = groups;
			_contacts = contacts;
			_messages = messages;
			_documents = documents;
			_notes = notes;
		}

		public async Task<SearchIndexSweepResult> SweepAsync(CancellationToken cancellationToken = default)
		{
			var result = new SearchIndexSweepResult();
			if (!SearchConfig.Enabled)
			{
				result.Skipped = true;
				result.Message = "Search host disabled; global index sweep skipped.";
				return result;
			}

			var states = (await _states.GetAllForIndexAsync(SearchIndexNames.Global))?.ToList() ?? new List<SearchIndexState>();
			var rebuilds = 0;

			foreach (var state in states)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.DepartmentsChecked++;

				try
				{
					if (!await FlagOnAsync(state.DepartmentId))
						continue;

					var generation = await ComputeGenerationAsync(state.DepartmentId);
					var needsRebuild = state.State != (int)SearchIndexBuildState.Ready
						|| state.RebuildRequestedOn.HasValue
						|| !string.Equals(state.Generation, generation, StringComparison.Ordinal);

					// Missing-index rule (plan R7): fresh pod, empty bucket or wiped cache.
					if (!needsRebuild && state.DocumentCount > 0 && await _indexer.CountDocumentsAsync(state.DepartmentId) == 0)
						needsRebuild = true;

					if (needsRebuild)
					{
						if (rebuilds >= Math.Max(1, SearchConfig.MaxRebuildsPerSweep))
							continue;
						rebuilds++;
						await RebuildAsync(state.DepartmentId, generation, state, result, cancellationToken);
					}
					else
					{
						await CatchUpAsync(state.DepartmentId, generation, state, result, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Global search index maintenance failed for department {state.DepartmentId}.");
				}
			}

			result.Message = $"Checked {result.DepartmentsChecked} department(s); rebuilt {result.DepartmentsRebuilt} ({result.ProjectionsRebuilt} projections); indexed {result.DocumentsIndexed}; deleted {result.DocumentsDeleted}; errors {result.Errors}.";
			return result;
		}

		public async Task<SearchIndexSweepResult> RebuildDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var result = new SearchIndexSweepResult { DepartmentsChecked = 1 };
			if (!SearchConfig.Enabled)
			{
				result.Skipped = true;
				result.Message = "Search host disabled.";
				return result;
			}

			var generation = await ComputeGenerationAsync(departmentId);
			var state = await _states.GetAsync(SearchIndexNames.Global, departmentId);
			await RebuildAsync(departmentId, generation, state, result, cancellationToken);
			result.Message = $"Rebuilt department {departmentId}: {result.ProjectionsRebuilt} projection(s), {result.DocumentsIndexed} document(s).";
			return result;
		}

		public async Task<SearchIndexState> RequestRebuildAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var state = await _states.GetAsync(SearchIndexNames.Global, departmentId)
				?? new SearchIndexState { IndexName = SearchIndexNames.Global, DepartmentId = departmentId, CreatedOn = now, Generation = GlobalSearchGeneration.Compute(0, 0), SchemaVersion = GlobalSearchGeneration.SchemaVersion };
			state.State = (int)SearchIndexBuildState.RebuildRequested;
			state.RebuildRequestedOn = now;
			state.ModifiedOn = now;
			return await _states.SaveOrUpdateAsync(state, cancellationToken, true);
		}

		private async Task RebuildAsync(int departmentId, string generation, SearchIndexState state, SearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			state = state ?? new SearchIndexState { IndexName = SearchIndexNames.Global, DepartmentId = departmentId, CreatedOn = now };
			state.State = (int)SearchIndexBuildState.Rebuilding;
			state.Generation = generation;
			ApplyGeneration(state, generation);
			state.ModifiedOn = now;
			state = await _states.SaveOrUpdateAsync(state, cancellationToken, true);

			try
			{
				result.ProjectionsRebuilt += await RebuildProjectionsAsync(departmentId, cancellationToken);

				await _indexer.DeleteDepartmentAsync(departmentId, cancellationToken);

				DateTime? lastModified = null;
				var indexed = 0;
				var skip = 0;
				var batch = Math.Max(50, SearchConfig.IndexBatchSize);
				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var page = (await _projections.GetLivePageAsync(departmentId, skip, batch))?.ToList() ?? new List<SearchProjection>();
					if (page.Count == 0)
						break;
					indexed += await _indexer.IndexAsync(page, generation, cancellationToken);
					lastModified = Max(lastModified, page.Max(p => p.ModifiedOn));
					skip += page.Count;
					if (page.Count < batch)
						break;
				}

				// The durable checkpoint must never lead the committed (and published) segments.
				await _indexer.CommitAsync(cancellationToken);
				state.State = (int)SearchIndexBuildState.Ready;
				state.DocumentCount = indexed;
				state.LastRebuiltOn = DateTime.UtcNow;
				state.LastIndexedModifiedOn = lastModified.HasValue && lastModified.Value > now ? now : lastModified;
				state.RebuildRequestedOn = null;
				state.ModifiedOn = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(state, cancellationToken, true);

				result.DepartmentsRebuilt++;
				result.DocumentsIndexed += indexed;
			}
			catch
			{
				state.State = (int)SearchIndexBuildState.Failed;
				state.ModifiedOn = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(state, CancellationToken.None, true);
				throw;
			}
		}

		private async Task CatchUpAsync(int departmentId, string generation, SearchIndexState state, SearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			var checkpoint = state.LastIndexedModifiedOn;
			var since = checkpoint.HasValue && checkpoint.Value > DateTime.MinValue.AddSeconds(1) ? checkpoint.Value.AddSeconds(-1) : checkpoint;
			string sinceId = null;
			var batch = Math.Max(50, Math.Min(5000, SearchConfig.IndexBatchSize));
			var touched = false;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var page = (await _projections.GetModifiedSinceAsync(departmentId, since, batch, sinceId))?.ToList() ?? new List<SearchProjection>();
				if (page.Count == 0)
					break;

				var deleted = page.Where(p => p.DeletedOn.HasValue).ToList();
				var live = page.Where(p => !p.DeletedOn.HasValue).ToList();

				foreach (var gone in deleted)
					await _indexer.DeleteAsync(departmentId, gone.EntityType, gone.EntityId, cancellationToken);

				result.DocumentsIndexed += await _indexer.IndexAsync(live, generation, cancellationToken);
				result.DocumentsDeleted += deleted.Count;
				touched = true;

				var last = page[page.Count - 1];
				if (since.HasValue && (last.ModifiedOn < since.Value || last.ModifiedOn == since.Value && string.Equals(last.SearchProjectionId, sinceId, StringComparison.Ordinal)))
					throw new InvalidOperationException("The search change cursor did not advance; its checkpoint was not saved.");
				since = last.ModifiedOn;
				sinceId = last.SearchProjectionId;
				checkpoint = Max(checkpoint, last.ModifiedOn);

				if (page.Count < batch)
					break;
			}

			if (touched)
			{
				await _indexer.CommitAsync(cancellationToken);
				state.LastIndexedModifiedOn = checkpoint;
				state.DocumentCount = await _indexer.CountDocumentsAsync(departmentId);
				state.ModifiedOn = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(state, cancellationToken, true);
			}
		}

		/// <summary>Regenerates every projection row of the department from the entity services, then soft-deletes rows no longer present.</summary>
		private async Task<int> RebuildProjectionsAsync(int departmentId, CancellationToken cancellationToken)
		{
			var started = DateTime.UtcNow;
			var count = 0;

			count += await Family(departmentId, SearchEntityTypes.Call, async () =>
			{
				var calls = new Dictionary<int, Call>();
				foreach (var c in await _calls.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>())
					calls[c.CallId] = c;
				var year = DateTime.UtcNow.Year;
				for (var i = 0; i < Math.Max(1, SearchConfig.CallRebuildYears); i++)
				{
					foreach (var c in await _calls.GetClosedCallsByDepartmentYearAsync(departmentId, (year - i).ToString()) ?? new List<Call>())
						calls[c.CallId] = c;
				}
				var n = 0;
				foreach (var call in calls.Values)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (call.IsDeleted) continue;
					var p = await _projectionService.BuildCallAsync(call);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			count += await Family(departmentId, SearchEntityTypes.Unit, async () =>
			{
				var n = 0;
				foreach (var unit in await _units.GetUnitsForDepartmentUnlimitedAsync(departmentId) ?? new List<Unit>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					var p = await _projectionService.BuildUnitAsync(unit);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			count += await Family(departmentId, SearchEntityTypes.Personnel, async () =>
			{
				var members = await _departments.GetAllMembersForDepartmentAsync(departmentId) ?? new List<DepartmentMember>();
				var profiles = await _profiles.GetAllProfilesForDepartmentIncDisabledDeletedAsync(departmentId) ?? new Dictionary<string, UserProfile>();
				Dictionary<string, DepartmentGroup> groups;
				try { groups = await _groups.GetAllDepartmentGroupsForDepartmentAsync(departmentId) ?? new Dictionary<string, DepartmentGroup>(); }
				catch (Exception ex) { Logging.LogException(ex); groups = new Dictionary<string, DepartmentGroup>(); }
				var n = 0;
				foreach (var member in members)
				{
					cancellationToken.ThrowIfCancellationRequested();
					// Same membership rule as DepartmentsService.SaveDepartmentMemberAsync: disabled and hidden members are
					// off the personnel list, so they stay out of the index too (SoftDeleteStaleAsync retires their row).
					if (member.IsDeleted || member.IsDisabled.GetValueOrDefault() || member.IsHidden.GetValueOrDefault() || string.IsNullOrWhiteSpace(member.UserId)) continue;
					profiles.TryGetValue(member.UserId, out var profile);
					if (profile == null) continue;
					groups.TryGetValue(member.UserId, out var group);
					var p = await _projectionService.BuildPersonnelAsync(departmentId, profile, group?.DepartmentGroupId, member.IsActive);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			count += await Family(departmentId, SearchEntityTypes.Contact, async () =>
			{
				var n = 0;
				foreach (var contact in await _contacts.GetAllContactsForDepartmentAsync(departmentId) ?? new List<Contact>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (contact.IsDeleted) continue;
					var p = await _projectionService.BuildContactAsync(contact);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			count += await Family(departmentId, SearchEntityTypes.Document, async () =>
			{
				var n = 0;
				foreach (var document in await _documents.GetAllDocumentsByDepartmentIdAsync(departmentId) ?? new List<Document>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					var p = await _projectionService.BuildDocumentAsync(document);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			count += await Family(departmentId, SearchEntityTypes.Note, async () =>
			{
				var n = 0;
				foreach (var note in await _notes.GetAllNotesForDepartmentAsync(departmentId) ?? new List<Note>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					var p = await _projectionService.BuildNoteAsync(note);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			count += await Family(departmentId, SearchEntityTypes.Message, async () =>
			{
				// One department-scoped read (M0137 owner column) instead of two folder queries per member. A message
				// that was never attributed to a department has no projection either way: BuildMessageAsync needs
				// the owner, and the per-member walk this replaces filtered on the same column.
				var seen = new HashSet<int>();
				var n = 0;
				foreach (var message in await _messages.GetAllMessagesForDepartmentAsync(departmentId) ?? new List<Message>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (message == null || message.IsDeleted || !seen.Add(message.MessageId))
						continue;
					var p = await _projectionService.BuildMessageAsync(message);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);

			return count;
		}

		private async Task<int> Family(int departmentId, string entityType, Func<Task<int>> rebuild, DateTime started, CancellationToken cancellationToken)
		{
			try
			{
				var n = await rebuild();
				await _projections.SoftDeleteStaleAsync(departmentId, entityType, started, cancellationToken);
				return n;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Search projection rebuild failed for family {entityType}; existing rows are kept.");
				return 0;
			}
		}

		private async Task<bool> FlagOnAsync(int departmentId)
		{
			try { return await _featureToggles.IsEnabledAsync(FeatureFlagKeys.SearchUnified, departmentId); }
			catch (Exception ex) { Logging.LogException(ex); return false; }
		}

		private async Task<string> ComputeGenerationAsync(int departmentId)
		{
			var catalogVersion = 0;
			long policyEpoch = 0;
			try { catalogVersion = await _dataProtection.GetPinnedCatalogVersionAsync(departmentId); } catch (Exception ex) { Logging.LogException(ex); }
			try { policyEpoch = (await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId))?.PolicyEpoch ?? 0; } catch (Exception ex) { Logging.LogException(ex); }
			return GlobalSearchGeneration.Compute(catalogVersion, policyEpoch);
		}

		private static void ApplyGeneration(SearchIndexState state, string generation)
		{
			var parts = (generation ?? string.Empty).Split('.');
			state.SchemaVersion = parts.Length > 0 && int.TryParse(parts[0], out var schema) ? schema : GlobalSearchGeneration.SchemaVersion;
			state.ProtectedCatalogVersion = parts.Length > 1 && int.TryParse(parts[1], out var catalog) ? catalog : 0;
			state.PolicyEpoch = parts.Length > 2 && long.TryParse(parts[2], out var epoch) ? epoch : 0;
		}

		private static DateTime? Max(DateTime? a, DateTime b)
		{
			return !a.HasValue || b > a.Value ? b : a;
		}
	}
}
