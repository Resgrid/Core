using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistAccessService(IRecordsAuthorizationService authorization, IFeatureToggleService flags,
		IDepartmentSettingsService settings, ISubscriptionsService subscriptions, IDepartmentDataProtectionService protection,
		IReadinessAccessService readiness, IBusinessOperationsAccessService business, IAdminAssistCatalog catalog,
		TimeProvider clock, IAuthorizationService sourceAuthorization, IPermissionsService permissions = null) : IAdminAssistAccessService
	{
		public async Task<bool> CanAccessAsync(AdminAssistActor actor, bool setup, CancellationToken ct = default)
		{
			ct.ThrowIfCancellationRequested();
			if (actor == null || actor.DepartmentId <= 0 || string.IsNullOrWhiteSpace(actor.UserId)) return false;
			if (!await authorization.IsActiveMemberAsync(actor.UserId, actor.DepartmentId) ||
				!await authorization.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId)) return false;
			return await AdminAssistFeatureAvailability.IsEnabledAsync(flags, actor.DepartmentId, setup, ct);
		}

		public Task<IReadOnlyList<CapabilityAccess>> GetCapabilitiesAsync(AdminAssistActor actor, CancellationToken ct = default) => ReadCapabilitiesAsync(actor, catalog.Capabilities, ct);
		public async Task<CapabilityAccess> GetCapabilityAsync(AdminAssistActor actor, string capabilityId, CancellationToken ct = default) =>
			(await ReadCapabilitiesAsync(actor, new[] { catalog.Capabilities.Single(c => c.Id == capabilityId) }, ct)).Single();

		private async Task<IReadOnlyList<CapabilityAccess>> ReadCapabilitiesAsync(AdminAssistActor actor, IEnumerable<ProductCapability> capabilities, CancellationToken ct)
		{
			if (!await authorization.IsActiveMemberAsync(actor.UserId, actor.DepartmentId) ||
				!await authorization.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId)) throw new UnauthorizedAccessException();
			var observed = new Dictionary<string, (EvidenceState State, string Reason)>();
			var result = new List<CapabilityAccess>();
			bool canManageSubscription;
			try { canManageSubscription = await sourceAuthorization.CanUserManageSubscriptionAsync(actor.UserId, actor.DepartmentId); }
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception) { canManageSubscription = false; }
			foreach (var capability in capabilities)
			{
				ct.ThrowIfCancellationRequested();
				var reasons = new List<string>();
				var commercial = new List<EvidenceState>();
				var state = EvidenceState.Known;
				if (capability.ReleaseStatus != "available" && capability.ReleaseStatus != "preview")
				{
					result.Add(new CapabilityAccess(capability.Id, EvidenceState.Unavailable, new[] { "Feature" + capability.ReleaseStatus }, false, null, clock.GetUtcNow().UtcDateTime));
					continue;
				}
				foreach (var requirement in capability.Requirements)
				{
					var key = requirement.Kind + ":" + requirement.Id;
					if (!observed.TryGetValue(key, out var requirementState))
					{
						requirementState = await EvaluateAsync(actor, requirement, ct);
						observed[key] = requirementState;
					}
					if (requirement.Kind == "addon") commercial.Add(requirementState.State);
					if (requirementState.State != EvidenceState.Known)
					{
						reasons.Add(requirementState.Reason);
						state = state == EvidenceState.Unknown || requirementState.State == EvidenceState.Unknown ? EvidenceState.Unknown : requirementState.State;
					}
				}
				// The source gate is authoritative even when diagnostic subscription metadata appears available.
				if (state == EvidenceState.Known && capability.Location.Controller == "Subscription" &&
					!canManageSubscription)
				{
					state = EvidenceState.Unavailable;
					reasons.Add("ManagingMemberRequired");
				}
				try
				{
					if (state == EvidenceState.Known && !await OwningGateAsync(actor.DepartmentId, capability))
					{
						state = EvidenceState.Unavailable;
						reasons.Add("SourceAccessUnavailable");
					}
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (Exception) { state = EvidenceState.Unknown; reasons.Add("AvailabilityUnknown"); }
				result.Add(new CapabilityAccess(capability.Id, state, reasons.Distinct().ToArray(), state == EvidenceState.Known,
					state == EvidenceState.Known ? capability.Location.Url : null, clock.GetUtcNow().UtcDateTime,
					canManageSubscription && capability.Requirements.Any(r => r.Kind == "addon") ? "/User/Subscription/Index" : null,
					commercial.Count == 0 ? EvidenceState.NotApplicable : commercial.Any(s => s == EvidenceState.Unknown || s == EvidenceState.Redacted) ? EvidenceState.Unknown :
					commercial.Any(s => s == EvidenceState.Unavailable) ? EvidenceState.Unavailable : EvidenceState.Known));
			}
			if (!await authorization.IsActiveMemberAsync(actor.UserId, actor.DepartmentId) ||
				!await authorization.IsDepartmentAdminAsync(actor.UserId, actor.DepartmentId)) throw new UnauthorizedAccessException();
			return result;
		}

		private async Task<(EvidenceState, string)> EvaluateAsync(AdminAssistActor actor, CapabilityRequirement requirement, CancellationToken ct)
		{
			var departmentId = actor.DepartmentId;
			ct.ThrowIfCancellationRequested();
			try
			{
				switch (requirement.Kind)
				{
					case "permission":
						if (!Enum.TryParse<PermissionTypes>(requirement.Id, out var permissionType) || !Enum.IsDefined(permissionType))
							return (EvidenceState.Unknown, "AvailabilityUnknown");
						bool allowed;
						if (RecordPermissionCatalog.Get(permissionType) != null)
							allowed = await authorization.HasPermissionAsync(actor.UserId, departmentId, permissionType);
						else
						{
							if (permissions == null) return (EvidenceState.Unknown, "AvailabilityUnknown");
							var permission = await permissions.GetPermissionByDepartmentTypeAsync(departmentId, permissionType);
							// This endpoint is exclusively for fresh department admins; do not infer other actors' rights.
							allowed = permissions.IsUserAllowed(permission, await authorization.IsDepartmentAdminAsync(actor.UserId, departmentId), false, new List<PersonnelRole>());
						}
						return allowed ? (EvidenceState.Known, null) : (EvidenceState.Unavailable, "SourceAccessUnavailable");
					case "flag":
						var flag = await flags.EvaluateFreshAsync(requirement.Id, departmentId);
						return flag == null ? (EvidenceState.Unknown, "AvailabilityUnknown") : flag.IsEnabled ? (EvidenceState.Known, null) : (EvidenceState.Unavailable, "FeatureNotEnabled");
					case "module":
						var module = await settings.GetDepartmentModuleSettingsAsync(departmentId, true);
						if (module == null) return (EvidenceState.Unknown, "AvailabilityUnknown");
						bool? disabled = requirement.Id switch
						{
							"Messaging" => module.MessagingDisabled, "Mapping" => module.MappingDisabled, "Shifts" => module.ShiftsDisabled,
							"Logs" => module.LogsDisabled, "Reports" => module.ReportsDisabled, "Documents" => module.DocumentsDisabled,
							"Calendar" => module.CalendarDisabled, "Notes" => module.NotesDisabled, "Training" => module.TrainingDisabled,
							"Inventory" => module.InventoryDisabled, "Maintenance" => module.MaintenanceDisabled,
							"Checklists" => module.ChecklistsDisabled, "BusinessOperations" => module.BusinessOperationsDisabled, _ => null
						};
						return disabled == null ? (EvidenceState.Unknown, "AvailabilityUnknown") : disabled.Value ? (EvidenceState.Unavailable, "ModuleDisabled") : (EvidenceState.Known, null);
					case "protection":
						return await protection.GetStateAsync(departmentId, true) == DepartmentDataProtectionState.Enabled
							? (EvidenceState.Known, null) : (EvidenceState.Unavailable, "ProtectionEnrollmentRequired");
					case "addon":
						if (!Enum.TryParse<PlanAddonTypes>(requirement.Id, out var addon) || !Enum.IsDefined(addon) ||
							string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) || string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
							return (EvidenceState.Unknown, "SubscriptionStatusUnavailable");
						var plans = await subscriptions.GetAllAddonPlansByTypeAsync(addon);
						var ids = plans?.Where(p => p != null && p.AddonType == (int)addon && !string.IsNullOrWhiteSpace(p.PlanAddonId)).Select(p => p.PlanAddonId).Distinct().ToList();
						if (ids == null || ids.Count == 0) return (EvidenceState.Unknown, "SubscriptionStatusUnavailable");
						var payments = await subscriptions.GetCurrentPaymentAddonsForDepartmentAsync(departmentId, ids);
						if (payments == null) return (EvidenceState.Unknown, "SubscriptionStatusUnavailable");
						var now = clock.GetUtcNow().UtcDateTime;
						return payments.Any(p => p != null && p.DepartmentId == departmentId && ids.Contains(p.PlanAddonId) && p.EffectiveOn != default &&
							p.EffectiveOn <= now && p.EndingOn > now && p.EndingOn != DateTime.MaxValue && !string.Equals(p.TransactionId, "SYSTEM", StringComparison.OrdinalIgnoreCase))
							? (EvidenceState.Known, null) : (EvidenceState.Unavailable, "AddonRequired." + requirement.Id);
					default: return (EvidenceState.Unknown, "AvailabilityUnknown");
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception)
			{
				// Diagnostic fallback carries no exception message or source payload to logs/the browser.
				return (EvidenceState.Unknown, "AvailabilityUnknown");
			}
		}

		private Task<bool> OwningGateAsync(int departmentId, ProductCapability capability) => capability.Location.Controller switch
		{
			"Checklists" => readiness.CanUseChecklistsAsync(departmentId),
			"WorkOrders" => readiness.CanUseMaintenanceAsync(departmentId),
			"Invoicing" => business.CanUseInvoicingAsync(departmentId),
			"Bids" or "Contracts" or "RateSchedules" => business.CanUseContractorBillingAsync(departmentId),
			"CalOesMars" => business.CanUseCostRecoveryAsync(departmentId),
			"Workforce" when capability.Id == "pay-data-reporting" => business.CanUsePayDataReportingAsync(departmentId),
			"Workforce" => business.CanUseWorkforceAsync(departmentId),
			_ => Task.FromResult(true)
		};
	}
}
