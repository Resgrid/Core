using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services.Search
{
	public partial class UnifiedSearchService
	{
		private readonly IDepartmentsService _departments;
		private readonly IPermissionsService _permissions;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly ICallsService _calls;
		private readonly IUnitsService _units;
		private readonly IMessageService _messages;
		private readonly IDocumentsService _documents;
		private readonly INotesService _notes;
		private readonly IContactsService _contacts;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly ISearchProjectionsRepository _projections;

		// One read operation owns this snapshot. Never cache authorization across users or requests.
		private sealed class SearchAccess
		{
			public SearchPrincipal Principal;
			public DepartmentGroupMember Group;
			public List<PersonnelRole> Roles;
			public Permission PersonnelPermission;
			public Permission UnitPermission;
			public int CatalogVersion;
			public long PolicyEpoch;
			public bool ProtectedTextAllowed;
			public string GlobalGeneration => GlobalSearchGeneration.Compute(CatalogVersion, PolicyEpoch);
			public string RecordsGeneration => RecordsSearchGeneration.Compute(CatalogVersion, PolicyEpoch);
		}

		private async Task<SearchAccess> LoadAccessAsync(SearchPrincipal supplied)
		{
			try
			{
				var department = await _departments.GetDepartmentByIdAsync(supplied.DepartmentId, true);
				var member = await _departments.GetDepartmentMemberAsync(supplied.UserId, supplied.DepartmentId, true);
				if (department == null || department.DepartmentId != supplied.DepartmentId)
					return null;
				var owner = department.ManagingUserId == supplied.UserId;
				if (member == null ? !owner : !ActiveMember(member, supplied.DepartmentId, supplied.UserId))
					return null;

				var modules = await _departmentSettings.GetDepartmentModuleSettingsAsync(supplied.DepartmentId, true);
				if (modules == null) return null;
				var policy = await _dataProtection.GetPolicyByDepartmentIdAsync(supplied.DepartmentId, true);
				if (policy != null && policy.DepartmentId != supplied.DepartmentId) return null;
				var group = await _groups.GetGroupMemberForUserAsync(supplied.UserId, supplied.DepartmentId);
				if (group != null && (group.DepartmentId != supplied.DepartmentId || group.UserId != supplied.UserId)) return null;

				return new SearchAccess
				{
					Principal = new SearchPrincipal
					{
						UserId = supplied.UserId,
						DepartmentId = supplied.DepartmentId,
						IsDepartmentAdmin = supplied.IsDepartmentAdmin && (owner || member?.IsAdmin == true),
						IsGroupAdmin = group?.IsAdmin == true,
						HasClaim = supplied.HasClaim,
						IsModuleEnabled = module => ModuleEnabled(modules, module)
					},
					Group = group,
					Roles = (await _roles.GetRolesForUserAsync(supplied.UserId, supplied.DepartmentId) ?? new List<PersonnelRole>())
						.Where(r => r.DepartmentId == supplied.DepartmentId).ToList(),
					PersonnelPermission = await _permissions.GetPermissionByDepartmentTypeAsync(supplied.DepartmentId, PermissionTypes.ViewGroupUsers),
					UnitPermission = await _permissions.GetPermissionByDepartmentTypeAsync(supplied.DepartmentId, PermissionTypes.ViewGroupUnits),
					CatalogVersion = policy?.CatalogVersion ?? 0,
					PolicyEpoch = policy?.PolicyEpoch ?? 0,
					// Enrollment and offboarding are also restrictive: no stale plaintext matches during a transition.
					ProtectedTextAllowed = policy == null || policy.State == (int)DepartmentDataProtectionState.Disabled
				};
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Search access could not be verified.");
				return null;
			}
		}

		private static bool ActiveMember(DepartmentMember member, int departmentId, string userId) =>
			member != null && member.DepartmentId == departmentId && member.UserId == userId &&
			!member.IsDeleted && member.IsDisabled != true;

		private static bool ProjectionIsCurrent(GlobalSearchHit hit, SearchProjection projection, SearchAccess access) =>
			hit.DepartmentId == access.Principal.DepartmentId && projection.DepartmentId == access.Principal.DepartmentId &&
			hit.Generation == access.GlobalGeneration && hit.RowVersion > 0 && hit.RowVersion == projection.RowVersion &&
			hit.EntityType == projection.EntityType && hit.EntityId == projection.EntityId && !projection.DeletedOn.HasValue &&
			projection.PolicyEpoch == access.PolicyEpoch && projection.ProtectedCatalogVersion == access.CatalogVersion &&
			(!projection.IsAdminOnly || access.Principal.IsDepartmentAdmin) &&
			(!projection.IncludesProtectedText || access.ProtectedTextAllowed);

		private async Task<bool> AuthorizeAsync(GlobalSearchHit hit, SearchAccess access)
		{
			var principal = access.Principal;
			var departmentId = principal.DepartmentId;
			var userId = principal.UserId;
			try
			{
				switch (hit.EntityType)
				{
					case SearchEntityTypes.Call:
						if (!int.TryParse(hit.EntityId, out var callId)) return false;
						var call = await _calls.GetCallByIdAsync(callId, true);
						return call != null && call.DepartmentId == departmentId && !call.IsDeleted &&
							await _authorization.CanUserViewCallAsync(userId, callId);
					case SearchEntityTypes.Unit:
						if (!int.TryParse(hit.EntityId, out var unitId)) return false;
						var unit = await _units.GetUnitByIdAsync(unitId);
						return unit != null && unit.DepartmentId == departmentId &&
							CanViewGroup(access.UnitPermission, unit.StationGroupId, access);
					case SearchEntityTypes.Personnel:
						var member = await _departments.GetDepartmentMemberAsync(hit.EntityId, departmentId, true);
						if (!ActiveMember(member, departmentId, hit.EntityId) || member.IsHidden == true) return false;
						if (hit.EntityId == userId) return true;
						var group = await _groups.GetGroupMemberForUserAsync(hit.EntityId, departmentId);
						if (group != null && (group.DepartmentId != departmentId || group.UserId != hit.EntityId)) return false;
						return CanViewGroup(access.PersonnelPermission, group?.DepartmentGroupId, access);
					case SearchEntityTypes.Message:
						if (!int.TryParse(hit.EntityId, out var messageId)) return false;
						var message = await _messages.GetMessageByIdAsync(messageId);
						return message != null && message.DepartmentId == departmentId && !message.IsDeleted &&
							(!message.ExpireOn.HasValue || message.ExpireOn > DateTime.UtcNow) &&
							(message.SendingUserId == userId || message.ReceivingUserId == userId ||
							message.MessageRecipients?.Any(r => r.UserId == userId && !r.IsDeleted &&
								(!r.DepartmentId.HasValue || r.DepartmentId == departmentId)) == true);
					case SearchEntityTypes.Contact:
						var contact = await _contacts.GetContactByIdAsync(hit.EntityId);
						return contact != null && contact.DepartmentId == departmentId && !contact.IsDeleted && access.ProtectedTextAllowed;
					case SearchEntityTypes.Document:
						if (!int.TryParse(hit.EntityId, out var documentId)) return false;
						var document = await _documents.GetDocumentByIdAsync(documentId);
						return document != null && document.DepartmentId == departmentId &&
							(!document.AdminsOnly || principal.IsDepartmentAdmin) &&
							(!document.RemoveOn.HasValue || document.RemoveOn > DateTime.UtcNow);
					case SearchEntityTypes.Note:
						if (!int.TryParse(hit.EntityId, out var noteId)) return false;
						var note = await _notes.GetNoteByIdAsync(noteId);
						return note != null && note.DepartmentId == departmentId &&
							(!note.IsAdminOnly || principal.IsDepartmentAdmin) &&
							(!note.ExpiresOn.HasValue || note.ExpiresOn > DateTime.UtcNow);
					default:
						return false;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Search hit access could not be verified.");
				return false;
			}
		}

		private bool CanViewGroup(Permission permission, int? targetGroupId, SearchAccess access)
		{
			if (permission == null) return true;
			if (permission.DepartmentId != access.Principal.DepartmentId) return false;
			// The generic permission overload lets Everyone bypass LockToGroup. Visibility must apply the lock
			// first, matching SecurityLogic's personnel/unit matrix without relying on its fail-open cache.
			if (!access.Principal.IsDepartmentAdmin && permission.LockToGroup &&
				(!targetGroupId.HasValue || targetGroupId != access.Group?.DepartmentGroupId)) return false;
			return _permissions.IsUserAllowed(permission, access.Principal.IsDepartmentAdmin,
				access.Principal.IsGroupAdmin, access.Roles);
		}

		private static bool ModuleEnabled(DepartmentModuleSettings settings, string module)
		{
			switch (module)
			{
				case SystemActionModules.Messaging: return !settings.MessagingDisabled;
				case SystemActionModules.Mapping: return !settings.MappingDisabled;
				case SystemActionModules.Shifts: return !settings.ShiftsDisabled;
				case SystemActionModules.Logs: return !settings.LogsDisabled;
				case SystemActionModules.Reports: return !settings.ReportsDisabled;
				case SystemActionModules.Documents: return !settings.DocumentsDisabled;
				case SystemActionModules.Calendar: return !settings.CalendarDisabled;
				case SystemActionModules.Notes: return !settings.NotesDisabled;
				case SystemActionModules.Training: return !settings.TrainingDisabled;
				case SystemActionModules.Inventory: return !settings.InventoryDisabled;
				case SystemActionModules.Maintenance: return !settings.MaintenanceDisabled;
				case SystemActionModules.BusinessOperations: return !settings.BusinessOperationsDisabled;
				default: return string.IsNullOrWhiteSpace(module);
			}
		}
	}
}
