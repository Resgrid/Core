using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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

		private readonly IGlobalSearchService _global;
		private readonly Lazy<IInvoicingService> _invoicing;
		private readonly Lazy<IBidsService> _bids;
		private readonly Lazy<IServiceContractService> _contracts;
		private readonly Lazy<IDeploymentService> _deploymentsService;
		private readonly Lazy<ICertificationService> _certifications;
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
			Lazy<IDeploymentService> deployments = null, Lazy<ICertificationService> certifications = null)
		{
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

			var types = AllowedTypes(request.EntityTypes, principal);
			var skip = Math.Max(0, request.Skip);
			var take = Math.Max(1, Math.Min(100, request.Take));
			var needed = Math.Min(skip, CandidateWindow) + take;
			var dropped = 0;
			var authorized = new List<UnifiedSearchHit>();
			var windowCoveredAll = true;

			if (types.Count > 0)
			{
				if (!_global.IsAvailable)
				{
					windowCoveredAll = false;
					result.Degraded = true;
					result.DegradedReason = "The search index is not available yet.";
					await EnsureStateAsync(principal.DepartmentId, cancellationToken);
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
							IncludeAdminOnly = principal.IsDepartmentAdmin,
							Prefix = request.Prefix,
							Skip = 0,
							Take = CandidateWindow
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
						await EnsureStateAsync(principal.DepartmentId, cancellationToken);
					}
					else
					{
						windowCoveredAll = !indexResult.Truncated && indexResult.Hits.Count == indexResult.Total;
						var projections = (await _projections.GetByIdsAsync(principal.DepartmentId,
							indexResult.Hits.Where(h => h != null && !string.IsNullOrWhiteSpace(h.ProjectionId)).Select(h => h.ProjectionId))
							?? Enumerable.Empty<SearchProjection>()).ToDictionary(p => p.SearchProjectionId, StringComparer.Ordinal);
						foreach (var hit in indexResult.Hits)
						{
							cancellationToken.ThrowIfCancellationRequested();
							// An incomplete window can never yield an authorized total; stop once the page is filled.
							if (!windowCoveredAll && authorized.Count >= needed)
								break;
							if (hit != null && types.Contains(hit.EntityType) &&
								projections.TryGetValue(hit.ProjectionId ?? string.Empty, out var projection) &&
								ProjectionIsCurrent(hit, projection, access) && await AuthorizeAsync(hit, access))
								authorized.Add(Map(projection, hit.Score));
							else
								dropped++;
						}
					}
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
					(recordHits, recordsTotal) = await FederateRecordsAsync(text, access, Math.Min(Math.Min(skip, CandidateWindow) + take, CandidateWindow), cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Records federation failed; returning the other families.");
					recordsTotal = null;
				}
			}

			// One sequence, index hits then the records federation, paged as a whole: special-casing the first page
			// dropped the Records family from every later page.
			result.Hits = authorized.Concat(recordHits).Skip(skip).Take(take).ToList();
			// A raw index truncation flag also discloses unauthorized matches. Only expose authorized metadata.
			result.Truncated = false;

			// Totals only when they can be proven from authorized results (plan 2026-08-15 correction).
			if (dropped == 0 && windowCoveredAll && recordsTotal.HasValue)
				result.Total = authorized.Count + recordsTotal.Value;
			else
				result.Total = null;

			return Finish(result, watch);
		}

		private static UnifiedSearchResult Finish(UnifiedSearchResult result, Stopwatch watch)
		{
			result.QueryTimeMs = (int)watch.ElapsedMilliseconds;
			return result;
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
			Add(SearchEntityTypes.Deployment, "Deployments");
			Add(SearchEntityTypes.CertificationType, "Certifications");
			return allowed;
		}

		private static UnifiedSearchHit Map(SearchProjection hit, float score)
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
				Metadata = metadata
			};
		}

		private async Task<(List<UnifiedSearchHit> hits, int? total)> FederateRecordsAsync(string text, SearchAccess access, int window, CancellationToken cancellationToken)
		{
			var principal = access.Principal;
			var hits = new List<UnifiedSearchHit>();
			if (!principal.IsDepartmentAdmin && !principal.HasResourceClaim("Record", "View"))
				return (hits, 0);
			if (_recordsSearch == null || !_recordsSearch.IsAvailable)
				return (hits, 0);

			var module = await _recordsCutover.GetModuleStateAsync(principal.DepartmentId);
			if (module == null || !module.FlagEnabled || !module.Activated)
				return (hits, 0);
			if (!await _recordsAuthorization.IsActiveMemberAsync(principal.UserId, principal.DepartmentId))
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

		private async Task EnsureStateAsync(int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				var existing = await _states.GetAsync(SearchIndexNames.Global, departmentId);
				if (existing != null)
					return;
				var now = DateTime.UtcNow;
				await _states.SaveOrUpdateAsync(new SearchIndexState
				{
					IndexName = SearchIndexNames.Global,
					DepartmentId = departmentId,
					SchemaVersion = GlobalSearchGeneration.SchemaVersion,
					Generation = GlobalSearchGeneration.Compute(0, 0),
					State = (int)SearchIndexBuildState.RebuildRequested,
					RebuildRequestedOn = now,
					CreatedOn = now,
					ModifiedOn = now
				}, cancellationToken, true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not create the search index state row for department {departmentId}.");
			}
		}

		private async Task<bool> FlagOnAsync(int departmentId)
		{
			try { return await _featureToggles.IsEnabledAsync(FeatureFlagKeys.SearchUnified, departmentId); }
			catch (Exception ex) { Logging.LogException(ex); return false; }
		}
	}
}
