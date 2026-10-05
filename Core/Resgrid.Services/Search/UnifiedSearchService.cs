using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
	/// The unified endpoint (plan R4 Phase 2). Checks current membership, module settings and policy, then queries
	/// the department's current index generation. Each candidate must match its live projection and pass current
	/// entity ownership and visibility checks. Records are federated with the same department and policy boundary.
	/// Totals are returned only when every candidate was checked and authorized. A department
	/// that has never searched gets a state row on its first query so worker 70 starts indexing it (lazy activation).
	/// </summary>
	public partial class UnifiedSearchService : IUnifiedSearchService
	{
		private const int CandidateWindow = 200;

		/// <summary>How long this process trusts that a department's state row exists and is Ready before reading it again.</summary>
		private static readonly TimeSpan ReadyMemo = TimeSpan.FromMinutes(5);

		/// <summary>
		/// Departments seen with a Ready state row at a generation, and when. Typeahead sends a query per keystroke; without the
		/// memo every one would read the state row. Only Ready is remembered, so a department still building is re-read until
		/// it is done, and a new generation (schema bump, protection change) is never answered from the memo.
		/// </summary>
		private static readonly ConcurrentDictionary<int, (DateTime Seen, string Generation)> ReadyDepartments = new ConcurrentDictionary<int, (DateTime Seen, string Generation)>();

		private readonly IGlobalSearchService _global;
		private readonly Lazy<IInvoicingService> _invoicing;
		private readonly Lazy<IBidsService> _bids;
		private readonly Lazy<IServiceContractService> _contracts;
		private readonly Lazy<IDeploymentService> _deploymentsService;
		private readonly Lazy<ICertificationService> _certifications;
		private readonly Lazy<ITrainingService> _trainings;
		private readonly Lazy<ICalendarService> _calendar;
		private readonly Lazy<IWorkLogsService> _logs;
		private readonly Lazy<IMappingService> _mapping;
		private readonly Lazy<IShiftsService> _shifts;
		private readonly Lazy<Records.RecordsPreventionGate> _preventionGate;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly ISystemActionsService _actions;
		private readonly IFeatureToggleService _featureToggles;
		private readonly IAuthorizationService _authorization;
		private readonly ISearchIndexStatesRepository _states;
		private readonly IRecordsSearchService _recordsSearch;
		private readonly IRecordsAuthorizationService _recordsAuthorization;
		private readonly IRecordsService _records;
		private readonly IRecordsCutoverService _recordsCutover;

		public UnifiedSearchService(IGlobalSearchService global, ISystemActionsService actions, IFeatureToggleService featureToggles,
			IAuthorizationService authorization, ISearchIndexStatesRepository states, IRecordsSearchService recordsSearch,
			IRecordsAuthorizationService recordsAuthorization, IRecordsService records, IRecordsCutoverService recordsCutover,
			IDepartmentsService departments, IPermissionsService permissions, IDepartmentGroupsService groups,
			IPersonnelRolesService roles, ICallsService calls, IUnitsService units, IMessageService messages,
			IDocumentsService documents, INotesService notes, IContactsService contacts,
			IDepartmentDataProtectionService dataProtection, IDepartmentSettingsService departmentSettings,
			ISearchProjectionsRepository projections,
			Lazy<IInvoicingService> invoicing = null, Lazy<IBidsService> bids = null, Lazy<IServiceContractService> contracts = null,
			Lazy<IDeploymentService> deployments = null, Lazy<ICertificationService> certifications = null,
			Lazy<ITrainingService> trainings = null, Lazy<ICalendarService> calendar = null, Lazy<IWorkLogsService> logs = null,
			Lazy<IMappingService> mapping = null, Lazy<IShiftsService> shifts = null,
			Lazy<Records.RecordsPreventionGate> preventionGate = null, IRmsOccupanciesRepository occupancies = null,
			ICallLocationKeysRepository callLocationKeys = null)
		{
			_preventionGate = preventionGate;
			_occupancies = occupancies;
			_callLocationKeys = callLocationKeys;
			_trainings = trainings;
			_calendar = calendar;
			_logs = logs;
			_mapping = mapping;
			_shifts = shifts;
			_invoicing = invoicing;
			_bids = bids;
			_contracts = contracts;
			_deploymentsService = deployments;
			_certifications = certifications;
			_global = global;
			_actions = actions;
			_featureToggles = featureToggles;
			_authorization = authorization;
			_states = states;
			_recordsSearch = recordsSearch;
			_recordsAuthorization = recordsAuthorization;
			_records = records;
			_recordsCutover = recordsCutover;
			_departments = departments;
			_permissions = permissions;
			_groups = groups;
			_roles = roles;
			_calls = calls;
			_units = units;
			_messages = messages;
			_documents = documents;
			_notes = notes;
			_contacts = contacts;
			_dataProtection = dataProtection;
			_departmentSettings = departmentSettings;
			_projections = projections;
		}

		public async Task<UnifiedSearchResult> SearchAsync(UnifiedSearchRequest request, SearchPrincipal principal, CancellationToken cancellationToken = default)
		{
			var watch = Stopwatch.StartNew();
			request = request ?? new UnifiedSearchRequest();
			var result = new UnifiedSearchResult();

			if (principal == null || principal.DepartmentId <= 0 || string.IsNullOrWhiteSpace(principal.UserId))
			{
				result.Available = false;
				return Finish(result, watch);
			}

			if (!await FlagOnAsync(principal.DepartmentId))
			{
				result.Available = false;
				result.DegradedReason = "Search.Unified is off for this department.";
				return Finish(result, watch);
			}

			var access = await LoadAccessAsync(principal);
			if (access == null)
			{
				result.Available = false;
				return Finish(result, watch);
			}
			principal = access.Principal;

			var text = (request.Text ?? string.Empty).Trim();
			if (text.Length > 500)
				text = text.Substring(0, 500);

			if (request.IncludeActions)
			{
				try
				{
					result.Actions = text.Length == 0
						? await _actions.ListAsync(principal, cancellationToken)
						: await _actions.SearchAsync(text, principal, 8, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "System action search failed.");
				}
			}

			if (text.Length == 0)
			{
				result.Total = 0;
				return Finish(result, watch);
			}

			var types = await WithoutClosedModulesAsync(AllowedTypes(request.EntityTypes, principal), access);
			var window = request.MaxCandidates > CandidateWindow
				? Math.Min(request.MaxCandidates, Math.Max(CandidateWindow, SearchConfig.MaxPageWindow))
				: CandidateWindow;
			var skip = Math.Max(0, request.Skip);
			// A page is at most 100 hits; an export (MaxCandidates past the default window) may take the whole window at once.
			var take = Math.Max(1, Math.Min(request.MaxCandidates > CandidateWindow ? window : 100, request.Take));
			var needed = Math.Min(skip, window) + take;
			var sort = SearchSortOrders.Normalize(request.Sort);
			var snippetTerms = request.Prefix ? null : SearchSnippets.Terms(text);
			var dropped = 0;
			var authorized = new List<UnifiedSearchHit>();
			var windowCoveredAll = true;
			var stoppedEarly = false;

			if (types.Count > 0)
			{
				// Activation runs on every search, not only while the index is missing: once any department had built the
				// shared index, a department that had never searched before never got a state row and was never indexed.
				result.IndexBuilding = !await EnsureStateAsync(principal.DepartmentId, access.GlobalGeneration, cancellationToken);

				if (!_global.IsAvailable)
				{
					windowCoveredAll = false;
					result.Degraded = true;
					result.DegradedReason = "The search index is not available yet.";
				}
				else
				{
					GlobalSearchResult indexResult;
					try
					{
						indexResult = await _global.SearchAsync(principal.DepartmentId, new GlobalSearchQuery
						{
							Generation = access.GlobalGeneration,
							Text = text,
							EntityTypes = types,
							ViewerUserId = principal.UserId,
							ViewerScopedEntityTypes = ViewerScopedTypes(principal),
							IncludeAdminOnly = principal.IsDepartmentAdmin,
							Prefix = request.Prefix,
							FromUtc = request.FromUtc,
							ToUtc = request.ToUtc,
							Sort = sort,
							MaxWindow = window,
							Skip = 0,
							Take = window
						}, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, "Global search query failed.");
						indexResult = new GlobalSearchResult { Available = false };
					}

					if (!indexResult.Available)
					{
						windowCoveredAll = false;
						result.Degraded = true;
						result.DegradedReason = "The search index is not available yet.";
					}
					else
					{
						windowCoveredAll = !indexResult.Truncated && indexResult.Hits.Count == indexResult.Total;
						var projections = (await _projections.GetByIdsAsync(principal.DepartmentId,
							indexResult.Hits.Where(h => h != null && !string.IsNullOrWhiteSpace(h.ProjectionId)).Select(h => h.ProjectionId))
							?? Enumerable.Empty<SearchProjection>()).ToDictionary(p => p.SearchProjectionId, StringComparer.Ordinal);
						await PreloadDeploymentsAsync(indexResult.Hits, access, cancellationToken);
						foreach (var hit in indexResult.Hits)
						{
							cancellationToken.ThrowIfCancellationRequested();
							// An incomplete window can never yield an authorized total, and a caller that shows none (the command
							// palette) does not need one: stop once the page is filled instead of authorizing every candidate.
							if ((!windowCoveredAll || !request.CountTotal) && authorized.Count >= needed)
							{
								stoppedEarly = true;
								break;
							}
							if (hit != null && types.Contains(hit.EntityType) &&
								projections.TryGetValue(hit.ProjectionId ?? string.Empty, out var projection) &&
								ProjectionIsCurrent(hit, projection, access) && await AuthorizeAsync(hit, access))
								authorized.Add(Map(projection, hit.Score, snippetTerms));
							else
								dropped++;
						}
					}
				}
			}

			// Street address matching beside the index, which matches word by word: "110 Main Street" also finds the call at
			// "110 Main St" or "110 Main". Address hits lead the list; one the index also found is shown once.
			AddressMatches addresses = null;
			if (!request.Prefix && types.Count > 0)
			{
				try
				{
					addresses = await MatchAddressesAsync(text, types, access, request, window, needed, snippetTerms, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// The query text stays out of the log: an address can name a protected location.
					Logging.LogException(ex, $"Street address matching failed for department {principal.DepartmentId}; returning the index hits only.");
					addresses = new AddressMatches { Complete = false };
				}

				if (addresses.Hits.Count > 0)
				{
					var shown = new HashSet<(string, string)>(addresses.Hits.Select(h => (h.EntityType, h.EntityId)));
					authorized = addresses.Hits.Concat(authorized.Where(h => !shown.Contains((h.EntityType, h.EntityId)))).ToList();
				}
			}

			// The records federation must reach as deep as the requested page can: the page is cut from index hits followed
			// by record hits, so a fixed top-N of records would leave every page past N empty for a records-heavy query.
			var recordHits = new List<UnifiedSearchHit>();
			int? recordsTotal = 0;
			if (request.IncludeRecords && !request.Prefix && WantsType(request.EntityTypes, SearchEntityTypes.Record))
			{
				try
				{
					(recordHits, recordsTotal) = await FederateRecordsAsync(text, access, Math.Min(Math.Min(skip, window) + take, window), cancellationToken);
					if (request.FromUtc.HasValue || request.ToUtc.HasValue)
					{
						// Records carry no date filter of their own; the range applies to the authorized hits, so the total
						// stays a count of hits the caller may open.
						var inRange = recordHits.Where(h => InRange(h.OccurredOn, request.FromUtc, request.ToUtc)).ToList();
						if (recordsTotal.HasValue)
							recordsTotal = inRange.Count;
						recordHits = inRange;
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Records federation failed; returning the other families.");
					recordsTotal = null;
				}
			}

			// One sequence, index hits then the records federation, paged as a whole: special-casing the first page
			// dropped the Records family from every later page. A date ordering spans both families.
			var combined = authorized.Concat(recordHits);
			if (sort == SearchSortOrders.Newest)
				combined = combined.OrderByDescending(h => h.OccurredOn ?? DateTime.MinValue);
			else if (sort == SearchSortOrders.Oldest)
				combined = combined.OrderBy(h => h.OccurredOn ?? DateTime.MaxValue);
			result.Hits = combined.Skip(skip).Take(take).ToList();
			// A raw index truncation flag also discloses unauthorized matches. Only expose authorized metadata.
			result.Truncated = false;

			// Totals only when they can be proven from authorized results (plan 2026-08-15 correction).
			if (dropped == 0 && windowCoveredAll && !stoppedEarly && recordsTotal.HasValue && (addresses == null || addresses.Complete))
				result.Total = authorized.Count + recordsTotal.Value;
			else
				result.Total = null;

			return Finish(result, watch);
		}

		public async Task<List<string>> GetSearchableEntityTypesAsync(SearchPrincipal principal, CancellationToken cancellationToken = default)
		{
			var types = new List<string>();
			if (principal == null || principal.DepartmentId <= 0 || string.IsNullOrWhiteSpace(principal.UserId))
				return types;
			if (!await FlagOnAsync(principal.DepartmentId))
				return types;

			var access = await LoadAccessAsync(principal);
			if (access == null)
				return types;

			types.AddRange(await WithoutClosedModulesAsync(AllowedTypes(null, access.Principal), access));
			try
			{
				if (await RecordsSearchableAsync(access.Principal))
					types.Add(SearchEntityTypes.Record);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Records search availability could not be determined.");
			}

			return types;
		}

		private static UnifiedSearchResult Finish(UnifiedSearchResult result, Stopwatch watch)
		{
			result.QueryTimeMs = (int)watch.ElapsedMilliseconds;
			return result;
		}

		/// <summary>Drops the families whose module is closed for the department in ways the claim set does not show (the Records occupancy module).</summary>
		private async Task<List<string>> WithoutClosedModulesAsync(List<string> types, SearchAccess access)
		{
			if (types.Contains(SearchEntityTypes.Occupancy) && !await OccupancyModuleOpenAsync(access))
				types.Remove(SearchEntityTypes.Occupancy);
			return types;
		}

		private static bool InRange(DateTime? value, DateTime? fromUtc, DateTime? toUtc)
		{
			if (!fromUtc.HasValue && !toUtc.HasValue)
				return true;
			if (!value.HasValue)
				return false;
			return (!fromUtc.HasValue || value.Value >= fromUtc.Value) && (!toUtc.HasValue || value.Value <= toUtc.Value);
		}

		private static bool WantsType(List<string> requested, string type)
		{
			return requested == null || requested.Count == 0 || requested.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Families the caller may search at all: requested ∩ claim-allowed ∩ module-enabled.</summary>
		private static List<string> AllowedTypes(List<string> requested, SearchPrincipal principal)
		{
			var allowed = new List<string>();
			void Add(string type, string resource, string module = null)
			{
				if (!WantsType(requested, type))
					return;
				if (!principal.IsDepartmentAdmin && !principal.HasResourceClaim(resource, "View"))
					return;
				if (!principal.ModuleEnabled(module))
					return;
				allowed.Add(type);
			}

			Add(SearchEntityTypes.Call, "Call");
			Add(SearchEntityTypes.Unit, "Unit");
			Add(SearchEntityTypes.Personnel, "Personnel");
			Add(SearchEntityTypes.Contact, "Contacts");
			Add(SearchEntityTypes.Message, "Messages", SystemActionModules.Messaging);
			Add(SearchEntityTypes.Document, "Documents", SystemActionModules.Documents);
			Add(SearchEntityTypes.Note, "Notes", SystemActionModules.Notes);
			// Workforce & Business Operations families (decision 41): the same claims as their pages; the paid ones behind the Business Ops module switch.
			Add(SearchEntityTypes.Invoice, "Invoicing", SystemActionModules.BusinessOperations);
			Add(SearchEntityTypes.RateCard, "Invoicing", SystemActionModules.BusinessOperations);
			Add(SearchEntityTypes.Bid, "Bids", SystemActionModules.BusinessOperations);
			Add(SearchEntityTypes.ServiceContract, "ServiceContracts", SystemActionModules.BusinessOperations);
			// Deployments have no family claim gate: the deployment page admits a rostered member without Deployments/View, so the
			// index must too, and AuthorizeAsync keeps the claim-or-admin-or-roster rule per hit (membership was verified in LoadAccessAsync).
			if (WantsType(requested, SearchEntityTypes.Deployment))
				allowed.Add(SearchEntityTypes.Deployment);
			// Certification types are the setup catalog: readers of records and ManageCertificationSetup holders both open them.
			if (WantsType(requested, SearchEntityTypes.CertificationType) &&
				(principal.IsDepartmentAdmin || principal.HasResourceClaim("Certifications", "View") || principal.HasResourceClaim("Certifications", "Setup")))
				allowed.Add(SearchEntityTypes.CertificationType);
			// Operations reference families (plan R3 Tier 2): the claim and module switch of each family's own page. POIs have
			// no claim of their own: the mapping pages admit every member.
			Add(SearchEntityTypes.Log, "Log", SystemActionModules.Logs);
			Add(SearchEntityTypes.Protocol, "Protocols");
			Add(SearchEntityTypes.Training, "Training", SystemActionModules.Training);
			Add(SearchEntityTypes.CalendarEvent, "Schedule", SystemActionModules.Calendar);
			Add(SearchEntityTypes.Shift, "Shift", SystemActionModules.Shifts);
			Add(SearchEntityTypes.Group, "GenericGroup");
			// Occupancies sit behind Record_View like their pages; the Records cutover and the module flag are checked by
			// WithoutClosedModulesAsync, which is asynchronous.
			Add(SearchEntityTypes.Occupancy, "Record");
			if (WantsType(requested, SearchEntityTypes.Poi) && principal.ModuleEnabled(SystemActionModules.Mapping))
				allowed.Add(SearchEntityTypes.Poi);
			return allowed;
		}

		/// <summary>
		/// Families the index must scope to the caller's own rows (owner or participant) on top of Message: deployments for a
		/// member without Deployments/View, whose rule is roster membership. Asking the index for the caller's deployments only
		/// keeps the unrostered ones — most of the department's, for a field member — out of the candidate window, so a
		/// rostered deployment past the window is still found and an authorized total stays provable.
		/// </summary>
		private static List<string> ViewerScopedTypes(SearchPrincipal principal) =>
			principal.IsDepartmentAdmin || principal.HasResourceClaim("Deployments", "View")
				? null
				: new List<string> { SearchEntityTypes.Deployment };

		private static UnifiedSearchHit Map(SearchProjection hit, float score, IReadOnlyList<string> snippetTerms = null)
		{
			IDictionary<string, string> metadata = new Dictionary<string, string>();
			if (!string.IsNullOrWhiteSpace(hit.MetadataJson))
			{
				try { metadata = JsonConvert.DeserializeObject<Dictionary<string, string>>(hit.MetadataJson) ?? metadata; }
				catch { /* stored by us; a parse failure only loses badges */ }
			}

			return new UnifiedSearchHit
			{
				EntityType = hit.EntityType,
				EntityId = hit.EntityId,
				Title = hit.Title,
				Summary = hit.Summary,
				Url = hit.Url,
				Score = score,
				OccurredOn = hit.OccurredOn,
				Category = hit.Category,
				Status = hit.Status,
				Snippet = snippetTerms == null || snippetTerms.Count == 0 ? null
					: SearchSnippets.Build(hit.SearchText, snippetTerms) ?? SearchSnippets.Build(hit.Summary, snippetTerms),
				Metadata = metadata
			};
		}

		private async Task<(List<UnifiedSearchHit> hits, int? total)> FederateRecordsAsync(string text, SearchAccess access, int window, CancellationToken cancellationToken)
		{
			var principal = access.Principal;
			var hits = new List<UnifiedSearchHit>();
			if (!await RecordsSearchableAsync(principal))
				return (hits, 0);

			List<int> visibleGroups = null;
			if (await _recordsAuthorization.IsGroupScopedAsync(principal.DepartmentId))
				visibleGroups = await _recordsAuthorization.GetVisibleGroupIdsAsync(principal.UserId, principal.DepartmentId) ?? new List<int>();

			var search = await _recordsSearch.SearchAsync(principal.DepartmentId, new RecordsSearchRequest
			{
				Generation = access.RecordsGeneration,
				IncludeNarrative = access.ProtectedTextAllowed &&
					(await _departmentSettings.GetRecordsSearchConfigAsync(principal.DepartmentId, true))?.IndexNarrative == true,
				Text = text,
				VisibleGroupIds = visibleGroups,
				ViewerUserId = principal.UserId,
				Take = Math.Max(1, window)
			}, cancellationToken);
			if (search == null || !search.Available)
				return (hits, 0);

			var recordSource = ((int)RmsSearchSourceType.Record).ToString();
			var ids = search.Hits.Where(h => h.SourceType == recordSource && !string.IsNullOrWhiteSpace(h.SourceId)).Select(h => h.SourceId).Distinct().ToList();
			var loaded = (await _records.GetProjectionsByIdsAsync(principal.DepartmentId, ids) ?? new List<RmsRecordSearchProjection>())
				.ToDictionary(p => p.RmsRecordSearchProjectionId, StringComparer.OrdinalIgnoreCase);

			// Only record-source hits were loaded above, so only those can be judged: any other source type reaching
			// here is not a dropped hit, and counting it as one would null the totals for every query in the department.
			var dropped = 0;
			foreach (var hit in search.Hits.Where(h => h.SourceType == recordSource))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (hit.DepartmentId != principal.DepartmentId || hit.Generation != access.RecordsGeneration ||
					!loaded.TryGetValue(hit.SourceId ?? string.Empty, out var projection) ||
					projection.DepartmentId != principal.DepartmentId || projection.DeletedOn.HasValue ||
					projection.SourceType != (int)RmsSearchSourceType.Record || projection.SourceId != hit.SourceId ||
					projection.PolicyEpoch != access.PolicyEpoch || projection.ProtectedCatalogVersion != access.CatalogVersion ||
					!await _recordsAuthorization.CanUserViewRecordAsync(principal.UserId, hit.SourceId, principal.DepartmentId))
				{
					dropped++;
					continue;
				}

				hits.Add(new UnifiedSearchHit
				{
					EntityType = SearchEntityTypes.Record,
					EntityId = projection.RmsRecordSearchProjectionId,
					Title = string.IsNullOrWhiteSpace(projection.RecordNumber) ? (projection.DraftReference ?? projection.DisplaySummary ?? "Record") : projection.RecordNumber,
					Summary = projection.DisplaySummary,
					Url = $"/User/Records/Edit?id={Uri.EscapeDataString(projection.RmsRecordSearchProjectionId)}",
					Score = hit.Score,
					OccurredOn = projection.OccurredOn ?? projection.RecordCreatedOn,
					Category = projection.DefinitionKey,
					Status = projection.State.ToString(),
					Metadata = new Dictionary<string, string>
					{
						["DefinitionKey"] = projection.DefinitionKey ?? string.Empty,
						["State"] = projection.State.ToString(),
						["CallId"] = projection.CallId?.ToString() ?? string.Empty
					}
				});
			}

			return (hits, dropped == 0 && !search.Truncated && search.Hits.Count == search.Total ? hits.Count : (int?)null);
		}

		/// <summary>The caller may search Records: the view claim (or admin), an activated Records module, current membership and a live records index.</summary>
		private async Task<bool> RecordsSearchableAsync(SearchPrincipal principal)
		{
			if (!principal.IsDepartmentAdmin && !principal.HasResourceClaim("Record", "View"))
				return false;
			if (_recordsSearch == null || !_recordsSearch.IsAvailable)
				return false;

			var module = await _recordsCutover.GetModuleStateAsync(principal.DepartmentId);
			if (module == null || !module.FlagEnabled || !module.Activated)
				return false;
			return await _recordsAuthorization.IsActiveMemberAsync(principal.UserId, principal.DepartmentId);
		}

		/// <summary>
		/// Makes sure worker 70 sweeps the department: creates its state row on first use (lazy activation). Returns true when
		/// the department's index is built (Ready) at the current generation; false while it is queued, rebuilding, failed,
		/// just created, or still built at an older generation (the query filters on the current one, so it finds nothing yet).
		/// </summary>
		private async Task<bool> EnsureStateAsync(int departmentId, string generation, CancellationToken cancellationToken)
		{
			if (ReadyDepartments.TryGetValue(departmentId, out var seen) && DateTime.UtcNow - seen.Seen < ReadyMemo &&
				string.Equals(seen.Generation, generation, StringComparison.Ordinal))
				return true;

			try
			{
				var existing = await _states.GetAsync(SearchIndexNames.Global, departmentId);
				if (existing != null)
				{
					var ready = existing.State == (int)SearchIndexBuildState.Ready && !existing.RebuildRequestedOn.HasValue &&
						(string.IsNullOrEmpty(existing.Generation) || string.Equals(existing.Generation, generation, StringComparison.Ordinal));
					if (ready)
						ReadyDepartments[departmentId] = (DateTime.UtcNow, generation);
					else
						ReadyDepartments.TryRemove(departmentId, out _);
					return ready;
				}
				var now = DateTime.UtcNow;
				// A department's first searches arrive together (typeahead sends one per keystroke) and all see no row
				// above, so the create has to be conditional or every request but one fails on the unique index.
				await _states.InsertIfMissingAsync(new SearchIndexState
				{
					IndexName = SearchIndexNames.Global,
					DepartmentId = departmentId,
					SchemaVersion = GlobalSearchGeneration.SchemaVersion,
					Generation = GlobalSearchGeneration.Compute(0, 0),
					State = (int)SearchIndexBuildState.RebuildRequested,
					RebuildRequestedOn = now,
					CreatedOn = now,
					ModifiedOn = now
				}, cancellationToken);
				return false;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not create the search index state row for department {departmentId}.");
				return false;
			}
		}

		/// <summary>Test seam: forget every department this process has seen Ready.</summary>
		public static void ResetReadyMemo() => ReadyDepartments.Clear();

		private async Task<bool> FlagOnAsync(int departmentId)
		{
			try { return await _featureToggles.IsEnabledAsync(FeatureFlagKeys.SearchUnified, departmentId); }
			catch (Exception ex) { Logging.LogException(ex); return false; }
		}
	}
}
