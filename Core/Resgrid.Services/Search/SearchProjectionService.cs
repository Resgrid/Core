using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
	/// Builds and stores the safe search projection for each Tier 1 entity (plan R3). The allowlist is decided here, per
	/// family, against the protected-field catalog: a column the catalog protects (call name/nature/address/incident
	/// number/notes and call note text, every contact field, document name/description/filename, message subject/body,
	/// member identification number) is projected only when Advanced Data Protection is not enforced for the department
	/// (R2.15, the RMS narrative precedent). A value carrying an envelope prefix or the redaction placeholder is dropped regardless.
	/// Enrollment bumps the generation key, and the rebuild that follows re-projects without those columns.
	/// </summary>
	public class SearchProjectionService : ISearchProjectionService
	{
		private const int TitleMax = 400;
		private const int SummaryMax = 1000;
		private const int KeywordsMax = 400;
		private const int SearchTextMax = 8000;
		/// <summary>Calls carry their dispatch notes and every call note in the full text, so they get more room than other families.</summary>
		private const int CallSearchTextMax = 32000;

		private static readonly Regex HtmlTags = new Regex("<[^>]+>", RegexOptions.Compiled);
		private static readonly Regex Whitespace = new Regex("\\s+", RegexOptions.Compiled);

		private readonly ISearchProjectionsRepository _projections;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDeploymentPersonnelRepository _deploymentPersonnel;
		private readonly ICallNotesRepository _callNotes;
		private readonly IAddressRepository _addresses;
		private readonly IContactNotesRepository _contactNotes;
		private readonly IContactCategoryRepository _contactCategories;
		private readonly IPersonnelRolesRepository _personnelRoles;
		private readonly IDepartmentGroupMembersRepository _groupMembers;
		private readonly IDepartmentGroupsRepository _groups;
		private readonly IDepartmentMemberSensitiveDataRepository _memberSensitiveData;
		private readonly IUdfDefinitionRepository _udfDefinitions;
		private readonly IUdfFieldRepository _udfFields;
		private readonly IUdfFieldValueRepository _udfValues;
		private readonly ICallsRepository _calls;
		private readonly IUnitsRepository _units;
		private readonly IContactsRepository _contacts;
		private readonly IUserProfilesRepository _profiles;
		private readonly IDepartmentMembersRepository _members;
		private readonly IPoiTypesRepository _poiTypes;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IRmsOccupancyHazardsRepository _occupancyHazards;

		// One scope serves one request or one worker sweep; these keep a department rebuild from re-reading the same group,
		// category or custom-field definition for every row.
		private readonly ConcurrentDictionary<int, DepartmentGroup> _groupCache = new ConcurrentDictionary<int, DepartmentGroup>();
		private readonly ConcurrentDictionary<string, string> _categoryCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<(int, int), List<UdfField>> _udfFieldCache = new ConcurrentDictionary<(int, int), List<UdfField>>();
		private readonly ConcurrentDictionary<(int, int), string> _udfDefinitionCache = new ConcurrentDictionary<(int, int), string>();

		/// <param name="deploymentPersonnel">Roster rows for the deployment projection's participants; optional only for hosts without the Business Operations repositories.</param>
		/// <param name="callNotes">The call's note rows for its full text; without it only the notes already loaded on the call are projected.</param>
		/// <remarks>
		/// The remaining repositories enrich a projection with related rows (addresses, notes, group and role names, custom
		/// field values, member identification numbers) and back <see cref="RefreshAsync"/>. Each is optional: a host without
		/// one projects the entity's own columns only. Repositories, not services, so the services that call the hooks never
		/// form a construction cycle with this one.
		/// </remarks>
		public SearchProjectionService(ISearchProjectionsRepository projections, IDepartmentDataProtectionService dataProtection,
			IDeploymentPersonnelRepository deploymentPersonnel = null, ICallNotesRepository callNotes = null,
			IAddressRepository addresses = null, IContactNotesRepository contactNotes = null, IContactCategoryRepository contactCategories = null,
			IPersonnelRolesRepository personnelRoles = null, IDepartmentGroupMembersRepository groupMembers = null, IDepartmentGroupsRepository groups = null,
			IDepartmentMemberSensitiveDataRepository memberSensitiveData = null, IUdfDefinitionRepository udfDefinitions = null,
			IUdfFieldRepository udfFields = null, IUdfFieldValueRepository udfValues = null, ICallsRepository calls = null,
			IUnitsRepository units = null, IContactsRepository contacts = null, IUserProfilesRepository profiles = null,
			IDepartmentMembersRepository members = null, IPoiTypesRepository poiTypes = null,
			IRmsOccupanciesRepository occupancies = null, IRmsOccupancyHazardsRepository occupancyHazards = null)
		{
			_occupancies = occupancies;
			_occupancyHazards = occupancyHazards;
			_projections = projections ?? throw new ArgumentNullException(nameof(projections));
			_dataProtection = dataProtection ?? throw new ArgumentNullException(nameof(dataProtection));
			_deploymentPersonnel = deploymentPersonnel;
			_callNotes = callNotes;
			_addresses = addresses;
			_contactNotes = contactNotes;
			_contactCategories = contactCategories;
			_personnelRoles = personnelRoles;
			_groupMembers = groupMembers;
			_groups = groups;
			_memberSensitiveData = memberSensitiveData;
			_udfDefinitions = udfDefinitions;
			_udfFields = udfFields;
			_udfValues = udfValues;
			_calls = calls;
			_units = units;
			_contacts = contacts;
			_profiles = profiles;
			_members = members;
			_poiTypes = poiTypes;
		}

		// ---- hooks -----------------------------------------------------------------------------------------------

		public Task ProjectCallAsync(Call call, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Call, call?.DepartmentId ?? 0, call?.CallId.ToString(), call != null && call.IsDeleted, () => BuildCallAsync(call), cancellationToken);

		public Task ProjectUnitAsync(Unit unit, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Unit, unit?.DepartmentId ?? 0, unit?.UnitId.ToString(), unit != null && unit.IsDeleted, () => BuildUnitAsync(unit), cancellationToken);

		public Task ProjectPersonnelAsync(int departmentId, UserProfile profile, int? groupId, bool? isActive, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Personnel, departmentId, profile?.UserId, false, async () =>
			{
				if (!groupId.HasValue || !isActive.HasValue)
				{
					var existing = await _projections.GetAsync(departmentId, SearchEntityTypes.Personnel, profile.UserId);
					groupId = groupId ?? existing?.GroupId;
					isActive = isActive ?? existing?.IsActive ?? true;
				}
				return await BuildPersonnelAsync(departmentId, profile, groupId, isActive.Value);
			}, cancellationToken);

		public Task ProjectContactAsync(Contact contact, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Contact, contact?.DepartmentId ?? 0, contact?.ContactId, contact != null && contact.IsDeleted, () => BuildContactAsync(contact), cancellationToken);

		public Task ProjectMessageAsync(Message message, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Message, message?.DepartmentId ?? 0, message?.MessageId.ToString(), message != null && message.IsDeleted, () => BuildMessageAsync(message), cancellationToken);

		public Task ProjectDocumentAsync(Document document, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Document, document?.DepartmentId ?? 0, document?.DocumentId.ToString(), false, () => BuildDocumentAsync(document), cancellationToken);

		public Task ProjectNoteAsync(Note note, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Note, note?.DepartmentId ?? 0, note?.NoteId.ToString(), false, () => BuildNoteAsync(note), cancellationToken);

		public Task ProjectProtocolAsync(DispatchProtocol protocol, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Protocol, protocol?.DepartmentId ?? 0, protocol?.DispatchProtocolId.ToString(), false, () => BuildProtocolAsync(protocol), cancellationToken);

		public Task ProjectTrainingAsync(Training training, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Training, training?.DepartmentId ?? 0, training?.TrainingId.ToString(), false, () => BuildTrainingAsync(training), cancellationToken);

		public Task ProjectCalendarItemAsync(CalendarItem item, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.CalendarEvent, item?.DepartmentId ?? 0, item?.CalendarItemId.ToString(), false, () => BuildCalendarItemAsync(item), cancellationToken);

		public Task ProjectLogAsync(Log log, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Log, log?.DepartmentId ?? 0, log?.LogId.ToString(), false, () => BuildLogAsync(log), cancellationToken);

		public async Task ProjectPoiAsync(Poi poi, CancellationToken cancellationToken = default)
		{
			if (poi == null || poi.PoiId <= 0)
				return;
			PoiType type = poi.Type;
			try
			{
				if (type == null || type.PoiTypeId != poi.PoiTypeId)
					type = _poiTypes != null ? await _poiTypes.GetPoiTypeByTypeIdAsync(poi.PoiTypeId) : null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Search projection could not resolve the type of POI {poi.PoiId}.");
				return;
			}
			await Guarded(SearchEntityTypes.Poi, type?.DepartmentId ?? 0, poi.PoiId.ToString(), false, () => BuildPoiAsync(poi, type), cancellationToken);
		}

		public Task ProjectShiftAsync(Shift shift, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Shift, shift?.DepartmentId ?? 0, shift?.ShiftId.ToString(), false, () => BuildShiftAsync(shift), cancellationToken);

		public Task ProjectGroupAsync(DepartmentGroup group, CancellationToken cancellationToken = default)
		{
			if (group != null)
				_groupCache.TryRemove(group.DepartmentGroupId, out _);
			return Guarded(SearchEntityTypes.Group, group?.DepartmentId ?? 0, group?.DepartmentGroupId.ToString(), false, () => BuildGroupAsync(group), cancellationToken);
		}

		public Task ProjectOccupancyAsync(RmsOccupancy occupancy, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Occupancy, occupancy?.DepartmentId ?? 0, occupancy?.RmsOccupancyId,
				occupancy != null && (occupancy.DeletedOn.HasValue || occupancy.Status == (int)RmsOccupancyStatus.Merged), () => BuildOccupancyAsync(occupancy), cancellationToken);

		public async Task RefreshAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
				return;
			try
			{
				switch (entityType)
				{
					case SearchEntityTypes.Call:
						if (_calls == null || !int.TryParse(entityId, out var callId)) return;
						var call = await _calls.GetByIdAsync(callId);
						if (call != null && call.DepartmentId == departmentId)
							await ProjectCallAsync(call, cancellationToken);
						return;
					case SearchEntityTypes.Unit:
						if (_units == null || !int.TryParse(entityId, out var unitId)) return;
						var unit = await _units.GetByIdAsync(unitId);
						if (unit != null && unit.DepartmentId == departmentId)
							await ProjectUnitAsync(unit, cancellationToken);
						return;
					case SearchEntityTypes.Contact:
						if (_contacts == null) return;
						var contact = await _contacts.GetByIdAsync(entityId);
						if (contact != null && contact.DepartmentId == departmentId)
							await ProjectContactAsync(contact, cancellationToken);
						return;
					case SearchEntityTypes.Personnel:
						await RefreshPersonnelAsync(departmentId, entityId, cancellationToken);
						return;
					case SearchEntityTypes.Occupancy:
						if (_occupancies == null) return;
						var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, entityId);
						if (occupancy == null)
							await RemoveAsync(departmentId, SearchEntityTypes.Occupancy, entityId, cancellationToken);
						else if (occupancy.DepartmentId == departmentId)
							await ProjectOccupancyAsync(occupancy, cancellationToken);
						return;
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Never fail the caller's write over the search projection; the next rebuild reconciles.
				Logging.LogException(ex, $"Search projection refresh failed for {entityType} {entityId} in department {departmentId}.");
			}
		}

		/// <summary>Same membership rule as the rebuild: deleted, disabled and hidden members are off the personnel list and out of the index.</summary>
		private async Task RefreshPersonnelAsync(int departmentId, string userId, CancellationToken cancellationToken)
		{
			if (_profiles == null || _members == null)
				return;
			var member = await _members.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userId);
			if (member == null || member.IsDeleted || member.IsDisabled.GetValueOrDefault() || member.IsHidden.GetValueOrDefault())
			{
				await RemoveAsync(departmentId, SearchEntityTypes.Personnel, userId, cancellationToken);
				return;
			}
			var profile = await _profiles.GetProfileByUserIdAsync(userId);
			if (profile == null)
				return;
			int? groupId = null;
			if (_groupMembers != null)
				groupId = (await _groupMembers.GetAllGroupMembersByUserAndDepartmentAsync(userId, departmentId) ?? Enumerable.Empty<DepartmentGroupMember>())
					.FirstOrDefault(m => m != null && m.DepartmentId == departmentId)?.DepartmentGroupId;
			await Guarded(SearchEntityTypes.Personnel, departmentId, userId, false, () => BuildPersonnelAsync(departmentId, profile, groupId, member.IsActive), cancellationToken);
		}

		public async Task RefreshGroupDependentsAsync(int departmentId, int departmentGroupId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || departmentGroupId <= 0)
				return;
			_groupCache.TryRemove(departmentGroupId, out _);
			try
			{
				if (_groupMembers != null)
					foreach (var member in await _groupMembers.GetAllGroupMembersByGroupIdAsync(departmentGroupId) ?? Enumerable.Empty<DepartmentGroupMember>())
						if (member != null && member.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(member.UserId))
							await RefreshPersonnelAsync(departmentId, member.UserId, cancellationToken);
				if (_units != null)
					foreach (var unit in await _units.GetAllUnitsByGroupIdAsync(departmentGroupId) ?? Enumerable.Empty<Unit>())
						if (unit != null && unit.DepartmentId == departmentId)
							await ProjectUnitAsync(unit, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Search projection refresh failed for the members and units of group {departmentGroupId} in department {departmentId}.");
			}
		}

		public async Task RemoveAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
				return;
			try { await _projections.SoftDeleteAsync(departmentId, entityType, entityId, cancellationToken); }
			catch (Exception ex) { Logging.LogException(ex, $"Search projection removal failed for {entityType} {entityId} in department {departmentId}."); }
		}

		public async Task<SearchProjection> UpsertAsync(SearchProjection projection, CancellationToken cancellationToken = default)
		{
			if (projection == null)
				return null;
			return await _projections.UpsertAsync(projection, cancellationToken);
		}

		private async Task Guarded(string entityType, int departmentId, string entityId, bool deleted, Func<Task<SearchProjection>> build, CancellationToken cancellationToken)
		{
			try
			{
				if (departmentId <= 0 || string.IsNullOrWhiteSpace(entityId) || entityId == "0")
					return;
				if (deleted)
				{
					await _projections.SoftDeleteAsync(departmentId, entityType, entityId, cancellationToken);
					return;
				}
				var projection = await build();
				if (projection == null)
					await _projections.SoftDeleteAsync(departmentId, entityType, entityId, cancellationToken);
				else
					await _projections.UpsertAsync(projection, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Never fail the entity write over the search projection; the next rebuild reconciles.
				Logging.LogException(ex, $"Search projection failed for {entityType} {entityId} in department {departmentId}.");
			}
		}

		public Task ProjectInvoiceAsync(Invoice invoice, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Invoice, invoice?.DepartmentId ?? 0, invoice?.InvoiceId, invoice != null && invoice.IsDeleted, () => BuildInvoiceAsync(invoice), cancellationToken);

		public Task ProjectRateCardAsync(RateCard rateCard, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.RateCard, rateCard?.DepartmentId ?? 0, rateCard?.RateCardId, rateCard != null && rateCard.IsDeleted, () => BuildRateCardAsync(rateCard), cancellationToken);

		public Task ProjectBidAsync(Bid bid, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Bid, bid?.DepartmentId ?? 0, bid?.BidId, bid != null && bid.IsDeleted, () => BuildBidAsync(bid), cancellationToken);

		public Task ProjectServiceContractAsync(ServiceContract contract, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.ServiceContract, contract?.DepartmentId ?? 0, contract?.ServiceContractId, contract != null && contract.IsDeleted, () => BuildServiceContractAsync(contract), cancellationToken);

		public Task ProjectDeploymentAsync(Deployment deployment, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Deployment, deployment?.DepartmentId ?? 0, deployment?.DeploymentId, deployment != null && deployment.IsDeleted, () => BuildDeploymentAsync(deployment), cancellationToken);

		public Task ProjectCertificationTypeAsync(DepartmentCertificationType type, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.CertificationType, type?.DepartmentId ?? 0, type?.DepartmentCertificationTypeId.ToString(), type != null && type.IsDeleted, () => BuildCertificationTypeAsync(type), cancellationToken);

		// ---- builders --------------------------------------------------------------------------------------------

		public async Task<SearchProjection> BuildCallAsync(Call call)
		{
			if (call == null || call.DepartmentId <= 0 || call.CallId <= 0 || call.IsDeleted)
				return null;

			var ctx = await ContextAsync(call.DepartmentId);
			var number = Safe(call.Number);
			var name = ctx.ProtectedTextAllowed ? Safe(call.Name) : null;
			var nature = ctx.ProtectedTextAllowed ? Safe(call.NatureOfCall) : null;
			var type = ctx.ProtectedTextAllowed ? Safe(call.Type) : null;
			var address = ctx.ProtectedTextAllowed ? Safe(call.Address) : null;
			var incident = ctx.ProtectedTextAllowed ? Safe(call.IncidentNumber) : null;
			var reference = ctx.ProtectedTextAllowed ? Safe(call.ReferenceNumber) : null;
			var external = ctx.ProtectedTextAllowed ? Safe(call.ExternalIdentifier) : null;
			// Calls.Notes, Calls.CompletedNotes and CallNotes.Note are PHI in the catalog: projected under the same rule as the
			// nature and address. They are what lets a department find every call whose notes mention a given alarm point.
			var notes = ctx.ProtectedTextAllowed ? Strip(Safe(call.Notes)) : null;
			var completedNotes = ctx.ProtectedTextAllowed ? Strip(Safe(call.CompletedNotes)) : null;
			var callNotes = ctx.ProtectedTextAllowed ? await CallNotesTextAsync(call) : null;
			// The caller (Calls.ContactName / ContactNumber, PII) answers "every call this alarm company placed"; the
			// department's custom call fields (UdfFieldValues.Value) carry account numbers and the like. Same rule.
			var callerName = ctx.ProtectedTextAllowed ? Safe(call.ContactName) : null;
			var callerNumber = ctx.ProtectedTextAllowed ? Safe(call.ContactNumber) : null;
			var custom = ctx.ProtectedTextAllowed ? await CustomFieldsTextAsync(call.DepartmentId, UdfEntityType.Call, call.CallId.ToString()) : null;
			var state = Enum.IsDefined(typeof(CallStates), call.State) ? ((CallStates)call.State).ToString() : call.State.ToString();

			var p = New(call.DepartmentId, SearchEntityTypes.Call, call.CallId.ToString(), ctx);
			p.Title = Cap(name ?? (number != null ? "Call " + number : "Call " + call.CallId), TitleMax);
			p.Summary = Cap(Join(" · ", nature, type), SummaryMax);
			p.Keywords = Cap(Join(" ", number, incident, reference, external, ctx.ProtectedTextAllowed ? Digits(call.ContactNumber) : null), KeywordsMax);
			p.SearchText = Cap(Join(" ", address, nature, type, callerName, callerNumber, notes, completedNotes, callNotes, custom), CallSearchTextMax);
			p.Category = type;
			p.Status = state;
			p.Priority = call.Priority;
			p.IsActive = call.State == (int)CallStates.Active;
			p.OccurredOn = call.LoggedOn == default ? DateTime.UtcNow : call.LoggedOn;
			p.OwnerUserId = Safe(call.ReportingUserId);
			p.Url = $"/User/Dispatch/ViewCall?callId={call.CallId}";
			p.MetadataJson = Json(new Dictionary<string, string>
			{
				["Number"] = number,
				["Priority"] = call.Priority.ToString(),
				["State"] = state,
				["LoggedOn"] = p.OccurredOn.ToString("o"),
				["IncidentNumber"] = incident
			});
			return p;
		}

		/// <summary>The text of the call's live (not deleted) notes, oldest first. Read from the repository when available: a call
		/// handed to a save path can carry only the notes that request posted.</summary>
		private async Task<string> CallNotesTextAsync(Call call)
		{
			IEnumerable<CallNote> rows = null;
			if (_callNotes != null && call.CallId > 0)
				rows = await _callNotes.GetCallNotesByCallIdAsync(call.CallId);
			rows ??= call.CallNotes;

			var texts = (rows ?? Enumerable.Empty<CallNote>())
				.Where(n => n != null && !n.IsDeleted)
				.OrderBy(n => n.Timestamp)
				.Select(n => Strip(Safe(n.Note)))
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.ToList();
			return texts.Count == 0 ? null : string.Join(" ", texts);
		}

		public async Task<SearchProjection> BuildUnitAsync(Unit unit)
		{
			if (unit == null || unit.DepartmentId <= 0 || unit.UnitId <= 0)
				return null;

			var ctx = await ContextAsync(unit.DepartmentId);
			var name = Safe(unit.Name);
			if (name == null)
				return null;

			// The station name lets "Station 4" find the station's apparatus; custom unit fields are cataloged values.
			var station = Safe((unit.StationGroup != null && unit.StationGroup.DepartmentGroupId == unit.StationGroupId ? unit.StationGroup : await GroupAsync(unit.StationGroupId))?.Name);
			var custom = ctx.ProtectedTextAllowed ? await CustomFieldsTextAsync(unit.DepartmentId, UdfEntityType.Unit, unit.UnitId.ToString()) : null;

			var p = New(unit.DepartmentId, SearchEntityTypes.Unit, unit.UnitId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", Safe(unit.Type), station), SummaryMax);
			p.Keywords = Cap(Join(" ", name, Safe(unit.VIN), Safe(unit.PlateNumber)), KeywordsMax);
			p.SearchText = Cap(Join(" ", station, custom), SearchTextMax);
			p.Category = Safe(unit.Type);
			p.GroupId = unit.StationGroupId;
			p.IsActive = true;
			p.OccurredOn = DateTime.UtcNow;
			// The page every unit viewer may open; UnifiedSearchService.LinkAsync sends callers who may edit the unit to EditUnit.
			p.Url = $"/User/Units/ViewEvents?unitId={unit.UnitId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Type"] = Safe(unit.Type), ["StationGroupId"] = unit.StationGroupId?.ToString(), ["Station"] = station });
			return p;
		}

		public async Task<SearchProjection> BuildPersonnelAsync(int departmentId, UserProfile profile, int? groupId, bool isActive)
		{
			if (profile == null || departmentId <= 0 || string.IsNullOrWhiteSpace(profile.UserId))
				return null;

			var ctx = await ContextAsync(departmentId);
			var first = Safe(profile.FirstName);
			var last = Safe(profile.LastName);
			var name = Join(" ", first, last);
			if (name == null)
				return null;

			// Member identification numbers are cataloged and live per department on DepartmentMemberSensitiveData; the
			// legacy profile column is not mapped any more and only a host without that repository falls back to it.
			// Phones, e-mail and addresses are never projected (plan R3).
			var idNumber = ctx.ProtectedTextAllowed ? Safe(await IdentificationNumberAsync(departmentId, profile)) : null;
			// Group and role names are plain configuration: "engineer", "captain" or "Station 2" finds the people.
			var group = Safe((await GroupAsync(groupId))?.Name);
			var roles = await RoleNamesAsync(departmentId, profile.UserId);
			var custom = ctx.ProtectedTextAllowed ? await CustomFieldsTextAsync(departmentId, UdfEntityType.Personnel, profile.UserId) : null;

			var p = New(departmentId, SearchEntityTypes.Personnel, profile.UserId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", group, roles), SummaryMax);
			p.Keywords = Cap(Join(" ", idNumber, first, last), KeywordsMax);
			p.SearchText = Cap(Join(" ", group, roles, custom), SearchTextMax);
			p.GroupId = groupId;
			p.OwnerUserId = profile.UserId;
			p.IsActive = isActive;
			p.OccurredOn = profile.LastUpdated ?? DateTime.UtcNow;
			p.Url = $"/User/Personnel/ViewPerson?userId={Uri.EscapeDataString(profile.UserId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["IdentificationNumber"] = idNumber, ["GroupId"] = groupId?.ToString(), ["Group"] = group, ["IsActive"] = isActive ? "true" : "false" });
			return p;
		}

		public async Task<SearchProjection> BuildContactAsync(Contact contact)
		{
			if (contact == null || contact.DepartmentId <= 0 || string.IsNullOrWhiteSpace(contact.ContactId) || contact.IsDeleted)
				return null;

			var ctx = await ContextAsync(contact.DepartmentId);
			// Every Contact column is cataloged: in an enforced department nothing textual can be indexed.
			if (!ctx.ProtectedTextAllowed)
				return null;

			var first = Safe(contact.FirstName);
			var middle = Safe(contact.MiddleName);
			var last = Safe(contact.LastName);
			var company = Safe(contact.CompanyName);
			var other = Safe(contact.OtherName);
			var title = Join(" ", first, last) ?? company ?? other;
			if (title == null)
				return null;

			// Addresses (Addresses rows behind PhysicalAddressId / MailingAddressId), the contact's live notes
			// (ContactNotes.Note, cataloged) and its category name, so "123 Main" or a note about the alarm panel finds it.
			var physical = await AddressTextAsync(contact.PhysicalAddressId);
			var mailing = await AddressTextAsync(contact.MailingAddressId);
			var notes = await ContactNotesTextAsync(contact);
			var category = await CategoryNameAsync(contact.DepartmentId, contact.ContactCategoryId);
			var custom = await CustomFieldsTextAsync(contact.DepartmentId, UdfEntityType.Contact, contact.ContactId);

			var p = New(contact.DepartmentId, SearchEntityTypes.Contact, contact.ContactId, ctx);
			p.Title = Cap(title, TitleMax);
			p.Summary = Cap(Join(" · ", company != null && title != company ? company : null, physical, Safe(contact.Description)), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(contact.Email), Digits(contact.CellPhoneNumber), Digits(contact.HomePhoneNumber), Digits(contact.OfficePhoneNumber), Digits(contact.FaxPhoneNumber)), KeywordsMax);
			p.SearchText = Cap(Join(" ", middle, other, company, category, physical, mailing, Safe(contact.Email), Safe(contact.OtherInfo), Safe(contact.Website), notes, custom), SearchTextMax);
			p.Category = contact.ContactType == 1 ? "Company" : "Person";
			p.IsActive = true;
			p.OccurredOn = DateTime.UtcNow;
			p.Url = $"/User/Contacts/View?contactId={Uri.EscapeDataString(contact.ContactId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["ContactType"] = contact.ContactType.ToString(), ["CategoryId"] = Safe(contact.ContactCategoryId), ["Category"] = category });
			return p;
		}

		public async Task<SearchProjection> BuildMessageAsync(Message message)
		{
			if (message == null || !message.DepartmentId.HasValue || message.DepartmentId.Value <= 0 || message.MessageId <= 0 || message.IsDeleted)
				return null;

			var ctx = await ContextAsync(message.DepartmentId.Value);
			var subject = ctx.ProtectedTextAllowed ? Safe(message.Subject) : null;
			var body = ctx.ProtectedTextAllowed ? Safe(Strip(message.Body)) : null;

			var recipients = new List<string>();
			if (!string.IsNullOrWhiteSpace(message.ReceivingUserId))
				recipients.Add(message.ReceivingUserId);
			if (message.MessageRecipients != null)
				recipients.AddRange(message.MessageRecipients.Where(r => r != null && !r.IsDeleted && !string.IsNullOrWhiteSpace(r.UserId)).Select(r => r.UserId));

			var p = New(message.DepartmentId.Value, SearchEntityTypes.Message, message.MessageId.ToString(), ctx);
			p.Title = Cap(subject ?? "Message", TitleMax);
			p.Summary = Cap(body == null ? null : body.Substring(0, Math.Min(body.Length, 200)), SummaryMax);
			p.SearchText = Cap(body, SearchTextMax);
			p.Category = message.Type.ToString();
			p.OwnerUserId = Safe(message.SendingUserId);
			p.ParticipantUserIds = recipients.Count == 0 ? null : string.Join(",", recipients.Distinct());
			p.IsActive = !message.ExpireOn.HasValue || message.ExpireOn.Value > DateTime.UtcNow;
			p.OccurredOn = message.SentOn == default ? DateTime.UtcNow : message.SentOn;
			p.Url = $"/User/Messages/ViewMessage?messageId={message.MessageId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Type"] = message.Type.ToString(), ["SentOn"] = p.OccurredOn.ToString("o"), ["IsBroadcast"] = message.IsBroadcast ? "true" : "false" });
			return p;
		}

		public async Task<SearchProjection> BuildDocumentAsync(Document document)
		{
			if (document == null || document.DepartmentId <= 0 || document.DocumentId <= 0)
				return null;

			var ctx = await ContextAsync(document.DepartmentId);
			var name = ctx.ProtectedTextAllowed ? Safe(document.Name) : null;
			var description = ctx.ProtectedTextAllowed ? Safe(document.Description) : null;
			var filename = ctx.ProtectedTextAllowed ? Safe(document.Filename) : null;

			var p = New(document.DepartmentId, SearchEntityTypes.Document, document.DocumentId.ToString(), ctx);
			p.Title = Cap(name ?? "Document " + document.DocumentId, TitleMax);
			p.Summary = Cap(description, SummaryMax);
			p.Keywords = Cap(filename, KeywordsMax);
			p.SearchText = Cap(Join(" ", filename, Safe(document.Category), Safe(document.Type)), SearchTextMax);
			p.Category = Safe(document.Category);
			p.IsAdminOnly = document.AdminsOnly;
			p.OwnerUserId = Safe(document.UserId);
			p.IsActive = !document.RemoveOn.HasValue || document.RemoveOn.Value > DateTime.UtcNow;
			p.OccurredOn = document.AddedOn == default ? DateTime.UtcNow : document.AddedOn;
			p.Url = $"/User/Documents/ViewDocument?documentId={document.DocumentId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Category"] = Safe(document.Category), ["Type"] = Safe(document.Type), ["AddedOn"] = p.OccurredOn.ToString("o") });
			return p;
		}

		public async Task<SearchProjection> BuildNoteAsync(Note note)
		{
			if (note == null || note.DepartmentId <= 0 || note.NoteId <= 0)
				return null;

			// Notes are not in the protected catalog; title and body are department-authored plain text.
			var ctx = await ContextAsync(note.DepartmentId);
			var title = Safe(note.Title);
			var body = Safe(Strip(note.Body));
			if (title == null && body == null)
				return null;

			var p = New(note.DepartmentId, SearchEntityTypes.Note, note.NoteId.ToString(), ctx);
			p.Title = Cap(title ?? "Note " + note.NoteId, TitleMax);
			p.Summary = Cap(body == null ? null : body.Substring(0, Math.Min(body.Length, 200)), SummaryMax);
			p.SearchText = Cap(body, SearchTextMax);
			p.Category = Safe(note.Category);
			p.IsAdminOnly = note.IsAdminOnly;
			p.OwnerUserId = Safe(note.UserId);
			p.IsActive = !note.ExpiresOn.HasValue || note.ExpiresOn.Value > DateTime.UtcNow;
			p.OccurredOn = note.AddedOn == default ? DateTime.UtcNow : note.AddedOn;
			p.Url = $"/User/Notes/View?noteId={note.NoteId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Category"] = Safe(note.Category), ["Color"] = Safe(note.Color), ["AddedOn"] = p.OccurredOn.ToString("o") });
			return p;
		}

		// ---- Workforce & Business Operations families (decision 41): identifier, title, status. Never an amount, a line, a note, an e-mail or a person. ----

		public async Task<SearchProjection> BuildInvoiceAsync(Invoice invoice)
		{
			if (invoice == null || invoice.DepartmentId <= 0 || string.IsNullOrWhiteSpace(invoice.InvoiceId) || invoice.IsDeleted)
				return null;
			var ctx = await ContextAsync(invoice.DepartmentId);
			var status = Enum.IsDefined(typeof(InvoiceStatus), invoice.Status) ? ((InvoiceStatus)invoice.Status).ToString() : invoice.Status.ToString();
			var p = New(invoice.DepartmentId, SearchEntityTypes.Invoice, invoice.InvoiceId, ctx);
			p.Title = Cap("Invoice #" + invoice.InvoiceNumber, TitleMax);
			p.Summary = Cap(Join(" · ", status, invoice.IssuedOn?.ToString("yyyy-MM-dd"), Safe(invoice.Currency)), SummaryMax);
			p.Keywords = Cap(Join(" ", invoice.InvoiceNumber.ToString(), Safe(invoice.DeploymentId), Safe(invoice.ServiceContractId)), KeywordsMax);
			p.Category = status;
			p.IsActive = invoice.Status != (int)InvoiceStatus.Void;
			p.OccurredOn = invoice.IssuedOn ?? (invoice.AddedOn == default ? DateTime.UtcNow : invoice.AddedOn);
			p.Url = $"/User/Invoicing/View?id={Uri.EscapeDataString(invoice.InvoiceId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["ContactId"] = Safe(invoice.ContactId), ["DeploymentId"] = Safe(invoice.DeploymentId) });
			return p;
		}

		public async Task<SearchProjection> BuildRateCardAsync(RateCard rateCard)
		{
			if (rateCard == null || rateCard.DepartmentId <= 0 || string.IsNullOrWhiteSpace(rateCard.RateCardId) || rateCard.IsDeleted)
				return null;
			var ctx = await ContextAsync(rateCard.DepartmentId);
			var name = Safe(rateCard.Name);
			if (name == null) return null;
			var p = New(rateCard.DepartmentId, SearchEntityTypes.RateCard, rateCard.RateCardId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", rateCard.IsDefault ? "Default" : null, rateCard.Active ? "Active" : "Inactive", Safe(rateCard.Description)), SummaryMax);
			p.Category = rateCard.Active ? "Active" : "Inactive";
			p.IsActive = rateCard.Active;
			p.OccurredOn = rateCard.AddedOn == default ? DateTime.UtcNow : rateCard.AddedOn;
			p.Url = $"/User/Invoicing/EditRateCard?id={Uri.EscapeDataString(rateCard.RateCardId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["IsDefault"] = rateCard.IsDefault.ToString() });
			return p;
		}

		public async Task<SearchProjection> BuildBidAsync(Bid bid)
		{
			if (bid == null || bid.DepartmentId <= 0 || string.IsNullOrWhiteSpace(bid.BidId) || bid.IsDeleted)
				return null;
			var ctx = await ContextAsync(bid.DepartmentId);
			var status = Enum.IsDefined(typeof(BidStatuses), bid.Status) ? ((BidStatuses)bid.Status).ToString() : bid.Status.ToString();
			var p = New(bid.DepartmentId, SearchEntityTypes.Bid, bid.BidId, ctx);
			p.Title = Cap(Join(" ", "Bid #" + bid.BidNumber, Safe(bid.Title)), TitleMax);
			p.Summary = Cap(Join(" · ", status, Safe(bid.IncidentNumber), bid.RequestedStartOn?.ToString("yyyy-MM-dd")), SummaryMax);
			p.Keywords = Cap(Join(" ", bid.BidNumber.ToString(), Safe(bid.IncidentNumber), Safe(bid.ConvertedDeploymentId)), KeywordsMax);
			p.Category = status;
			p.IsActive = bid.Status != (int)BidStatuses.Declined && bid.Status != (int)BidStatuses.Expired;
			p.OccurredOn = bid.AddedOn == default ? DateTime.UtcNow : bid.AddedOn;
			p.Url = $"/User/Bids/View?id={Uri.EscapeDataString(bid.BidId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["ContactId"] = Safe(bid.ContactId), ["DeploymentId"] = Safe(bid.ConvertedDeploymentId) });
			return p;
		}

		public async Task<SearchProjection> BuildServiceContractAsync(ServiceContract contract)
		{
			if (contract == null || contract.DepartmentId <= 0 || string.IsNullOrWhiteSpace(contract.ServiceContractId) || contract.IsDeleted)
				return null;
			var ctx = await ContextAsync(contract.DepartmentId);
			var name = Safe(contract.Name);
			if (name == null) return null;
			var status = Enum.IsDefined(typeof(ServiceContractStatuses), contract.Status) ? ((ServiceContractStatuses)contract.Status).ToString() : contract.Status.ToString();
			var p = New(contract.DepartmentId, SearchEntityTypes.ServiceContract, contract.ServiceContractId, ctx);
			p.Title = Cap(Join(" ", Safe(contract.ContractNumber), name), TitleMax);
			p.Summary = Cap(Join(" · ", status, contract.StartOn.ToString("yyyy-MM-dd") + " – " + (contract.EndOn?.ToString("yyyy-MM-dd") ?? "…")), SummaryMax);
			p.Keywords = Cap(Safe(contract.ContractNumber), KeywordsMax);
			p.Category = status;
			p.IsActive = contract.Status == (int)ServiceContractStatuses.Active;
			p.OccurredOn = contract.StartOn == default ? DateTime.UtcNow : contract.StartOn;
			p.Url = $"/User/Contracts/View?id={Uri.EscapeDataString(contract.ServiceContractId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["ContactId"] = Safe(contract.ContactId) });
			return p;
		}

		public async Task<SearchProjection> BuildDeploymentAsync(Deployment deployment)
		{
			if (deployment == null || deployment.DepartmentId <= 0 || string.IsNullOrWhiteSpace(deployment.DeploymentId) || deployment.IsDeleted)
				return null;
			var ctx = await ContextAsync(deployment.DepartmentId);
			var name = Safe(deployment.Name);
			if (name == null) return null;
			var status = Enum.IsDefined(typeof(DeploymentStatuses), deployment.Status) ? ((DeploymentStatuses)deployment.Status).ToString() : deployment.Status.ToString();
			// Deployments.Notes is ADP catalog 27 and never indexed; the order / request / incident identifiers print on every claim and are plain.
			var p = New(deployment.DepartmentId, SearchEntityTypes.Deployment, deployment.DeploymentId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", status, Safe(deployment.IncidentNumber), Safe(deployment.ResourceOrderNumber), deployment.StartOn?.ToString("yyyy-MM-dd")), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(deployment.IncidentNumber), Safe(deployment.ResourceOrderNumber), Safe(deployment.RequestNumber), Safe(deployment.ServiceRequestNumber),
				Safe(deployment.RmsExternalOrderId), Safe(deployment.CostCode), deployment.CallId?.ToString()), KeywordsMax);
			p.SearchText = Cap(Safe(deployment.PointOfHire), SearchTextMax);
			p.Category = status;
			p.IsActive = deployment.IsOpen;
			p.OccurredOn = deployment.StartOn ?? (deployment.AddedOn == default ? DateTime.UtcNow : deployment.AddedOn);
			p.Url = $"/User/Deployments/View?id={Uri.EscapeDataString(deployment.DeploymentId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["FinanceMode"] = deployment.FinanceMode.ToString(), ["CallId"] = deployment.CallId?.ToString() });
			// The roster is the deployment's participant set: a member without Deployments/View reaches a deployment they are
			// rostered on (any personnel row, removed or not — the deployment page's rule), and the index applies that scope for
			// such a caller so unrostered deployments never enter their candidate window. Read here rather than from
			// deployment.Personnel, which is only populated on the aggregate read paths.
			p.ParticipantUserIds = await RosterAsync(deployment.DeploymentId);
			return p;
		}

		private async Task<string> RosterAsync(string deploymentId)
		{
			if (_deploymentPersonnel == null) return null;
			var users = (await _deploymentPersonnel.GetByDeploymentAsync(deploymentId) ?? Enumerable.Empty<DeploymentPersonnel>())
				.Select(r => r?.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
			return users.Count == 0 ? null : string.Join(",", users);
		}

		public async Task<SearchProjection> BuildCertificationTypeAsync(DepartmentCertificationType type)
		{
			if (type == null || type.DepartmentId <= 0 || type.DepartmentCertificationTypeId <= 0 || type.IsDeleted)
				return null;
			var ctx = await ContextAsync(type.DepartmentId);
			var name = Safe(type.Type);
			if (name == null) return null;
			var p = New(type.DepartmentId, SearchEntityTypes.CertificationType, type.DepartmentCertificationTypeId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", Safe(type.Code), Safe(type.IssuingAuthority), Safe(type.Description)), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(type.Code), Safe(type.IssuingAuthority)), KeywordsMax);
			p.Category = type.IsActive ? "Active" : "Inactive";
			p.IsActive = type.IsActive;
			p.OccurredOn = DateTime.UtcNow;
			p.Url = $"/User/Certifications/EditType?id={type.DepartmentCertificationTypeId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Code"] = Safe(type.Code), ["AppliesTo"] = type.AppliesTo.ToString() });
			return p;
		}

		// ---- operations reference families (plan R3 Tier 2) --------------------------------------------------------

		public async Task<SearchProjection> BuildProtocolAsync(DispatchProtocol protocol)
		{
			if (protocol == null || protocol.DepartmentId <= 0 || protocol.DispatchProtocolId <= 0)
				return null;
			var name = Safe(protocol.Name);
			if (name == null)
				return null;

			var ctx = await ContextAsync(protocol.DepartmentId);
			var p = New(protocol.DepartmentId, SearchEntityTypes.Protocol, protocol.DispatchProtocolId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Strip(Safe(protocol.Description)), SummaryMax);
			p.Keywords = Cap(Safe(protocol.Code), KeywordsMax);
			p.SearchText = Cap(Join(" ", Strip(Safe(protocol.Description)), Strip(Safe(protocol.ProtocolText))), SearchTextMax);
			p.Category = Safe(protocol.Code);
			p.Status = protocol.IsDisabled ? "Disabled" : "Active";
			p.IsActive = !protocol.IsDisabled;
			p.OccurredOn = protocol.UpdatedOn ?? (protocol.CreatedOn == default ? DateTime.UtcNow : protocol.CreatedOn);
			p.Url = $"/User/Protocols/View?id={protocol.DispatchProtocolId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Code"] = Safe(protocol.Code) });
			return p;
		}

		public async Task<SearchProjection> BuildTrainingAsync(Training training)
		{
			if (training == null || training.DepartmentId <= 0 || training.TrainingId <= 0)
				return null;
			var name = Safe(training.Name);
			if (name == null)
				return null;

			var ctx = await ContextAsync(training.DepartmentId);
			var p = New(training.DepartmentId, SearchEntityTypes.Training, training.TrainingId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Strip(Safe(training.Description)), SummaryMax);
			p.SearchText = Cap(Join(" ", Strip(Safe(training.Description)), Strip(Safe(training.TrainingText))), SearchTextMax);
			p.Status = training.ToBeCompletedBy.HasValue ? "Due " + training.ToBeCompletedBy.Value.ToString("yyyy-MM-dd") : null;
			p.IsActive = !training.ToBeCompletedBy.HasValue || training.ToBeCompletedBy.Value >= DateTime.UtcNow.Date;
			p.OccurredOn = training.CreatedOn == default ? DateTime.UtcNow : training.CreatedOn;
			p.Url = $"/User/Trainings/View?trainingId={training.TrainingId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["ToBeCompletedBy"] = training.ToBeCompletedBy?.ToString("o") });
			return p;
		}

		public async Task<SearchProjection> BuildCalendarItemAsync(CalendarItem item)
		{
			if (item == null || item.DepartmentId <= 0 || item.CalendarItemId <= 0)
				return null;
			// One document per event: the series parent stands for its occurrences, which only repeat its text.
			if (!string.IsNullOrWhiteSpace(item.RecurrenceId))
				return null;

			// CalendarItems.Title / Description / Location are cataloged (catalog 9): projected under the call-text rule.
			var ctx = await ContextAsync(item.DepartmentId);
			if (!ctx.ProtectedTextAllowed)
				return null;
			var title = Safe(item.Title);
			if (title == null)
				return null;
			var location = Safe(item.Location);

			var p = New(item.DepartmentId, SearchEntityTypes.CalendarEvent, item.CalendarItemId.ToString(), ctx);
			p.Title = Cap(title, TitleMax);
			p.Summary = Cap(Join(" · ", location, item.IsAllDay ? item.Start.ToString("yyyy-MM-dd") : null), SummaryMax);
			p.SearchText = Cap(Join(" ", location, Strip(Safe(item.Description))), SearchTextMax);
			p.Category = item.RecurrenceType > 0 ? "Recurring" : null;
			p.IsActive = item.End >= DateTime.UtcNow || item.RecurrenceType > 0 && (!item.RecurrenceEnd.HasValue || item.RecurrenceEnd.Value >= DateTime.UtcNow);
			p.OccurredOn = item.Start == default ? DateTime.UtcNow : item.Start;
			p.OwnerUserId = Safe(item.CreatorUserId);
			p.Url = $"/User/Calendar/View?calendarItemId={item.CalendarItemId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Start"] = item.Start.ToString("o"), ["End"] = item.End.ToString("o"), ["AllDay"] = item.IsAllDay ? "true" : null });
			return p;
		}

		public async Task<SearchProjection> BuildLogAsync(Log log)
		{
			if (log == null || log.DepartmentId <= 0 || log.LogId <= 0)
				return null;

			// Logs.Narrative, InitialReport, Cause, ContactName/Number, OtherPersonnel, Location, BodyLocation and
			// PronouncedDeceasedBy are cataloged (catalog 3) and follow the call-text rule; the course, instructors, other
			// agencies/units and identifiers are plain.
			var ctx = await ContextAsync(log.DepartmentId);
			var allowed = ctx.ProtectedTextAllowed;
			var kind = log.LogType.HasValue && Enum.IsDefined(typeof(LogTypes), log.LogType.Value) ? ((LogTypes)log.LogType.Value).ToString() : null;
			var type = Safe(log.Type);
			var course = Safe(log.Course);
			var narrative = allowed ? Strip(Safe(log.Narrative)) : null;
			var initial = allowed ? Strip(Safe(log.InitialReport)) : null;
			var location = allowed ? Safe(log.Location) : null;

			var p = New(log.DepartmentId, SearchEntityTypes.Log, log.LogId.ToString(), ctx);
			p.Title = Cap(Join(" · ", kind ?? "Log", course ?? type) ?? "Log " + log.LogId, TitleMax);
			p.Summary = Cap(narrative == null ? location : narrative.Substring(0, Math.Min(narrative.Length, 200)), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(log.ExternalId), Safe(log.CourseCode)), KeywordsMax);
			p.SearchText = Cap(Join(" ", type, course, Safe(log.CourseCode), Safe(log.Instructors), Safe(log.OtherAgencies), Safe(log.OtherUnits),
				narrative, initial, location, allowed ? Strip(Safe(log.Cause)) : null, allowed ? Safe(log.ContactName) : null,
				allowed ? Safe(log.ContactNumber) : null, allowed ? Safe(log.OtherPersonnel) : null, allowed ? Safe(log.BodyLocation) : null,
				allowed ? Safe(log.PronouncedDeceasedBy) : null), CallSearchTextMax);
			p.Category = kind;
			p.GroupId = log.StationGroupId;
			p.IsActive = true;
			p.OccurredOn = log.StartedOn ?? (log.LoggedOn == default ? DateTime.UtcNow : log.LoggedOn);
			p.OwnerUserId = Safe(log.LoggedByUserId);
			p.Url = $"/User/Logs/View?logId={log.LogId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["LogType"] = kind, ["CallId"] = log.CallId?.ToString() });
			return p;
		}

		public async Task<SearchProjection> BuildPoiAsync(Poi poi, PoiType type)
		{
			if (poi == null || poi.PoiId <= 0 || type == null || type.DepartmentId <= 0 || type.PoiTypeId != poi.PoiTypeId)
				return null;
			var typeName = Safe(type.Name);
			var name = Safe(poi.Name);
			var address = Safe(poi.Address);
			if (name == null && address == null)
				return null;

			var ctx = await ContextAsync(type.DepartmentId);
			var p = New(type.DepartmentId, SearchEntityTypes.Poi, poi.PoiId.ToString(), ctx);
			p.Title = Cap(name ?? address, TitleMax);
			p.Summary = Cap(Join(" · ", typeName, name != null ? address : null), SummaryMax);
			p.SearchText = Cap(Join(" ", typeName, address, Strip(Safe(poi.Note))), SearchTextMax);
			p.Category = typeName;
			p.IsActive = true;
			p.OccurredOn = DateTime.UtcNow;
			p.Url = $"/User/Mapping/EditPOI?poiId={poi.PoiId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["PoiTypeId"] = type.PoiTypeId.ToString(), ["Type"] = typeName });
			return p;
		}

		public async Task<SearchProjection> BuildShiftAsync(Shift shift)
		{
			if (shift == null || shift.DepartmentId <= 0 || shift.ShiftId <= 0)
				return null;
			var name = Safe(shift.Name);
			if (name == null)
				return null;

			var ctx = await ContextAsync(shift.DepartmentId);
			var hours = Safe(shift.StartTime) != null || Safe(shift.EndTime) != null ? $"{Safe(shift.StartTime)}–{Safe(shift.EndTime)}" : null;
			var p = New(shift.DepartmentId, SearchEntityTypes.Shift, shift.ShiftId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", Safe(shift.Code), hours), SummaryMax);
			p.Keywords = Cap(Safe(shift.Code), KeywordsMax);
			p.IsActive = true;
			p.OccurredOn = shift.StartDay == default ? DateTime.UtcNow : shift.StartDay;
			p.Url = $"/User/Shifts/ShiftCalendar?shiftId={shift.ShiftId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Code"] = Safe(shift.Code) });
			return p;
		}

		public async Task<SearchProjection> BuildGroupAsync(DepartmentGroup group)
		{
			if (group == null || group.DepartmentId <= 0 || group.DepartmentGroupId <= 0)
				return null;
			var name = Safe(group.Name);
			if (name == null)
				return null;

			var ctx = await ContextAsync(group.DepartmentId);
			var isStation = group.Type == (int)DepartmentGroupTypes.Station;
			var address = group.Address != null ? AddressText(group.Address) : await AddressTextAsync(group.AddressId);
			var p = New(group.DepartmentId, SearchEntityTypes.Group, group.DepartmentGroupId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", isStation ? "Station" : "Group", address), SummaryMax);
			p.SearchText = Cap(address, SearchTextMax);
			p.Category = isStation ? "Station" : "Group";
			p.GroupId = group.DepartmentGroupId;
			p.IsActive = true;
			p.OccurredOn = DateTime.UtcNow;
			// Groups have no read-only page: the group's row on the list; LinkAsync sends callers who may edit it to EditGroup.
			p.Url = $"/User/Groups#group-{group.DepartmentGroupId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Type"] = isStation ? "Station" : "Group", ["ParentGroupId"] = group.ParentDepartmentGroupId?.ToString() });
			return p;
		}

		public async Task<SearchProjection> BuildOccupancyAsync(RmsOccupancy occupancy)
		{
			if (occupancy == null || occupancy.DepartmentId <= 0 || string.IsNullOrWhiteSpace(occupancy.RmsOccupancyId) || occupancy.DeletedOn.HasValue
				|| occupancy.Status == (int)RmsOccupancyStatus.Merged)
				return null;
			var name = Safe(occupancy.Name);
			if (name == null)
				return null;

			// Name, number, address and parcel are plain. The alarm company, alarm panel location, hazard notes and tactical
			// summary are cataloged (RMS prevention) and follow the protected-text rule. Gate codes, Knox box locations,
			// emergency contacts, access notes and occupants needing assistance never enter the shared index.
			var ctx = await ContextAsync(occupancy.DepartmentId);
			var allowed = ctx.ProtectedTextAllowed;
			var address = Join(" ", Safe(occupancy.AddressText), Safe(occupancy.City), Safe(occupancy.StateProvince), Safe(occupancy.PostalCode));
			string hazards = null;
			if (_occupancyHazards != null)
			{
				var titles = (await _occupancyHazards.GetForOccupancyAsync(occupancy.DepartmentId, occupancy.RmsOccupancyId) ?? Enumerable.Empty<RmsOccupancyHazard>())
					.Where(h => h != null && !h.DeletedOn.HasValue).Select(h => Safe(h.Title)).Where(t => t != null).ToList();
				hazards = titles.Count == 0 ? null : string.Join(" ", titles);
			}
			var status = Enum.IsDefined(typeof(RmsOccupancyStatus), occupancy.Status) ? ((RmsOccupancyStatus)occupancy.Status).ToString() : null;

			var p = New(occupancy.DepartmentId, SearchEntityTypes.Occupancy, occupancy.RmsOccupancyId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", address, allowed ? Safe(occupancy.AlarmCompany) : null), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(occupancy.OccupancyNumber), Safe(occupancy.ParcelId), allowed ? Digits(occupancy.AlarmCompanyPhone) : null), KeywordsMax);
			p.SearchText = Cap(Join(" ", address, hazards, allowed ? Safe(occupancy.AlarmCompany) : null, allowed ? Safe(occupancy.AlarmPanelLocation) : null,
				allowed ? Strip(Safe(occupancy.GeneralHazardNotes)) : null, allowed ? Strip(Safe(occupancy.TacticalSummary)) : null), SearchTextMax);
			p.Category = status;
			p.Status = status;
			p.IsActive = occupancy.Status == (int)RmsOccupancyStatus.Active;
			p.OccurredOn = occupancy.ModifiedOn == default ? (occupancy.CreatedOn == default ? DateTime.UtcNow : occupancy.CreatedOn) : occupancy.ModifiedOn;
			p.Url = $"/User/RecordOccupancies/Details?id={Uri.EscapeDataString(occupancy.RmsOccupancyId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Number"] = Safe(occupancy.OccupancyNumber), ["Status"] = status });
			return p;
		}

		// ---- enrichment ------------------------------------------------------------------------------------------

		private async Task<DepartmentGroup> GroupAsync(int? departmentGroupId)
		{
			if (!departmentGroupId.HasValue || departmentGroupId.Value <= 0 || _groups == null)
				return null;
			if (_groupCache.TryGetValue(departmentGroupId.Value, out var cached))
				return cached;
			var group = await _groups.GetGroupByGroupIdAsync(departmentGroupId.Value);
			_groupCache[departmentGroupId.Value] = group;
			return group;
		}

		private async Task<string> RoleNamesAsync(int departmentId, string userId)
		{
			if (_personnelRoles == null || string.IsNullOrWhiteSpace(userId))
				return null;
			var names = (await _personnelRoles.GetRolesForUserAsync(departmentId, userId) ?? Enumerable.Empty<PersonnelRole>())
				.Where(r => r != null && r.DepartmentId == departmentId)
				.Select(r => Safe(r.Name)).Where(n => n != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
			return names.Count == 0 ? null : string.Join(", ", names);
		}

		private async Task<string> IdentificationNumberAsync(int departmentId, UserProfile profile)
		{
			if (_memberSensitiveData == null)
				return profile.IdentificationNumber;
			// A member with no row for this department has no number here, whatever the legacy global column says.
			return (await _memberSensitiveData.GetByDepartmentAndUserAsync(departmentId, profile.UserId))?.IdentificationNumber;
		}

		private async Task<string> AddressTextAsync(int? addressId)
		{
			if (!addressId.HasValue || addressId.Value <= 0 || _addresses == null)
				return null;
			return AddressText(await _addresses.GetByIdAsync(addressId.Value));
		}

		private static string AddressText(Address address) =>
			address == null ? null : Join(" ", Safe(address.Address1), Safe(address.City), Safe(address.State), Safe(address.PostalCode));

		private async Task<string> ContactNotesTextAsync(Contact contact)
		{
			if (_contactNotes == null)
				return null;
			// Filter here: the service read of a contact's notes has returned deleted ones as well.
			var texts = (await _contactNotes.GetContactNotesByContactIdAsync(contact.ContactId) ?? Enumerable.Empty<ContactNote>())
				.Where(n => n != null && !n.IsDeleted && n.DepartmentId == contact.DepartmentId)
				.OrderBy(n => n.AddedOn)
				.Select(n => Strip(Safe(n.Note)))
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.ToList();
			return texts.Count == 0 ? null : string.Join(" ", texts);
		}

		private async Task<string> CategoryNameAsync(int departmentId, string categoryId)
		{
			if (string.IsNullOrWhiteSpace(categoryId) || _contactCategories == null)
				return null;
			if (_categoryCache.TryGetValue(categoryId, out var cached))
				return cached;
			var category = await _contactCategories.GetByIdAsync(categoryId);
			var name = category != null && category.DepartmentId == departmentId ? Safe(category.Name) : null;
			_categoryCache[categoryId] = name;
			return name;
		}

		/// <summary>
		/// "Label value" for each custom field value of the entity whose field anyone who can open the entity may see:
		/// visibility Everyone, sensitivity None, enabled. Admin-only and restricted fields never enter the shared index.
		/// UdfFieldValues.Value is cataloged, so callers apply the protected-text rule.
		/// </summary>
		private async Task<string> CustomFieldsTextAsync(int departmentId, UdfEntityType entityType, string entityId)
		{
			if (_udfDefinitions == null || _udfFields == null || _udfValues == null || string.IsNullOrWhiteSpace(entityId))
				return null;

			var key = (departmentId, (int)entityType);
			if (!_udfDefinitionCache.TryGetValue(key, out var definitionId))
			{
				var definition = await _udfDefinitions.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, (int)entityType);
				definitionId = definition?.UdfDefinitionId;
				_udfDefinitionCache[key] = definitionId;
				_udfFieldCache[key] = definitionId == null ? new List<UdfField>()
					: (await _udfFields.GetFieldsByDefinitionIdAsync(definitionId) ?? Enumerable.Empty<UdfField>())
						.Where(f => f != null && f.IsEnabled && f.Visibility == (int)UdfFieldVisibility.Everyone && f.Sensitivity == (int)UdfFieldSensitivity.None)
						.ToList();
			}
			if (definitionId == null || !_udfFieldCache.TryGetValue(key, out var fields) || fields.Count == 0)
				return null;

			var byId = fields.ToDictionary(f => f.UdfFieldId, StringComparer.Ordinal);
			var parts = new List<string>();
			foreach (var value in (await _udfValues.GetFieldValuesByEntityAsync((int)entityType, entityId, definitionId) ?? Enumerable.Empty<UdfFieldValue>())
				.Where(v => v != null && v.UdfFieldId != null && byId.ContainsKey(v.UdfFieldId)).OrderBy(v => byId[v.UdfFieldId].SortOrder))
			{
				var text = Safe(value.Value);
				if (text == null)
					continue;
				parts.Add(Join(" ", Safe(byId[value.UdfFieldId].Label) ?? Safe(byId[value.UdfFieldId].Name), text));
			}
			return parts.Count == 0 ? null : string.Join(" ", parts);
		}

		// ---- helpers ---------------------------------------------------------------------------------------------

		private sealed class ProjectionContext
		{
			public bool ProtectedTextAllowed;
			public int CatalogVersion;
			public long PolicyEpoch;
		}

		private async Task<ProjectionContext> ContextAsync(int departmentId)
		{
			var ctx = new ProjectionContext();
			try
			{
				// Unknown protection state never widens exposure: index metadata only.
				ctx.ProtectedTextAllowed = !await _dataProtection.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Protection state for department {departmentId} could not be determined; projecting metadata only.");
				ctx.ProtectedTextAllowed = false;
			}
			try { ctx.CatalogVersion = await _dataProtection.GetPinnedCatalogVersionAsync(departmentId); } catch (Exception ex) { Logging.LogException(ex); }
			try { ctx.PolicyEpoch = (await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId))?.PolicyEpoch ?? 0; } catch (Exception ex) { Logging.LogException(ex); }
			return ctx;
		}

		private static SearchProjection New(int departmentId, string entityType, string entityId, ProjectionContext ctx)
		{
			return new SearchProjection
			{
				DepartmentId = departmentId,
				EntityType = entityType,
				EntityId = entityId,
				ProtectedCatalogVersion = ctx.CatalogVersion,
				PolicyEpoch = ctx.PolicyEpoch,
				IncludesProtectedText = ctx.ProtectedTextAllowed,
				IsActive = true,
				OccurredOn = DateTime.UtcNow
			};
		}

		/// <summary>Trimmed value, or null when empty, enveloped or redacted.</summary>
		public static string Safe(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			if (ProtectedDataEnvelope.HasEnvelopePrefix(trimmed) || trimmed == ProtectedDataEnvelope.RedactionValue)
				return null;
			return trimmed;
		}

		private static string Digits(string value)
		{
			var safe = Safe(value);
			if (safe == null)
				return null;
			var digits = new string(safe.Where(char.IsDigit).ToArray());
			return digits.Length >= 4 ? digits : null;
		}

		private static string Strip(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return null;
			var text = HtmlTags.Replace(html, " ");
			text = System.Net.WebUtility.HtmlDecode(text);
			return Whitespace.Replace(text, " ").Trim();
		}

		private static string Join(string separator, params string[] parts)
		{
			var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return kept.Count == 0 ? null : string.Join(separator, kept);
		}

		private static string Cap(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
				return null;
			return value.Length <= max ? value : value.Substring(0, max);
		}

		private static string Json(Dictionary<string, string> values)
		{
			var kept = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
			return kept.Count == 0 ? null : JsonConvert.SerializeObject(kept);
		}
	}
}
