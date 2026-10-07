using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
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
		/// <summary>
		/// The invoice, bid and deployment repositories clamp a page at 500 rows; a single larger read silently returned the
		/// newest 500 and the family's SoftDeleteStaleAsync then retired every projection past them.
		/// </summary>
		private const int RebuildPageSize = 500;

		/// <summary>
		/// One sweep per process at a time. The scheduler starts a sweep every minute (and retries a failed one) whether or not
		/// the previous run has finished. Two sweeps in one worker shared the host's single writer and the publish lease — the
		/// lease admits its own owner, so both got in — raced the manifest ETag, and the loser's conflict reset wiped the local
		/// index under the winner. An overlapping sweep now skips instead.
		/// </summary>
		private static readonly SemaphoreSlim SweepGate = new SemaphoreSlim(1, 1);

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
		private readonly Lazy<IInvoicingService> _invoicing;
		private readonly Lazy<IBidsService> _bids;
		private readonly Lazy<IServiceContractService> _contracts;
		private readonly Lazy<IDeploymentService> _deploymentsService;
		private readonly Lazy<ICertificationService> _certifications;
		private readonly Lazy<IProtocolsService> _protocols;
		private readonly Lazy<ITrainingService> _trainings;
		private readonly Lazy<ICalendarService> _calendar;
		private readonly Lazy<IWorkLogsService> _logs;
		private readonly Lazy<IMappingService> _mapping;
		private readonly Lazy<IShiftsService> _shifts;
		private readonly IRmsOccupanciesRepository _occupancies;

		public SearchIndexMaintenanceService(ISearchIndexStatesRepository states, ISearchProjectionsRepository projections, IGlobalSearchIndexer indexer,
			IDepartmentDataProtectionService dataProtection, IFeatureToggleService featureToggles, ISearchProjectionService projectionService,
			ICallsService calls, IUnitsService units, IUserProfileService profiles, IDepartmentsService departments, IDepartmentGroupsService groups,
			IContactsService contacts, IMessageService messages, IDocumentsService documents, INotesService notes,
			Lazy<IInvoicingService> invoicing = null, Lazy<IBidsService> bids = null, Lazy<IServiceContractService> contracts = null,
			Lazy<IDeploymentService> deploymentsService = null, Lazy<ICertificationService> certifications = null,
			Lazy<IProtocolsService> protocols = null, Lazy<ITrainingService> trainings = null, Lazy<ICalendarService> calendar = null,
			Lazy<IWorkLogsService> logs = null, Lazy<IMappingService> mapping = null, Lazy<IShiftsService> shifts = null,
			IRmsOccupanciesRepository occupancies = null)
		{
			_occupancies = occupancies;
			_protocols = protocols;
			_trainings = trainings;
			_calendar = calendar;
			_logs = logs;
			_mapping = mapping;
			_shifts = shifts;
			_invoicing = invoicing;
			_bids = bids;
			_contracts = contracts;
			_deploymentsService = deploymentsService;
			_certifications = certifications;
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

			if (!await SweepGate.WaitAsync(0, cancellationToken))
			{
				result.Skipped = true;
				result.Message = "The previous global index sweep is still running in this process; skipped.";
				return result;
			}

			try
			{
				await SweepCoreAsync(result, cancellationToken);
				await BackfillCallsAsync(result, cancellationToken);
			}
			finally
			{
				SweepGate.Release();
			}

			result.Message = $"Checked {result.DepartmentsChecked} department(s); rebuilt {result.DepartmentsRebuilt} ({result.ProjectionsRebuilt} projections); indexed {result.DocumentsIndexed}; deleted {result.DocumentsDeleted}; backfilled {result.CallsBackfilled} call(s), {result.CallBackfillsCompleted} department(s) finished; errors {result.Errors}.";
			return result;
		}

		/// <summary>
		/// The call history backfill (M0261). Rebuilds once projected only recent years and retired older calls; rather than
		/// change the index generation, which blanks every department's results until its rebuild, each sweep walks one batch
		/// of every pending department's calls, newest CallId first, and projects only the calls search does not hold. The
		/// rows reach the index through the next sweep's catch-up (their ModifiedOn is past the checkpoint). Runs after the
		/// sweep's commit, in a time budget, least recently touched department first; the cursor is saved per batch, so an
		/// interrupted walk resumes where it stopped.
		/// </summary>
		private async Task BackfillCallsAsync(SearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			if (SearchConfig.CallBackfillSecondsPerSweep <= 0 || SearchConfig.CallRebuildYears > 0)
				return;

			var watch = System.Diagnostics.Stopwatch.StartNew();
			var budget = TimeSpan.FromSeconds(SearchConfig.CallBackfillSecondsPerSweep);
			var batch = Math.Max(1, Math.Min(1000, SearchConfig.CallBackfillBatchSize));
			List<SearchIndexState> pending;
			try
			{
				pending = ((await _states.GetAllForIndexAsync(SearchIndexNames.Global)) ?? Enumerable.Empty<SearchIndexState>())
					.Where(s => s.State == (int)SearchIndexBuildState.Ready && !s.RebuildRequestedOn.HasValue && !s.CallBackfillCompletedOn.HasValue)
					.OrderBy(s => s.ModifiedOn).ThenBy(s => s.DepartmentId).ToList();
			}
			catch (Exception ex)
			{
				result.Errors++;
				Logging.LogException(ex, "Search call history backfill could not list departments.");
				return;
			}

			foreach (var listed in pending)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (watch.Elapsed >= budget)
					break;

				try
				{
					if (!await FlagOnAsync(listed.DepartmentId))
						continue;

					// Re-read just before the batch: the whole row is saved back, and an admin rebuild request made since the
					// list was read must not be overwritten.
					var state = await _states.GetAsync(SearchIndexNames.Global, listed.DepartmentId);
					if (state == null || state.State != (int)SearchIndexBuildState.Ready || state.RebuildRequestedOn.HasValue || state.CallBackfillCompletedOn.HasValue)
						continue;

					var calls = (await _projections.GetCallsForBackfillAsync(state.DepartmentId, state.CallBackfillCursor, batch))?.ToList() ?? new List<Call>();
					if (calls.Count > 0)
					{
						var present = new HashSet<string>(((await _projections.GetByEntityIdsAsync(state.DepartmentId, SearchEntityTypes.Call,
							calls.Select(c => c.CallId.ToString(CultureInfo.InvariantCulture)))) ?? Enumerable.Empty<SearchProjection>())
							.Select(p => p.EntityId), StringComparer.Ordinal);
						foreach (var call in calls)
						{
							cancellationToken.ThrowIfCancellationRequested();
							if (call == null || call.IsDeleted || present.Contains(call.CallId.ToString(CultureInfo.InvariantCulture)))
								continue;
							var projection = await _projectionService.BuildCallAsync(call);
							if (projection == null)
								continue;
							await _projectionService.UpsertAsync(projection, cancellationToken);
							result.CallsBackfilled++;
						}

						state.CallBackfillCursor = calls.Min(c => c.CallId);
					}

					var now = DateTime.UtcNow;
					if (calls.Count < batch)
					{
						state.CallBackfillCompletedOn = now;
						result.CallBackfillsCompleted++;
					}
					state.ModifiedOn = now;
					await _states.SaveOrUpdateAsync(state, cancellationToken, true);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Search call history backfill failed for department {listed.DepartmentId}; it resumes from its cursor on the next sweep.");
				}
			}
		}

		private async Task SweepCoreAsync(SearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			var states = (await _states.GetAllForIndexAsync(SearchIndexNames.Global))?.ToList() ?? new List<SearchIndexState>();
			var rebuilds = 0;
			// Catch-up checkpoints wait for the sweep's single commit: a checkpoint must never lead the committed segments.
			var pending = new List<(SearchIndexState State, DateTime? Checkpoint)>();

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
						var (changed, checkpoint) = await CatchUpAsync(state.DepartmentId, generation, state, result, cancellationToken);
						if (changed)
							pending.Add((state, checkpoint));
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

			if (pending.Count == 0)
				return;

			try
			{
				// One commit-and-publish per sweep, not one per department: each publish lists the bucket, uploads the new
				// segments and conditionally replaces the manifest, so a per-department commit made every quiet minute with N
				// activated departments cost N publishes.
				await _indexer.CommitAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				result.Errors++;
				Logging.LogException(ex, $"Global search index commit failed; {pending.Count} department checkpoint(s) were not advanced and the next sweep re-reads their rows.");
				return;
			}

			foreach (var (state, checkpoint) in pending)
			{
				try
				{
					state.LastIndexedModifiedOn = checkpoint;
					state.DocumentCount = await _indexer.CountDocumentsAsync(state.DepartmentId);
					state.ModifiedOn = DateTime.UtcNow;
					await _states.SaveOrUpdateAsync(state, cancellationToken, true);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Global search index checkpoint could not be saved for department {state.DepartmentId}; the next sweep re-reads its rows.");
				}
			}
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

			await SweepGate.WaitAsync(cancellationToken);
			try
			{
				var generation = await ComputeGenerationAsync(departmentId);
				var state = await _states.GetAsync(SearchIndexNames.Global, departmentId);
				await RebuildAsync(departmentId, generation, state, result, cancellationToken);
			}
			finally
			{
				SweepGate.Release();
			}

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
				var (projected, callsComplete) = await RebuildProjectionsAsync(departmentId, cancellationToken);
				result.ProjectionsRebuilt += projected;

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
				// A rebuild that projected every call leaves nothing to backfill; one whose call family failed (or that an
				// operator capped with CallRebuildYears) restarts the backfill walk from the newest call.
				state.CallBackfillCursor = null;
				state.CallBackfillCompletedOn = callsComplete && SearchConfig.CallRebuildYears <= 0 ? DateTime.UtcNow : (DateTime?)null;
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

		/// <summary>
		/// Writes the rows modified since the department's checkpoint into the index without committing. Returns whether the
		/// index changed and the checkpoint to save once the caller's commit has succeeded.
		/// </summary>
		private async Task<(bool Changed, DateTime? Checkpoint)> CatchUpAsync(int departmentId, string generation, SearchIndexState state, SearchIndexSweepResult result, CancellationToken cancellationToken)
		{
			var stored = state.LastIndexedModifiedOn;
			var checkpoint = stored;
			// The read re-covers the last second before the checkpoint so a row stamped just before it but committed after
			// the previous read is not lost. Rows it re-reads unchanged are skipped below; re-indexing them made every quiet
			// sweep commit and publish every department.
			var since = stored.HasValue && stored.Value > DateTime.MinValue.AddSeconds(1) ? stored.Value.AddSeconds(-1) : stored;
			string sinceId = null;
			var batch = Math.Max(50, Math.Min(5000, SearchConfig.IndexBatchSize));
			var touched = false;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var page = (await _projections.GetModifiedSinceAsync(departmentId, since, batch, sinceId))?.ToList() ?? new List<SearchProjection>();
				if (page.Count == 0)
					break;

				var changed = await WithoutUnchangedOverlapAsync(departmentId, page, stored);
				var deleted = changed.Where(p => p.DeletedOn.HasValue).ToList();
				var live = changed.Where(p => !p.DeletedOn.HasValue).ToList();

				foreach (var gone in deleted)
					await _indexer.DeleteAsync(departmentId, gone.EntityType, gone.EntityId, cancellationToken);

				if (live.Count > 0)
					result.DocumentsIndexed += await _indexer.IndexAsync(live, generation, cancellationToken);
				result.DocumentsDeleted += deleted.Count;
				touched |= changed.Count > 0;

				var last = page[page.Count - 1];
				if (since.HasValue && (last.ModifiedOn < since.Value || last.ModifiedOn == since.Value && string.Equals(last.SearchProjectionId, sinceId, StringComparison.Ordinal)))
					throw new InvalidOperationException("The search change cursor did not advance; its checkpoint was not saved.");
				since = last.ModifiedOn;
				sinceId = last.SearchProjectionId;
				checkpoint = Max(checkpoint, last.ModifiedOn);

				if (page.Count < batch)
					break;
			}

			return (touched, checkpoint);
		}

		/// <summary>
		/// Drops the rows of the overlap window (stamped at or before the stored checkpoint) that the index already holds at
		/// the same RowVersion, and deleted rows the index no longer holds. Rows past the checkpoint are always kept.
		/// </summary>
		private async Task<List<SearchProjection>> WithoutUnchangedOverlapAsync(int departmentId, List<SearchProjection> page, DateTime? stored)
		{
			if (!stored.HasValue)
				return page;

			var overlap = page.Where(p => p.ModifiedOn <= stored.Value).ToList();
			if (overlap.Count == 0)
				return page;

			var indexed = await _indexer.GetIndexedRowVersionsAsync(departmentId, overlap) ?? new Dictionary<string, long>();
			return page.Where(p =>
			{
				if (p.ModifiedOn > stored.Value)
					return true;
				var present = indexed.TryGetValue(p.SearchProjectionId ?? string.Empty, out var version);
				return p.DeletedOn.HasValue ? present : !present || version != p.RowVersion;
			}).ToList();
		}

		/// <summary>
		/// Regenerates every projection row of the department from the entity services, then soft-deletes rows no longer
		/// present. CallsComplete reports whether the call family walked every call it was asked to (its failure keeps the
		/// existing rows, and the call history backfill then covers the gap).
		/// </summary>
		private async Task<(int Count, bool CallsComplete)> RebuildProjectionsAsync(int departmentId, CancellationToken cancellationToken)
		{
			var started = DateTime.UtcNow;
			var count = 0;
			var callsComplete = false;

			count += await Family(departmentId, SearchEntityTypes.Call, async () =>
			{
				// Active calls, then closed calls one year at a time (newest first) so only one year is held in memory. Every
				// year the department has calls in is walked unless CallRebuildYears caps it: the family's stale-row sweep
				// retires any call left out, which used to drop older calls from search on every rebuild.
				var seen = new HashSet<int>();
				var n = 0;
				async Task Project(IEnumerable<Call> calls)
				{
					foreach (var call in calls ?? Enumerable.Empty<Call>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (call == null || call.IsDeleted || !seen.Add(call.CallId)) continue;
						var p = await _projectionService.BuildCallAsync(call);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
				}

				await Project(await _calls.GetActiveCallsByDepartmentAsync(departmentId));
				foreach (var year in await CallRebuildYearsAsync(departmentId))
					await Project(await _calls.GetClosedCallsByDepartmentYearAsync(departmentId, year.ToString(CultureInfo.InvariantCulture)));
				callsComplete = true;
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

			// Workforce & Business Operations families (decision 41). Each service is Lazy so the search worker never forms a construction cycle with them.
			if (_invoicing?.Value != null)
			{
				count += await Family(departmentId, SearchEntityTypes.Invoice, async () =>
				{
					var n = 0;
					for (var skip = 0; ; skip += RebuildPageSize)
					{
						var page = await _invoicing.Value.GetInvoicesForDepartmentAsync(departmentId, new InvoiceListFilter { Skip = skip, Take = RebuildPageSize }) ?? new List<Invoice>();
						foreach (var invoice in page)
						{
							cancellationToken.ThrowIfCancellationRequested();
							var p = await _projectionService.BuildInvoiceAsync(invoice);
							if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
						}
						if (page.Count < RebuildPageSize) break;
					}
					return n;
				}, started, cancellationToken);
				count += await Family(departmentId, SearchEntityTypes.RateCard, async () =>
				{
					var n = 0;
					foreach (var card in await _invoicing.Value.GetRateCardsForDepartmentAsync(departmentId) ?? new List<RateCard>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildRateCardAsync(card);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			}
			if (_bids?.Value != null)
			{
				count += await Family(departmentId, SearchEntityTypes.Bid, async () =>
				{
					var n = 0;
					for (var skip = 0; ; skip += RebuildPageSize)
					{
						var page = await _bids.Value.GetBidsForDepartmentAsync(departmentId, null, skip, RebuildPageSize) ?? new List<Bid>();
						foreach (var bid in page)
						{
							cancellationToken.ThrowIfCancellationRequested();
							var p = await _projectionService.BuildBidAsync(bid);
							if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
						}
						if (page.Count < RebuildPageSize) break;
					}
					return n;
				}, started, cancellationToken);
			}
			if (_contracts?.Value != null)
			{
				count += await Family(departmentId, SearchEntityTypes.ServiceContract, async () =>
				{
					var n = 0;
					foreach (var contract in await _contracts.Value.GetContractsForDepartmentAsync(departmentId) ?? new List<ServiceContract>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildServiceContractAsync(contract);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			}
			if (_deploymentsService?.Value != null)
			{
				count += await Family(departmentId, SearchEntityTypes.Deployment, async () =>
				{
					var n = 0;
					for (var skip = 0; ; skip += RebuildPageSize)
					{
						var page = await _deploymentsService.Value.GetDeploymentsForDepartmentAsync(departmentId, false, skip, RebuildPageSize) ?? new List<Deployment>();
						foreach (var deployment in page)
						{
							cancellationToken.ThrowIfCancellationRequested();
							var p = await _projectionService.BuildDeploymentAsync(deployment);
							if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
						}
						if (page.Count < RebuildPageSize) break;
					}
					return n;
				}, started, cancellationToken);
			}
			if (_certifications?.Value != null)
			{
				count += await Family(departmentId, SearchEntityTypes.CertificationType, async () =>
				{
					var n = 0;
					foreach (var type in await _certifications.Value.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildCertificationTypeAsync(type);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			}

			// Operations reference families (plan R3 Tier 2). Lazy for the same reason as the Business Operations ones.
			count += await Family(departmentId, SearchEntityTypes.Group, async () =>
			{
				var n = 0;
				foreach (var group in await _groups.GetAllGroupsForDepartmentUnlimitedAsync(departmentId) ?? new List<DepartmentGroup>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					var p = await _projectionService.BuildGroupAsync(group);
					if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
				}
				return n;
			}, started, cancellationToken);
			if (_protocols?.Value != null)
				count += await Family(departmentId, SearchEntityTypes.Protocol, async () =>
				{
					var n = 0;
					foreach (var protocol in await _protocols.Value.GetAllProtocolsForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildProtocolAsync(protocol);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			if (_trainings?.Value != null)
				count += await Family(departmentId, SearchEntityTypes.Training, async () =>
				{
					var n = 0;
					foreach (var training in await _trainings.Value.GetAllTrainingsForDepartmentAsync(departmentId) ?? new List<Training>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildTrainingAsync(training);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			if (_calendar?.Value != null)
				count += await Family(departmentId, SearchEntityTypes.CalendarEvent, async () =>
				{
					var n = 0;
					// Occurrences of a recurring series build to null; the parent row carries the event.
					foreach (var item in await _calendar.Value.GetAllCalendarItemsForDepartmentAsync(departmentId) ?? new List<CalendarItem>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildCalendarItemAsync(item);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			if (_logs?.Value != null)
				count += await Family(departmentId, SearchEntityTypes.Log, async () =>
				{
					var n = 0;
					foreach (var log in await _logs.Value.GetAllLogsForDepartmentAsync(departmentId) ?? new List<Log>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildLogAsync(log);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			if (_mapping?.Value != null)
				count += await Family(departmentId, SearchEntityTypes.Poi, async () =>
				{
					var n = 0;
					// The department read returns each POI with its type attached; POIs carry their department only through it.
					foreach (var poi in await _mapping.Value.GetPOIsForDepartmentAsync(departmentId) ?? new List<Poi>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildPoiAsync(poi, poi.Type);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);
			if (_shifts?.Value != null)
				count += await Family(departmentId, SearchEntityTypes.Shift, async () =>
				{
					var n = 0;
					foreach (var shift in await _shifts.Value.GetAllShiftsByDepartmentAsync(departmentId) ?? new List<Shift>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildShiftAsync(shift);
						if (p != null) { await _projectionService.UpsertAsync(p, cancellationToken); n++; }
					}
					return n;
				}, started, cancellationToken);

			if (_occupancies != null)
				count += await Family(departmentId, SearchEntityTypes.Occupancy, async () =>
				{
					// Read straight from the repository: the occupancy service's reads are per viewer, and a rebuild has none.
					// Removed and merged occupancies build to null.
					var n = 0;
					foreach (var occupancy in await _occupancies.GetAllLiveAsync(departmentId) ?? Enumerable.Empty<RmsOccupancy>())
					{
						cancellationToken.ThrowIfCancellationRequested();
						var p = await _projectionService.BuildOccupancyAsync(occupancy);
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

			return (count, callsComplete);
		}

		/// <summary>
		/// The calendar years (of LoggedOn) the department has calls in, newest first, limited to the last
		/// <see cref="SearchConfig.CallRebuildYears"/> when that is set. The years query returns whatever numeric type the
		/// dialect's YEAR/extract yields, as text.
		/// </summary>
		private async Task<List<int>> CallRebuildYearsAsync(int departmentId)
		{
			var years = new HashSet<int>();
			foreach (var value in await _calls.GetCallYearsByDeptartmentAsync(departmentId) ?? new List<string>())
			{
				if (decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var year) && year >= 1 && year <= 9999)
					years.Add((int)year);
			}

			var ordered = years.OrderByDescending(y => y).ToList();
			if (SearchConfig.CallRebuildYears > 0)
			{
				var oldest = DateTime.UtcNow.Year - SearchConfig.CallRebuildYears + 1;
				ordered = ordered.Where(y => y >= oldest).ToList();
			}

			return ordered;
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
