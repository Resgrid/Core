using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Search;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Writes the safe projection row for an entity (Unified Search plan R2.3). Called from the owning service's save
	/// and delete paths; every method swallows and logs its own failures so a projection problem never fails the
	/// entity write. Rebuilds live on <see cref="ISearchIndexMaintenanceService"/> because they need the entity
	/// services, which would otherwise form a constructor cycle with the services that call this one.
	/// </summary>
	public interface ISearchProjectionService
	{
		Task ProjectCallAsync(Call call, CancellationToken cancellationToken = default);

		Task ProjectUnitAsync(Unit unit, CancellationToken cancellationToken = default);

		/// <summary>Null groupId / isActive keep the values already on the projection row (the profile save path knows neither).</summary>
		Task ProjectPersonnelAsync(int departmentId, UserProfile profile, int? groupId, bool? isActive, CancellationToken cancellationToken = default);

		Task ProjectContactAsync(Contact contact, CancellationToken cancellationToken = default);

		Task ProjectMessageAsync(Message message, CancellationToken cancellationToken = default);

		Task ProjectDocumentAsync(Document document, CancellationToken cancellationToken = default);

		Task ProjectNoteAsync(Note note, CancellationToken cancellationToken = default);

		// Workforce & Business Operations families (decision 41): identifier / title / status only; a deleted row removes the projection.
		Task ProjectInvoiceAsync(Invoicing.Invoice invoice, CancellationToken cancellationToken = default);
		Task ProjectRateCardAsync(Invoicing.RateCard rateCard, CancellationToken cancellationToken = default);
		Task ProjectBidAsync(Invoicing.Bid bid, CancellationToken cancellationToken = default);
		Task ProjectServiceContractAsync(Invoicing.ServiceContract contract, CancellationToken cancellationToken = default);
		Task ProjectDeploymentAsync(Invoicing.Deployment deployment, CancellationToken cancellationToken = default);
		Task ProjectCertificationTypeAsync(DepartmentCertificationType type, CancellationToken cancellationToken = default);

		// Operations reference families (plan R3 Tier 2).
		Task ProjectProtocolAsync(DispatchProtocol protocol, CancellationToken cancellationToken = default);
		Task ProjectTrainingAsync(Training training, CancellationToken cancellationToken = default);
		Task ProjectCalendarItemAsync(CalendarItem item, CancellationToken cancellationToken = default);
		Task ProjectLogAsync(Log log, CancellationToken cancellationToken = default);
		Task ProjectPoiAsync(Poi poi, CancellationToken cancellationToken = default);
		Task ProjectShiftAsync(Shift shift, CancellationToken cancellationToken = default);
		Task ProjectGroupAsync(DepartmentGroup group, CancellationToken cancellationToken = default);
		Task ProjectOccupancyAsync(RmsOccupancy occupancy, CancellationToken cancellationToken = default);

		/// <summary>
		/// Re-reads the entity and rewrites its projection, for writes that change a projected child or related row without
		/// passing the entity itself: custom field values, role and group membership, member identification numbers, contact
		/// notes. Supported for Call, Unit, Personnel, Contact and Occupancy; never throws.
		/// </summary>
		Task RefreshAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default);

		/// <summary>A group was renamed or moved: re-project its members and its units, whose projections carry the group name.</summary>
		Task RefreshGroupDependentsAsync(int departmentId, int departmentGroupId, CancellationToken cancellationToken = default);

		Task RemoveAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default);

		/// <summary>Builds the projection row without saving it (used by rebuilds and tests). Null when nothing safe can be indexed.</summary>
		Task<SearchProjection> BuildCallAsync(Call call);
		Task<SearchProjection> BuildUnitAsync(Unit unit);
		Task<SearchProjection> BuildPersonnelAsync(int departmentId, UserProfile profile, int? groupId, bool isActive);
		Task<SearchProjection> BuildContactAsync(Contact contact);
		Task<SearchProjection> BuildMessageAsync(Message message);
		Task<SearchProjection> BuildDocumentAsync(Document document);
		Task<SearchProjection> BuildNoteAsync(Note note);
		Task<SearchProjection> BuildInvoiceAsync(Invoicing.Invoice invoice);
		Task<SearchProjection> BuildRateCardAsync(Invoicing.RateCard rateCard);
		Task<SearchProjection> BuildBidAsync(Invoicing.Bid bid);
		Task<SearchProjection> BuildServiceContractAsync(Invoicing.ServiceContract contract);
		Task<SearchProjection> BuildDeploymentAsync(Invoicing.Deployment deployment);
		Task<SearchProjection> BuildCertificationTypeAsync(DepartmentCertificationType type);
		Task<SearchProjection> BuildProtocolAsync(DispatchProtocol protocol);
		Task<SearchProjection> BuildTrainingAsync(Training training);
		/// <summary>Null for an occurrence of a recurring series: the series parent carries the event.</summary>
		Task<SearchProjection> BuildCalendarItemAsync(CalendarItem item);
		Task<SearchProjection> BuildLogAsync(Log log);
		/// <param name="type">The POI's type; POIs carry their department only through it.</param>
		Task<SearchProjection> BuildPoiAsync(Poi poi, PoiType type);
		Task<SearchProjection> BuildShiftAsync(Shift shift);
		Task<SearchProjection> BuildGroupAsync(DepartmentGroup group);
		/// <summary>Null for a removed or merged occupancy.</summary>
		Task<SearchProjection> BuildOccupancyAsync(RmsOccupancy occupancy);

		/// <summary>Upserts a prebuilt row (rebuild path).</summary>
		Task<SearchProjection> UpsertAsync(SearchProjection projection, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Worker command 70: keeps the global index in step with SearchProjections for every department that has a state
	/// row. Generation change, missing index or an admin request rebuilds the department (projections first, then the
	/// index); otherwise rows modified since the last sweep are re-indexed and soft-deleted rows removed.
	/// </summary>
	public interface ISearchIndexMaintenanceService
	{
		Task<SearchIndexSweepResult> SweepAsync(CancellationToken cancellationToken = default);

		/// <summary>Forces a full projection + index rebuild for one department regardless of its generation key.</summary>
		Task<SearchIndexSweepResult> RebuildDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Creates the department's state row (if missing) and flags it for rebuild; the next sweep picks it up.</summary>
		Task<SearchIndexState> RequestRebuildAsync(int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Write side of the global index. Only the worker process holds the IndexWriter; publish happens on commit.</summary>
	public interface IGlobalSearchIndexer
	{
		Task<int> IndexAsync(IEnumerable<SearchProjection> projections, string generation, CancellationToken cancellationToken = default);

		Task DeleteAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default);

		Task DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Commits and, when the object store is enabled, publishes under the database lease.</summary>
		Task CommitAsync(CancellationToken cancellationToken = default);

		/// <summary>Expunges deleted documents, commits, publishes and prunes superseded objects (erasure proof).</summary>
		Task ExpungeDeletesAsync(CancellationToken cancellationToken = default);

		Task<int> CountDocumentsAsync(int departmentId);

		/// <summary>
		/// The RowVersion each of these projections currently carries in the index, keyed by projection id; rows that are
		/// not indexed are absent. The catch-up sweep uses it to skip rows its overlap window re-reads unchanged.
		/// </summary>
		Task<IDictionary<string, long>> GetIndexedRowVersionsAsync(int departmentId, IEnumerable<SearchProjection> projections);

		/// <summary>True when a local index exists for this process (after a pull or a write).</summary>
		bool IndexExists { get; }
	}

	public class GlobalSearchQuery
	{
		/// <summary>Current department policy generation, supplied by the authorized orchestrator.</summary>
		public string Generation { get; set; }
		public string Text { get; set; }
		public List<string> EntityTypes { get; set; }
		public string ViewerUserId { get; set; }
		/// <summary>
		/// Families the index returns only when <see cref="ViewerUserId"/> is the projection's owner or a participant, in
		/// addition to Message (always viewer-scoped). The orchestrator names the families whose per-hit rule is
		/// membership for this caller — deployments for a member without the Deployments/View claim — so rows the caller
		/// can never see do not consume the candidate window or suppress the total.
		/// </summary>
		public List<string> ViewerScopedEntityTypes { get; set; }
		public bool IncludeAdminOnly { get; set; }
		public bool Prefix { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
		/// <summary>Only documents that occurred at or after this instant (UTC).</summary>
		public DateTime? FromUtc { get; set; }
		/// <summary>Only documents that occurred at or before this instant (UTC).</summary>
		public DateTime? ToUtc { get; set; }
		/// <summary>One of <see cref="SearchSortOrders"/>; relevance when null. A query without text is always newest first.</summary>
		public string Sort { get; set; }
		/// <summary>Deepest hit (skip + take) this query may reach; 0 = SearchConfig.MaxResults. Capped by SearchConfig.MaxPageWindow.</summary>
		public int MaxWindow { get; set; }
	}

	public class GlobalSearchHit
	{
		public int DepartmentId { get; set; }
		public string Generation { get; set; }
		public long RowVersion { get; set; }
		public string ProjectionId { get; set; }
		public string EntityType { get; set; }
		public string EntityId { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Url { get; set; }
		public string Category { get; set; }
		public string Status { get; set; }
		public long OccurredOnTicks { get; set; }
		public string MetadataJson { get; set; }
		public float Score { get; set; }
	}

	public class GlobalSearchResult
	{
		public List<GlobalSearchHit> Hits { get; set; } = new List<GlobalSearchHit>();
		public int Total { get; set; }
		public bool Truncated { get; set; }
		public bool Available { get; set; } = true;
	}

	/// <summary>Read side of the global index. The department clause is injected by the caller from authenticated state.</summary>
	public interface IGlobalSearchService
	{
		bool IsAvailable { get; }

		Task<GlobalSearchResult> SearchAsync(int departmentId, GlobalSearchQuery query, CancellationToken cancellationToken = default);

		Task<SearchIndexHealth> GetHealthAsync();
	}

	/// <summary>
	/// The unified endpoint behind the web command palette and the v4 API: global index hits re-checked per entity,
	/// Records federated from the RMS index, and system functionality from the action catalog (plan R3, R4 Phase 2).
	/// </summary>
	public interface IUnifiedSearchService
	{
		Task<UnifiedSearchResult> SearchAsync(UnifiedSearchRequest request, SearchPrincipal principal, CancellationToken cancellationToken = default);

		/// <summary>
		/// The entity families (<see cref="SearchEntityTypes"/>, Record included) this caller may search right now: flag,
		/// membership, view claims and module switches applied. Empty when search is unavailable to the caller.
		/// </summary>
		Task<List<string>> GetSearchableEntityTypesAsync(SearchPrincipal principal, CancellationToken cancellationToken = default);
	}

	/// <summary>Searches the static system-functionality catalog for one caller.</summary>
	public interface ISystemActionsService
	{
		Task<List<SystemActionHit>> SearchAsync(string text, SearchPrincipal principal, int max = 8, CancellationToken cancellationToken = default);

		/// <summary>Every entry the caller may use, unscored (the empty-query command palette).</summary>
		Task<List<SystemActionHit>> ListAsync(SearchPrincipal principal, CancellationToken cancellationToken = default);
	}
}
