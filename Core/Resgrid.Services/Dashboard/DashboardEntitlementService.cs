using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Services.Dashboard
{
	/// <summary>
	/// Widget visibility (home-dashboard-redesign-plan.md §9): completes the viewer the web tier started with the department
	/// facts the catalog references, once per request, sequentially (every check reads through the request's unit of work).
	/// Mirrors the nav's rules: the same access services, the same module switches, the same flags.
	/// </summary>
	public class DashboardEntitlementService : IDashboardEntitlementService
	{
		/// <summary>The policy that makes a member a Dispatcher for the built-in defaults (plan §8); the web tier always evaluates it.</summary>
		public const string DispatcherPolicy = DashboardViewer.DispatcherPolicy;

		private static readonly PermissionTypes[] AdpViewPermissions =
		{
			PermissionTypes.ViewProtectedCallData, PermissionTypes.ViewProtectedOperationalData,
			PermissionTypes.ViewProtectedPersonnelData, PermissionTypes.ViewProtectedContactData
		};

		/// <summary>The flag that offers each add-on (AddonCatalog); Push-To-Talk is always offered.</summary>
		private static readonly IReadOnlyDictionary<PlanAddonTypes, string> AddonOfferFlags = new Dictionary<PlanAddonTypes, string>
		{
			{ PlanAddonTypes.ADP, FeatureFlagKeys.DepartmentProtectedDataEnrollment },
			{ PlanAddonTypes.ReadinessPro, FeatureFlagKeys.MaintenanceWorkOrders },
			{ PlanAddonTypes.BusinessOperations, FeatureFlagKeys.BusinessOperations },
			{ PlanAddonTypes.EnhancedAi, FeatureFlagKeys.AiEnhanced },
			{ PlanAddonTypes.IncidentBackOffice, FeatureFlagKeys.IncidentBackOffice }
		};

		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IReadinessAccessService _readinessAccessService;
		private readonly IRecordsCutoverService _recordsCutoverService;
		private readonly IBusinessOperationsAccessService _businessOperationsAccessService;
		private readonly IIncidentBackOfficeAccessService _incidentBackOfficeAccessService;
		private readonly IEnhancedAiAccessService _enhancedAiAccessService;
		private readonly IVoiceService _voiceService;
		private readonly IAiDispatchAdminService _aiDispatchAdminService;
		private readonly INerisProfileService _nerisProfileService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IPermissionsService _permissionsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentDataProtectionService _dataProtectionService;

		public DashboardEntitlementService(IFeatureToggleService featureToggleService, IDepartmentSettingsService departmentSettingsService,
			IReadinessAccessService readinessAccessService, IRecordsCutoverService recordsCutoverService,
			IBusinessOperationsAccessService businessOperationsAccessService, IIncidentBackOfficeAccessService incidentBackOfficeAccessService,
			IEnhancedAiAccessService enhancedAiAccessService, IVoiceService voiceService, IAiDispatchAdminService aiDispatchAdminService,
			INerisProfileService nerisProfileService, IRecordsAuthorizationService recordsAuthorizationService, IPermissionsService permissionsService,
			IPersonnelRolesService personnelRolesService, IDepartmentDataProtectionService dataProtectionService)
		{
			_featureToggleService = featureToggleService;
			_departmentSettingsService = departmentSettingsService;
			_readinessAccessService = readinessAccessService;
			_recordsCutoverService = recordsCutoverService;
			_businessOperationsAccessService = businessOperationsAccessService;
			_incidentBackOfficeAccessService = incidentBackOfficeAccessService;
			_enhancedAiAccessService = enhancedAiAccessService;
			_voiceService = voiceService;
			_aiDispatchAdminService = aiDispatchAdminService;
			_nerisProfileService = nerisProfileService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
			_dataProtectionService = dataProtectionService;
		}

		public async Task<DashboardViewer> CompleteViewerAsync(DashboardViewer viewer)
		{
			await CompleteAsync(viewer, DashboardWidgetCatalog.ReferencedFlags.Concat(AddonOfferFlags.Values).Distinct(StringComparer.Ordinal),
				DashboardWidgetCatalog.ReferencedAccess, DashboardWidgetCatalog.ReferencedPermissions, includeAddons: viewer.IsDepartmentAdmin);
			return viewer;
		}

		public async Task<DashboardViewer> CompleteViewerForWidgetAsync(DashboardViewer viewer, DashboardWidgetDefinition definition)
		{
			await CompleteAsync(viewer, definition.Gates.Flags.Concat(definition.Gates.AnyFlags), definition.Gates.Access, definition.Gates.Permissions, includeAddons: false);
			return viewer;
		}

		public bool IsVisible(DashboardWidgetDefinition definition, DashboardViewer viewer)
		{
			if (definition == null || viewer == null)
				return false;

			var gates = definition.Gates;
			return PassesIdentity(gates, viewer) &&
				gates.Flags.All(viewer.HasFlag) &&
				(gates.AnyFlags.Count == 0 || gates.AnyFlags.Any(viewer.HasFlag)) &&
				gates.Access.All(viewer.Passes);
		}

		public bool IsLockedForAdmin(DashboardWidgetDefinition definition, DashboardViewer viewer)
		{
			if (definition?.Gates.Addon == null || viewer == null || !viewer.IsDepartmentAdmin || IsVisible(definition, viewer))
				return false;

			var addon = definition.Gates.Addon.Value;
			return viewer.OfferedAddons.Contains(addon) && !viewer.HeldAddons.Contains(addon) && PassesIdentity(definition.Gates, viewer);
		}

		public IReadOnlyList<DashboardWidgetDefinition> GetVisibleWidgets(DashboardViewer viewer) =>
			DashboardWidgetCatalog.All.Where(d => IsVisible(d, viewer)).ToList();

		public IReadOnlyList<DashboardWidgetDefinition> GetLockedWidgets(DashboardViewer viewer) =>
			DashboardWidgetCatalog.All.Where(d => IsLockedForAdmin(d, viewer)).ToList();

		private static bool PassesIdentity(DashboardWidgetGates gates, DashboardViewer viewer)
		{
			if (gates.Role == DashboardRoles.DepartmentAdmin && !viewer.IsDepartmentAdmin)
				return false;

			if (gates.Role == DashboardRoles.GroupAdmin && !viewer.IsDepartmentAdmin && !viewer.IsGroupAdmin)
				return false;

			return gates.Policies.All(viewer.HasPolicy) && gates.Permissions.All(viewer.Permissions.Contains);
		}

		private async Task CompleteAsync(DashboardViewer viewer, IEnumerable<string> flags, IEnumerable<DashboardAccessChecks> access,
			IEnumerable<PermissionTypes> permissions, bool includeAddons)
		{
			var departmentId = viewer.DepartmentId;

			foreach (var flag in flags.Distinct(StringComparer.Ordinal))
				if (!viewer.EnabledFlags.Contains(flag) && await _featureToggleService.IsEnabledAsync(flag, departmentId))
					viewer.EnabledFlags.Add(flag);

			DepartmentModuleSettings modules = null;
			RecordsModuleState records = null;

			foreach (var check in access.Distinct())
			{
				if (viewer.Access.ContainsKey(check))
					continue;

				modules ??= await _departmentSettingsService.GetDepartmentModuleSettingsAsync(departmentId) ?? new DepartmentModuleSettings();

				if (check == DashboardAccessChecks.RecordsUsable || check == DashboardAccessChecks.LegacyLogsOnly)
					records ??= await _recordsCutoverService.GetModuleStateAsync(departmentId) ?? new RecordsModuleState();

				viewer.Access[check] = await EvaluateAsync(check, departmentId, modules, records);
			}

			foreach (var permission in permissions.Distinct())
				if (!viewer.Permissions.Contains(permission) && await _recordsAuthorizationService.HasPermissionAsync(viewer.UserId, departmentId, permission))
					viewer.Permissions.Add(permission);

			viewer.AdpEnforced = await _dataProtectionService.IsProtectionEnforcedAsync(departmentId);
			if (viewer.AdpEnforced && viewer.AdpViewPermissions.Count == 0)
			{
				var roles = await _personnelRolesService.GetRolesForUserAsync(viewer.UserId, departmentId) ?? new List<PersonnelRole>();
				foreach (var type in AdpViewPermissions)
				{
					// Never the null-allow convention for ADP: a missing row resolves through AdpPermissionDefaults.
					var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, type)
						?? new Permission { DepartmentId = departmentId, PermissionType = (int)type, Action = (int)AdpPermissionDefaults.For(type) };

					if (_permissionsService.IsUserAllowed(permission, viewer.IsDepartmentAdmin, viewer.IsGroupAdmin, roles))
						viewer.AdpViewPermissions.Add(type);
				}
			}

			if (includeAddons)
				await CompleteAddonsAsync(viewer);
		}

		private async Task CompleteAddonsAsync(DashboardViewer viewer)
		{
			var departmentId = viewer.DepartmentId;

			foreach (var (addon, flag) in AddonOfferFlags)
				if (viewer.HasFlag(flag))
					viewer.OfferedAddons.Add(addon);
			viewer.OfferedAddons.Add(PlanAddonTypes.PTT);

			if (await _readinessAccessService.CanUseMaintenanceAsync(departmentId)) viewer.HeldAddons.Add(PlanAddonTypes.ReadinessPro);
			if (await _businessOperationsAccessService.HasActiveAddonAsync(departmentId)) viewer.HeldAddons.Add(PlanAddonTypes.BusinessOperations);
			if (await _incidentBackOfficeAccessService.HasActiveAddonAsync(departmentId)) viewer.HeldAddons.Add(PlanAddonTypes.IncidentBackOffice);
			if (await _enhancedAiAccessService.HasActiveAddonAsync(departmentId)) viewer.HeldAddons.Add(PlanAddonTypes.EnhancedAi);
			if (await _voiceService.CanDepartmentUseVoiceAsync(departmentId)) viewer.HeldAddons.Add(PlanAddonTypes.PTT);
			if (viewer.AdpEnforced) viewer.HeldAddons.Add(PlanAddonTypes.ADP);
		}

		private async Task<bool> EvaluateAsync(DashboardAccessChecks check, int departmentId, DepartmentModuleSettings modules, RecordsModuleState records)
		{
			switch (check)
			{
				case DashboardAccessChecks.MessagingEnabled: return !modules.MessagingDisabled;
				case DashboardAccessChecks.MappingEnabled: return !modules.MappingDisabled;
				case DashboardAccessChecks.ShiftsEnabled: return !modules.ShiftsDisabled;
				case DashboardAccessChecks.LogsEnabled: return !modules.LogsDisabled;
				case DashboardAccessChecks.ReportsEnabled: return !modules.ReportsDisabled;
				case DashboardAccessChecks.DocumentsEnabled: return !modules.DocumentsDisabled;
				case DashboardAccessChecks.CalendarEnabled: return !modules.CalendarDisabled;
				case DashboardAccessChecks.NotesEnabled: return !modules.NotesDisabled;
				case DashboardAccessChecks.TrainingEnabled: return !modules.TrainingDisabled;
				case DashboardAccessChecks.InventoryEnabled: return !modules.InventoryDisabled;
				case DashboardAccessChecks.ContactsEnabled: return !modules.ContactsDisabled;
				case DashboardAccessChecks.CertificationsEnabled: return !modules.CertificationsDisabled;
				case DashboardAccessChecks.DeploymentsEnabled: return !modules.DeploymentsDisabled;
				case DashboardAccessChecks.ChatEnabled: return !modules.ChatDisabled;
				case DashboardAccessChecks.ChecklistsUsable: return await _readinessAccessService.CanUseChecklistsAsync(departmentId);
				case DashboardAccessChecks.MaintenanceUsable: return await _readinessAccessService.CanUseMaintenanceAsync(departmentId);
				case DashboardAccessChecks.RecordsUsable: return !modules.RecordsDisabled && records.FlagEnabled && records.RecordsUsable;
				case DashboardAccessChecks.LegacyLogsOnly: return !modules.LogsDisabled && !records.RecordsUsable;
				case DashboardAccessChecks.InvoicingVisible:
					return !modules.BusinessOperationsDisabled && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.CustomerInvoicing, departmentId);
				case DashboardAccessChecks.ContractorBillingUsable:
					return !modules.BusinessOperationsDisabled && await _businessOperationsAccessService.CanUseContractorBillingAsync(departmentId);
				case DashboardAccessChecks.CostRecoveryUsable:
					return !modules.BusinessOperationsDisabled && await _businessOperationsAccessService.CanUseCostRecoveryAsync(departmentId);
				case DashboardAccessChecks.WorkforceUsable:
					return !modules.BusinessOperationsDisabled && await _businessOperationsAccessService.CanUseWorkforceAsync(departmentId);
				case DashboardAccessChecks.PayDataReportingUsable:
					return !modules.BusinessOperationsDisabled && await _businessOperationsAccessService.CanUsePayDataReportingAsync(departmentId);
				case DashboardAccessChecks.IncidentBackOfficeEnabled:
					// Module on AND the add-on held or lapsed (ReadOnly still renders); never bought = None = admins get a locked card.
					return (await _incidentBackOfficeAccessService.GetAccessAsync(departmentId))?.CanRead == true;
				case DashboardAccessChecks.IncidentBusinessEnabled: return await _incidentBackOfficeAccessService.IsBusinessEnabledAsync(departmentId);
				case DashboardAccessChecks.IncidentCoordinationEnabled: return await _incidentBackOfficeAccessService.IsCoordinationEnabledAsync(departmentId);
				case DashboardAccessChecks.EnhancedAiAssistant: return await _enhancedAiAccessService.CanUseAsync(departmentId, FeatureFlagKeys.AiAssistant);
				case DashboardAccessChecks.AiDispatchAvailable:
				{
					var status = await _aiDispatchAdminService.GetStatusAsync(departmentId);
					return status != null && status.HostEnabled && status.RolledOut && status.Entitled;
				}
				case DashboardAccessChecks.VoiceAddon: return await _voiceService.CanDepartmentUseVoiceAsync(departmentId);
				case DashboardAccessChecks.AdminAssistAvailable:
					return await AdminAssistFeatureAvailability.IsEnabledAsync(_featureToggleService, departmentId, setup: true) ||
						await AdminAssistFeatureAvailability.IsWorkspaceEnabledAsync(_featureToggleService, departmentId);
				case DashboardAccessChecks.WeatherConfigured:
					return !string.IsNullOrWhiteSpace(Config.MappingConfig.BigBoardOpenWeatherApiKey) || !string.IsNullOrWhiteSpace(Config.MappingConfig.DispatchOpenWeatherApiKey);
				case DashboardAccessChecks.NerisWorkflowEnabled:
					return Config.NerisConfig.Enabled && await _nerisProfileService.IsWorkflowEnabledAsync(departmentId);
				case DashboardAccessChecks.DataProtectionOffered:
					return await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, departmentId) ||
						await _dataProtectionService.IsProtectionEnforcedAsync(departmentId);
				default:
					return false;
			}
		}
	}
}
