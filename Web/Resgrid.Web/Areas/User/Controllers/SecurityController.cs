using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Helpers;
using Resgrid.Web.Attributes;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Microsoft.Extensions.Localization;

namespace Resgrid.Web.Areas.User.Controllers
{
	// Department security settings, SSO/SCIM configuration and audit logs. Individual actions still
	// check IsUserDepartmentAdmin; this sends an anonymous caller to sign-in rather than leaving the
	// controller reachable with no identity at all.
	[Area("User")]
	[Authorize]
	public class SecurityController : SecureBaseController
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuditService _auditService;
		private readonly IPermissionsService _permissionsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> _secLocalizer;
		private readonly IDepartmentSsoService _ssoService;
		private readonly IEncryptionService _encryptionService;
		private readonly IRecordsCutoverService _recordsCutoverService;

		private readonly IPasskeyFeatureGates _passkeyGates;
		private readonly IMfaEvidenceService _mfaEvidence;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IDepartmentApiKeysService _departmentApiKeysService;

		public SecurityController(IDepartmentsService departmentsService, IAuditService auditService,
			IPermissionsService permissionsService, IEventAggregator eventAggregator,
			IDepartmentSettingsService departmentSettingsService, ISystemAuditsService systemAuditsService,
			UserManager<IdentityUser> userManager,
			IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> secLocalizer,
			IDepartmentSsoService ssoService,
			IEncryptionService encryptionService,
			IRecordsCutoverService recordsCutoverService,
			IPasskeyFeatureGates passkeyGates,
			IMfaEvidenceService mfaEvidence,
			IMfaPolicyService mfaPolicy,
			IDepartmentApiKeysService departmentApiKeysService)
		{
			_departmentApiKeysService = departmentApiKeysService;
			_mfaEvidence = mfaEvidence;
			_mfaPolicy = mfaPolicy;
			_passkeyGates = passkeyGates;
			_departmentsService = departmentsService;
			_auditService = auditService;
			_permissionsService = permissionsService;
			_eventAggregator = eventAggregator;
			_departmentSettingsService = departmentSettingsService;
			_systemAuditsService = systemAuditsService;
			_userManager = userManager;
			_secLocalizer = secLocalizer;
			_ssoService = ssoService;
			_encryptionService = encryptionService;
			_recordsCutoverService = recordsCutoverService;
		}

		public async Task<IActionResult> Index()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var model = new PermissionsView();

			var permissions = await _permissionsService.GetAllPermissionsForDepartmentAsync(DepartmentId);

			// Option text is localized: the permission notes refer to these options by name in each language.
			var optionLabels = PermissionOptionLabels.From(_secLocalizer);

			// Every row renders from PermissionScreenCatalog, the table SetPermission and SetPermissionData validate
			// against. With no saved row the dropdown and the group-only box show what the runtime applies to a
			// missing row; where no offered action matches it (the workflow rows) a disabled "not saved" option does.
			int Value(PermissionTypes type) =>
				PermissionScreenCatalog.CurrentValue(PermissionScreenCatalog.Get(type), PermissionScreenCatalog.EffectiveRow(permissions, type));

			bool Lock(PermissionTypes type) =>
				PermissionScreenCatalog.CurrentLock(PermissionScreenCatalog.Get(type), PermissionScreenCatalog.EffectiveRow(permissions, type));

			List<SelectListItem> Options(PermissionTypes type)
			{
				var entry = PermissionScreenCatalog.Get(type);
				var notSavedLabel = entry.NotSavedLabelKey != null ? _secLocalizer[entry.NotSavedLabelKey].Value : null;
				return PermissionScreenCatalog.Options(entry, PermissionScreenCatalog.EffectiveRow(permissions, type), optionLabels, notSavedLabel);
			}

			model.AddUsers = Value(PermissionTypes.AddPersonnel);
			model.AddUserPermissions = Options(PermissionTypes.AddPersonnel);

			model.RemoveUsers = Value(PermissionTypes.RemovePersonnel);
			model.RemoveUserPermissions = Options(PermissionTypes.RemovePersonnel);

			model.CreateCall = Value(PermissionTypes.CreateCall);
			model.CreateCallPermissions = Options(PermissionTypes.CreateCall);

			model.CreateTraining = Value(PermissionTypes.CreateTraining);
			model.CreateTrainingPermissions = Options(PermissionTypes.CreateTraining);

			model.CreateDocument = Value(PermissionTypes.CreateDocument);
			model.CreateDocumentPermissions = Options(PermissionTypes.CreateDocument);

			model.CreateCalendarEntry = Value(PermissionTypes.CreateCalendarEntry);
			model.CreateCalendarEntryPermissions = Options(PermissionTypes.CreateCalendarEntry);

			model.CreateNote = Value(PermissionTypes.CreateNote);
			model.CreateNotePermissions = Options(PermissionTypes.CreateNote);

			model.CreateLog = Value(PermissionTypes.CreateLog);
			model.CreateLogPermissions = Options(PermissionTypes.CreateLog);

			model.DeleteLog = Value(PermissionTypes.DeleteLog);
			model.DeleteLogPermissions = Options(PermissionTypes.DeleteLog);

			model.CreateShift = Value(PermissionTypes.CreateShift);
			model.CreateShiftPermissions = Options(PermissionTypes.CreateShift);

			model.ViewPersonalInfo = Value(PermissionTypes.ViewPersonalInfo);
			model.ViewPersonalInfoPermissions = Options(PermissionTypes.ViewPersonalInfo);

			model.AdjustInventory = Value(PermissionTypes.AdjustInventory);
			model.AdjustInventoryPermissions = Options(PermissionTypes.AdjustInventory);

			model.ViewPersonnelLocation = Value(PermissionTypes.CanSeePersonnelLocations);
			model.LockViewPersonneLocationToGroup = Lock(PermissionTypes.CanSeePersonnelLocations);
			model.ViewPersonnelLocationPermissions = Options(PermissionTypes.CanSeePersonnelLocations);

			model.ViewUnitLocation = Value(PermissionTypes.CanSeeUnitLocations);
			model.LockViewUnitLocationToGroup = Lock(PermissionTypes.CanSeeUnitLocations);
			model.ViewUnitLocationPermissions = Options(PermissionTypes.CanSeeUnitLocations);

			model.CreateMessage = Value(PermissionTypes.CreateMessage);
			model.CreateMessagePermissions = Options(PermissionTypes.CreateMessage);

			model.ViewGroupsUsers = Value(PermissionTypes.ViewGroupUsers);
			model.LockViewGroupsUsersToGroup = Lock(PermissionTypes.ViewGroupUsers);
			model.ViewGroupUsersPermissions = Options(PermissionTypes.ViewGroupUsers);

			model.DeleteCall = Value(PermissionTypes.DeleteCall);
			model.LockDeleteCallToGroup = Lock(PermissionTypes.DeleteCall);
			model.DeleteCallPermissions = Options(PermissionTypes.DeleteCall);

			model.CloseCall = Value(PermissionTypes.CloseCall);
			model.LockCloseCallToGroup = Lock(PermissionTypes.CloseCall);
			model.CloseCallPermissions = Options(PermissionTypes.CloseCall);

			model.AddCallData = Value(PermissionTypes.AddCallData);
			model.LockAddCallDataToGroup = Lock(PermissionTypes.AddCallData);
			model.AddCallDataPermissions = Options(PermissionTypes.AddCallData);

			model.ViewGroupsUnits = Value(PermissionTypes.ViewGroupUnits);
			model.LockViewGroupsUnitsToGroup = Lock(PermissionTypes.ViewGroupUnits);
			model.ViewGrouUnitsPermissions = Options(PermissionTypes.ViewGroupUnits);

			model.ViewContacts = Value(PermissionTypes.ContactView);
			model.ViewContactsPermissions = Options(PermissionTypes.ContactView);

			model.EditContacts = Value(PermissionTypes.ContactEdit);
			model.EditContactsPermissions = Options(PermissionTypes.ContactEdit);

			model.DeleteContacts = Value(PermissionTypes.ContactDelete);
			model.DeleteContactsPermissions = Options(PermissionTypes.ContactDelete);

			model.CreateWorkflow = Value(PermissionTypes.CreateWorkflow);
			model.CreateWorkflowPermissions = Options(PermissionTypes.CreateWorkflow);

			model.ManageWorkflowCredentials = Value(PermissionTypes.ManageWorkflowCredentials);
			model.ManageWorkflowCredentialsPermissions = Options(PermissionTypes.ManageWorkflowCredentials);

			model.ViewWorkflowRuns = Value(PermissionTypes.ViewWorkflowRuns);
			model.ViewWorkflowRunsPermissions = Options(PermissionTypes.ViewWorkflowRuns);

			model.UseCalendarSync = Value(PermissionTypes.UseCalendarSync);
			model.UseCalendarSyncPermissions = Options(PermissionTypes.UseCalendarSync);

			// Dispatch app login and commander access default to Everyone so departments that never configure them are unaffected.
			model.DispatchAppLogin = Value(PermissionTypes.DispatchAppLogin);
			model.DispatchAppLoginPermissions = Options(PermissionTypes.DispatchAppLogin);

			model.CommandAppLogin = Value(PermissionTypes.CommandAppLogin);
			model.CommandAppLoginPermissions = Options(PermissionTypes.CommandAppLogin);

			// ── Advanced Data Protection (ADP) permissions ─────────────────────────────
			// Missing rows resolve through AdpPermissionDefaults, NOT the wide-open no-row convention — the
			// preselected value here is exactly what enforcement uses. Only the two values a runtime check reads are
			// shown (PermissionScreenCatalog); egress never offers "Everyone" because reconfiguring it widens disclosure.
			model.ViewProtectedCallData = Value(PermissionTypes.ViewProtectedCallData);
			model.ViewProtectedCallDataPermissions = Options(PermissionTypes.ViewProtectedCallData);

			model.ConfigureProtectedDataEgress = Value(PermissionTypes.ConfigureProtectedDataEgress);
			model.ConfigureProtectedDataEgressPermissions = Options(PermissionTypes.ConfigureProtectedDataEgress);

			// ── Records (RMS) permissions, PermissionTypes 50–67 ──────────────────────────────
			// Rows come from RecordPermissionCatalog so this screen, ClaimsLogic.AddRecordClaims and the
			// activation-time row migration share one set of no-row defaults. A missing row preselects that
			// default, which for the Logs-parity types equals today's CreateLog/DeleteLog fall-through.
			model.RecordsPermissions = PermissionScreenCatalog.RecordCatalogs
				.SelectMany(catalog => RecordsPermissionRows.Build(permissions, catalog, optionLabels)).ToList();
			var recordsState = await _recordsCutoverService.GetModuleStateAsync(DepartmentId);
			model.RecordsFlagEnabled = recordsState != null && recordsState.FlagEnabled;
			model.RecordsActivated = recordsState != null && recordsState.RecordsUsable;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.IsManagingUser = department.ManagingUserId == UserId;
			model.Require2FAForAdmins = await _departmentSettingsService.GetRequire2FAForAdminsAsync(DepartmentId);

			// Guard: check whether managing user and current user have 2FA enabled
			var managingIdentityUser = await _userManager.FindByIdAsync(department.ManagingUserId);
			var currentIdentityUser = await _userManager.FindByIdAsync(UserId);
			model.ManagingUserHas2FAEnabled = managingIdentityUser != null && await _userManager.GetTwoFactorEnabledAsync(managingIdentityUser);
			model.CurrentUserHas2FAEnabled = currentIdentityUser != null && await _userManager.GetTwoFactorEnabledAsync(currentIdentityUser);

			var require2FAOptions = new List<dynamic>();
			require2FAOptions.Add(new { Id = 0, Name = _secLocalizer["Require2FADisabled"].Value });
			require2FAOptions.Add(new { Id = 1, Name = _secLocalizer["Require2FADeptAdmins"].Value });
			require2FAOptions.Add(new { Id = 2, Name = _secLocalizer["Require2FAAllAdmins"].Value });
			model.Require2FAForAdminsOptions = new SelectList(require2FAOptions, "Id", "Name");

			return View(model);
		}

		public async Task<IActionResult> Audits()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			return View();
		}

		public async Task<IActionResult> GetAuditLogsList()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var auditLogsJson = new List<AuditLogJson>();
			var auditLogs = await _auditService.GetAllAuditLogsForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId, true);
			var personnelNamesByUserId = personnelNames
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
			var usersByUserId = users
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
			var systemActor = _secLocalizer["AuditLogsSystemActor"].Value;
			var unknownTime = _secLocalizer["AuditLogsUnknownTime"].Value;

			foreach (var auditLog in auditLogs)
			{
				var auditJson = new AuditLogJson();
				auditJson.AuditLogId = auditLog.AuditLogId;
				personnelNamesByUserId.TryGetValue(auditLog.UserId ?? String.Empty, out var personName);
				usersByUserId.TryGetValue(auditLog.UserId ?? String.Empty, out var user);
				auditJson.Name = personName != null && !String.IsNullOrWhiteSpace(personName.Name)
					? personName.Name
					: (!String.IsNullOrWhiteSpace(auditLog.UserId) ? auditLog.UserId : systemActor);
				auditJson.Message = auditLog.Message;
				auditJson.Successful = auditLog.Successful;

				if (auditLog.LoggedOn.HasValue)
				{
					auditJson.Timestamp = auditLog.LoggedOn.Value.TimeConverterToString(department);
					auditJson.TimestampSort = auditLog.LoggedOn.Value.Ticks / TimeSpan.TicksPerMillisecond;
				}
				else
					auditJson.Timestamp = unknownTime;

				auditJson.Type = GetAuditLogTypeDisplayName((AuditLogTypes)auditLog.LogType);
				auditJson.SearchTerms = String.Join(" ", new[]
				{
					auditJson.Name,
					auditLog.UserId,
					user?.UserName,
					user?.Email,
					auditLog.AuditLogId.ToString(CultureInfo.InvariantCulture),
					auditLog.DepartmentId.ToString(CultureInfo.InvariantCulture),
					auditLog.LogType.ToString(CultureInfo.InvariantCulture),
					auditLog.ObjectId,
					auditLog.ObjectDepartmentId.ToString(CultureInfo.InvariantCulture),
					auditLog.IpAddress,
					auditLog.ServerName,
					auditJson.Timestamp,
					auditLog.LoggedOn?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					auditJson.Type,
					((AuditLogTypes)auditLog.LogType).ToString()
				}.Where(x => !String.IsNullOrWhiteSpace(x)));

				auditLogsJson.Add(auditJson);
			}

			return Json(auditLogsJson);
		}

		/// <summary>
		/// The audit type as shown on the audit log list and detail pages, in the viewer's language. A type with no Security
		/// resource yet (a newly added AuditLogTypes value) falls back to the audit service's English name
		/// rather than showing the raw resource key.
		/// </summary>
		private string GetAuditLogTypeDisplayName(AuditLogTypes type)
		{
			var localized = _secLocalizer["AuditLogType" + type];
			return localized.ResourceNotFound ? _auditService.GetAuditLogTypeString(type) : localized.Value;
		}

		public async Task<IActionResult> ViewAudit(int auditLogId)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var auditLog = await _auditService.GetAuditLogByIdAsync(auditLogId);
			if (auditLog == null)
				return NotFound();

			if (auditLog.DepartmentId != DepartmentId)
				return Unauthorized();

			var model = new ViewAuditLogView
			{
				AuditLog = auditLog,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId),
				Type = (AuditLogTypes)auditLog.LogType,
				TypeName = GetAuditLogTypeDisplayName((AuditLogTypes)auditLog.LogType)
			};

			return View(model);
		}

		#region Async

		/// <summary>
		/// Sets the department-level 2FA enforcement scope. Only the managing user (owner) may change this.
		/// scope: 0=disabled, 1=dept admins+managing user, 2=also group admins
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> Set2FARequirement(int scope, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			if (department.ManagingUserId != UserId)
				return new StatusCodeResult((int)HttpStatusCode.Forbidden);

			if (scope < 0 || scope > 2)
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			// Guard: if enabling enforcement (scope > 0), both managing user and current admin must have 2FA enabled
			if (scope > 0)
			{
				var managingUser = await _userManager.FindByIdAsync(department.ManagingUserId);
				var currentUser = await _userManager.FindByIdAsync(UserId);

				bool managingHas2FA = managingUser != null && await _userManager.GetTwoFactorEnabledAsync(managingUser);
				bool currentHas2FA = currentUser != null && await _userManager.GetTwoFactorEnabledAsync(currentUser);

				if (!managingHas2FA || !currentHas2FA)
					return new StatusCodeResult((int)HttpStatusCode.PreconditionFailed);
			}

			await _departmentSettingsService.SaveOrUpdateSettingAsync(DepartmentId, scope.ToString(), DepartmentSettingTypes.Require2FAForAdmins, cancellationToken);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.PermissionsChanged;
			auditEvent.Before = string.Empty;
			auditEvent.After = $"Require2FAForAdmins={scope}";
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		// POST + antiforgery: permission changes are state-changing and must never be reachable by a
		// cross-site top-level GET navigation riding the SameSite=Lax auth cookie.
		// Permissions are a security change (passkey plan section 7.6 row 13): the sign-in methods count, Responder approval does not.
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SetPermission(int type, int perm, bool? lockToGroup)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return new StatusCodeResult((int)HttpStatusCode.NotModified);

			// Only a type, action and lock the Permissions screen offers can be stored (PermissionScreenCatalog);
			// anything else, including an unparsable value, would leave a row the claim chain mishandles.
			var entry = PermissionScreenCatalog.Get(type);
			if (!ModelState.IsValid || entry == null || lockToGroup == true && !entry.LockToGroupOffered)
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			var permissionType = (PermissionTypes)type;
			var before = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, permissionType);
			var effective = await EffectiveRowAsync(permissionType, before);
			if (!PermissionScreenCatalog.IsListed(entry, perm, effective))
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			// The action write keeps the saved roles (they have their own endpoint and stay on screen when the dropdown
			// changes) and, when the request leaves the lock out, the saved group lock, so neither control clears the other.
			var result = await _permissionsService.SetPermissionForDepartmentAsync(DepartmentId, UserId, permissionType, (PermissionActions)perm,
				effective?.Data, ResolveLockToGroup(entry, lockToGroup, effective));

			PublishPermissionChange(permissionType, before, result);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SetPermissionData(int type, string data, bool? lockToGroup)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return new StatusCodeResult((int)HttpStatusCode.NotModified);

			var entry = PermissionScreenCatalog.Get(type);
			if (!ModelState.IsValid || entry == null || !entry.RolesOffered || lockToGroup == true && !entry.LockToGroupOffered
			    || !TryNormalizeRoleIds(data, out var roleIds))
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			var permissionType = (PermissionTypes)type;
			var before = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, permissionType);
			var effective = await EffectiveRowAsync(permissionType, before);

			// Roles or the group-only box can change before the department has saved this row (a Records row still at
			// its default, or Transfer/Issue still following Adjust Inventory). Save the row at the action the screen is
			// showing. The workflow rows show no action until one is chosen, so there is nothing to attach roles to yet.
			var action = PermissionScreenCatalog.CurrentValue(entry, effective);
			if (action == PermissionScreenCatalog.NotSavedValue)
				return new StatusCodeResult((int)HttpStatusCode.BadRequest);

			var result = await _permissionsService.SetPermissionForDepartmentAsync(DepartmentId, UserId, permissionType, (PermissionActions)action,
				roleIds, ResolveLockToGroup(entry, lockToGroup, effective));

			PublishPermissionChange(permissionType, before, result);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[HttpGet]
		public async Task<IActionResult> GetRolesForPermission(int type)
		{
			var before = await EffectiveRowAsync((PermissionTypes)type, await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, (PermissionTypes)type));

			if (before != null)
				return Json(before.Data);

			return Json("");
		}

		/// <summary>
		/// The row the screen shows for a type: the department's own row or, for Transfer and Issue Inventory with no row
		/// of their own, the Adjust Inventory row InventoryAuthorizationService falls back to.
		/// </summary>
		private async Task<Permission> EffectiveRowAsync(PermissionTypes type, Permission own)
		{
			if (own != null || type is not (PermissionTypes.TransferInventory or PermissionTypes.IssueInventory))
				return own;

			return await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.AdjustInventory);
		}

		/// <summary>
		/// The posted lock when the request carries one; otherwise the lock the screen is showing. Rows without a
		/// group-only box always save no lock.
		/// </summary>
		private static bool ResolveLockToGroup(PermissionScreenEntry entry, bool? posted, Permission effective) =>
			entry.LockToGroupOffered && (posted ?? PermissionScreenCatalog.CurrentLock(entry, effective));

		/// <summary>
		/// The roles picker posts department role ids joined with commas. Anything else would make the claim chain's
		/// int.Parse throw at sign-in, so it is refused; blanks and repeats are dropped.
		/// </summary>
		private static bool TryNormalizeRoleIds(string data, out string normalized)
		{
			normalized = string.Empty;
			if (string.IsNullOrWhiteSpace(data))
				return true;

			var ids = new List<int>();
			foreach (var part in data.Split(','))
			{
				var trimmed = part.Trim();
				if (trimmed.Length == 0)
					continue;

				if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
					return false;

				if (!ids.Contains(id))
					ids.Add(id);
			}

			normalized = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
			return true;
		}

		private void PublishPermissionChange(PermissionTypes type, Permission before, Permission after)
		{
			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.PermissionsChanged;
			auditEvent.Before = before.CloneJsonToString();
			auditEvent.After = after.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			SecurityCacheTypes? cache = type switch
			{
				PermissionTypes.CanSeePersonnelLocations => SecurityCacheTypes.WhoCanViewPersonnelLocations,
				PermissionTypes.CanSeeUnitLocations => SecurityCacheTypes.WhoCanViewUnitLocations,
				PermissionTypes.ViewGroupUnits => SecurityCacheTypes.WhoCanViewUnits,
				PermissionTypes.ViewGroupUsers => SecurityCacheTypes.WhoCanViewPersonnel,
				_ => null
			};

			if (cache.HasValue)
			{
				var securityEvent = new SecurityRefreshEvent();
				securityEvent.DepartmentId = DepartmentId;
				securityEvent.Type = cache.Value;
				_eventAggregator.SendMessage<SecurityRefreshEvent>(securityEvent);
			}
		}
		#endregion Async

		#region SSO / SCIM Management

		// -- SSO Index --------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Sso(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var configList = configs?.ToList() ?? new System.Collections.Generic.List<DepartmentSsoConfig>();

			// Build an encrypted token carrying departmentId:departmentCode so it can
			// be passed safely over the public internet without exposing either value.
			var plainToken = $"{department.DepartmentId}:{department.Code}";
			var encryptedToken = _encryptionService.Encrypt(plainToken);

			var apiBase = Config.SystemBehaviorConfig.ResgridApiBaseUrl;

			var model = new SsoIndexView
			{
				IsAdmin = true,
				DepartmentId = department.DepartmentId,
				Configs = configList.Select(c => new SsoConfigRowView
				{
					DepartmentSsoConfigId = c.DepartmentSsoConfigId,
					ProviderType = ((SsoProviderType)c.SsoProviderType).ToString().ToLowerInvariant(),
					IsEnabled = c.IsEnabled,
					Identifier = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.ClientId : c.EntityId,
					EndpointUrl = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.Authority : c.MetadataUrl,
					AllowLocalLogin = c.AllowLocalLogin,
					AutoProvisionUsers = c.AutoProvisionUsers,
					ScimEnabled = c.ScimEnabled,
					HasScimBearerToken = !string.IsNullOrWhiteSpace(c.EncryptedScimBearerToken),
					CreatedOn = c.CreatedOn
				}).ToList(),
				HasOidcConfig = configList.Any(c => c.SsoProviderType == (int)SsoProviderType.Oidc),
				HasSamlConfig = configList.Any(c => c.SsoProviderType == (int)SsoProviderType.Saml2),
				EncryptedDepartmentToken = Uri.EscapeDataString(encryptedToken),
				ScimBaseUrl = $"{apiBase}{Config.SsoConfig.ScimBasePath}",
				ApiBaseUrl = apiBase,
				SsoDiscoveryUrl = $"{apiBase}{Config.SsoConfig.SsoDiscoveryPath}?departmentToken={Uri.EscapeDataString(encryptedToken)}"
			};

			return View(model);
		}

		// -- Create SSO config ------------------------------------------------

		[HttpGet]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SsoNew(string providerType, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var apiBase = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			var configId = Guid.NewGuid().ToString();
			var plainToken = $"{department.DepartmentId}:{department.Code}";

			var model = new SsoConfigEditView
			{
				IsNew = true,
				DepartmentSsoConfigId = configId,
				ProviderType = providerType ?? "oidc",
				EntityId = string.Equals(providerType, "saml2", StringComparison.OrdinalIgnoreCase)
					? $"{apiBase}{Config.SsoConfig.SamlEntityIdBasePath}{configId}"
					: null,
				IsEnabled = true,
				AllowLocalLogin = true,
				ProviderTypes = BuildProviderTypeList(providerType ?? "oidc"),
				RankList = await BuildRankListAsync(null),
				AcsUrl = $"{apiBase}{Config.SsoConfig.SamlAcsPath}?departmentToken={Uri.EscapeDataString(_encryptionService.Encrypt(plainToken))}",
				ApiBaseUrl = apiBase,
				OidcBrokerRedirectUri = OidcBrokerRedirectUri(apiBase)
			};

			return View("SsoEdit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SsoNew(SsoConfigEditView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			if (!ModelState.IsValid)
			{
				await PopulateSsoEditViewContextAsync(model);
				return View("SsoEdit", model);
			}

			if (!System.Enum.TryParse<SsoProviderType>(model.ProviderType, ignoreCase: true, out var providerType) || !System.Enum.IsDefined(providerType))
			{
				ModelState.AddModelError("ProviderType", _secLocalizer["SsoErrorInvalidProviderType"].Value);
				await PopulateSsoEditViewContextAsync(model);
				return View("SsoEdit", model);
			}

			ValidateSsoProviderConfiguration(model, providerType, hasStoredIdpCertificate: false);
			if (!ModelState.IsValid)
			{
				await PopulateSsoEditViewContextAsync(model);
				return View("SsoEdit", model);
			}

			var existing = await _ssoService.GetSsoConfigForDepartmentAsync(DepartmentId, providerType, cancellationToken);
			if (existing != null)
			{
				ModelState.AddModelError("", string.Format(_secLocalizer["SsoErrorConfigAlreadyExists"].Value, model.ProviderType.ToUpperInvariant()));
				await PopulateSsoEditViewContextAsync(model);
				return View("SsoEdit", model);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var configId = Guid.TryParse(model.DepartmentSsoConfigId, out var parsedConfigId)
				? parsedConfigId.ToString()
				: Guid.NewGuid().ToString();
			var apiBase = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			var encryptedDepartmentToken = _encryptionService.Encrypt($"{department.DepartmentId}:{department.Code}");
			var config = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = configId,
				DepartmentId = DepartmentId,
				SsoProviderType = (int)providerType,
				IsEnabled = model.IsEnabled,
				ClientId = model.ClientId,
				EncryptedClientSecret = model.ClientSecret,
				Authority = model.Authority,
				MetadataUrl = model.MetadataUrl,
				EntityId = providerType == SsoProviderType.Saml2 && string.IsNullOrWhiteSpace(model.EntityId)
					? $"{apiBase}{Config.SsoConfig.SamlEntityIdBasePath}{configId}"
					: model.EntityId,
				AssertionConsumerServiceUrl = providerType == SsoProviderType.Saml2 && string.IsNullOrWhiteSpace(model.AssertionConsumerServiceUrl)
					? $"{apiBase}{Config.SsoConfig.SamlAcsPath}?departmentToken={Uri.EscapeDataString(encryptedDepartmentToken)}"
					: model.AssertionConsumerServiceUrl,
				IdpSsoUrl = model.IdpSsoUrl,
				EncryptedIdpCertificate = model.IdpCertificate,
				EncryptedSigningCertificate = model.SigningCertificate,
				AttributeMappingJson = model.AttributeMappingJson,
				AllowLocalLogin = model.AllowLocalLogin,
				AutoProvisionUsers = model.AutoProvisionUsers,
				DefaultRankId = model.DefaultRankId,
				ScimEnabled = model.ScimEnabled,
				CreatedByUserId = UserId,
				CreatedOn = DateTime.UtcNow
			};

			await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);
			TempData["SsoSuccess"] = string.Format(_secLocalizer["SsoConfigCreatedSuccess"].Value, model.ProviderType.ToUpperInvariant());
			return RedirectToAction("Sso");
		}

		// -- Edit SSO config --------------------------------------------------

		[HttpGet]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SsoEdit(string id, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs?.FirstOrDefault(c => c.DepartmentSsoConfigId == id);
			if (config == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var apiBase = Config.SystemBehaviorConfig.ResgridApiBaseUrl;

			var model = new SsoConfigEditView
			{
				IsNew = false,
				DepartmentSsoConfigId = config.DepartmentSsoConfigId,
				ProviderType = ((SsoProviderType)config.SsoProviderType).ToString().ToLowerInvariant(),
				IsEnabled = config.IsEnabled,
				ClientId = config.ClientId,
				Authority = config.Authority,
				MetadataUrl = config.MetadataUrl,
				EntityId = config.EntityId,
				AssertionConsumerServiceUrl = config.AssertionConsumerServiceUrl,
				IdpSsoUrl = config.IdpSsoUrl,
				AttributeMappingJson = config.AttributeMappingJson,
				AllowLocalLogin = config.AllowLocalLogin,
				AutoProvisionUsers = config.AutoProvisionUsers,
				DefaultRankId = config.DefaultRankId,
				ScimEnabled = config.ScimEnabled,
				HasClientSecret = !string.IsNullOrWhiteSpace(config.EncryptedClientSecret),
				HasIdpCertificate = !string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate),
				HasSigningCertificate = !string.IsNullOrWhiteSpace(config.EncryptedSigningCertificate),
				ProviderTypes = BuildProviderTypeList(((SsoProviderType)config.SsoProviderType).ToString().ToLowerInvariant()),
				RankList = await BuildRankListAsync(config.DefaultRankId),
				AcsUrl = $"{apiBase}{Config.SsoConfig.SamlAcsPath}?departmentToken={Uri.EscapeDataString(_encryptionService.Encrypt($"{department.DepartmentId}:{department.Code}"))}",
				ApiBaseUrl = apiBase,
				OidcBrokerRedirectUri = OidcBrokerRedirectUri(apiBase)
			};

			return View("SsoEdit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SsoEdit(SsoConfigEditView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			if (!ModelState.IsValid)
			{
				await PopulateSsoEditViewContextAsync(model);
				return View("SsoEdit", model);
			}

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs?.FirstOrDefault(c => c.DepartmentSsoConfigId == model.DepartmentSsoConfigId);
			if (config == null)
				return NotFound();

			var providerType = (SsoProviderType)config.SsoProviderType;
			ValidateSsoProviderConfiguration(model, providerType, !string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate));
			if (!ModelState.IsValid)
			{
				model.HasClientSecret = !string.IsNullOrWhiteSpace(config.EncryptedClientSecret);
				model.HasIdpCertificate = !string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate);
				model.HasSigningCertificate = !string.IsNullOrWhiteSpace(config.EncryptedSigningCertificate);
				await PopulateSsoEditViewContextAsync(model);
				return View("SsoEdit", model);
			}

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			config.IsEnabled = model.IsEnabled;
			config.ClientId = model.ClientId ?? config.ClientId;
			config.Authority = model.Authority ?? config.Authority;
			config.MetadataUrl = model.MetadataUrl ?? config.MetadataUrl;
			config.EntityId = model.EntityId ?? config.EntityId;
			config.AssertionConsumerServiceUrl = model.AssertionConsumerServiceUrl ?? config.AssertionConsumerServiceUrl;
			config.IdpSsoUrl = model.IdpSsoUrl ?? config.IdpSsoUrl;
			config.AttributeMappingJson = model.AttributeMappingJson ?? config.AttributeMappingJson;
			config.AllowLocalLogin = model.AllowLocalLogin;
			config.AutoProvisionUsers = model.AutoProvisionUsers;
			config.DefaultRankId = model.DefaultRankId ?? config.DefaultRankId;
			config.ScimEnabled = model.ScimEnabled;
			config.UpdatedByUserId = UserId;

			// Only overwrite secrets when a new plaintext value is supplied
			config.EncryptedClientSecret = !string.IsNullOrWhiteSpace(model.ClientSecret) ? model.ClientSecret : null;
			config.EncryptedIdpCertificate = !string.IsNullOrWhiteSpace(model.IdpCertificate) ? model.IdpCertificate : null;
			config.EncryptedSigningCertificate = !string.IsNullOrWhiteSpace(model.SigningCertificate) ? model.SigningCertificate : null;

			await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);
			TempData["SsoSuccess"] = _secLocalizer["SsoConfigUpdatedSuccess"].Value;
			return RedirectToAction("Sso");
		}

		// -- Delete SSO config ------------------------------------------------

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SsoDelete(string id, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs?.FirstOrDefault(c => c.DepartmentSsoConfigId == id);
			if (config == null)
				return NotFound();

			var providerType = (SsoProviderType)config.SsoProviderType;
			await _ssoService.DeleteSsoConfigAsync(DepartmentId, providerType, cancellationToken);

			TempData["SsoSuccess"] = string.Format(_secLocalizer["SsoConfigDeletedSuccess"].Value, providerType.ToString().ToUpperInvariant());
			return RedirectToAction("Sso");
		}

		// -- Inline SCIM token generation (from SSO index page) ---------------

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> GenerateScimTokenFromSso(string id, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs?.FirstOrDefault(c => c.DepartmentSsoConfigId == id);
			if (config == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var tokenBytes = new byte[Config.SsoConfig.ScimBearerTokenByteLength];
			System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
			var newToken = Convert.ToBase64String(tokenBytes);

			var hadExistingToken = !string.IsNullOrWhiteSpace(config.EncryptedScimBearerToken);

			config.EncryptedScimBearerToken = newToken;
			config.ScimEnabled = true;
			config.UpdatedByUserId = UserId;
			config.EncryptedClientSecret = null;
			config.EncryptedIdpCertificate = null;
			config.EncryptedSigningCertificate = null;

			await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

			var scimTokenAuditEvent = new AuditEvent();
			scimTokenAuditEvent.DepartmentId = DepartmentId;
			scimTokenAuditEvent.UserId = UserId;
			scimTokenAuditEvent.Type = hadExistingToken ? AuditLogTypes.ScimBearerTokenRotated : AuditLogTypes.ScimBearerTokenProvisioned;
			scimTokenAuditEvent.Successful = true;
			scimTokenAuditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			scimTokenAuditEvent.ServerName = Environment.MachineName;
			scimTokenAuditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			scimTokenAuditEvent.After = System.Text.Json.JsonSerializer.Serialize(new
			{
				DepartmentSsoConfigId = id,
				DepartmentId,
				DepartmentCode = department.Code,
				Action = hadExistingToken ? "ScimBearerTokenRotated" : "ScimBearerTokenProvisioned"
			});
			_eventAggregator.SendMessage<AuditEvent>(scimTokenAuditEvent);

			// Reload all configs so the page is fully up-to-date
			configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var configList = configs?.ToList() ?? new System.Collections.Generic.List<DepartmentSsoConfig>();

			var plainToken = $"{department.DepartmentId}:{department.Code}";
			var encryptedToken = _encryptionService.Encrypt(plainToken);
			var apiBase = Config.SystemBehaviorConfig.ResgridApiBaseUrl;

			var model = new SsoIndexView
			{
				IsAdmin = true,
				DepartmentId = department.DepartmentId,
				NewScimBearerToken = newToken,
				NewScimConfigId = id,
				Configs = configList.Select(c => new SsoConfigRowView
				{
					DepartmentSsoConfigId = c.DepartmentSsoConfigId,
					ProviderType = ((SsoProviderType)c.SsoProviderType).ToString().ToLowerInvariant(),
					IsEnabled = c.IsEnabled,
					Identifier = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.ClientId : c.EntityId,
					EndpointUrl = c.SsoProviderType == (int)SsoProviderType.Oidc ? c.Authority : c.MetadataUrl,
					AllowLocalLogin = c.AllowLocalLogin,
					AutoProvisionUsers = c.AutoProvisionUsers,
					ScimEnabled = c.ScimEnabled,
					HasScimBearerToken = !string.IsNullOrWhiteSpace(c.EncryptedScimBearerToken),
					CreatedOn = c.CreatedOn
				}).ToList(),
				HasOidcConfig = configList.Any(c => c.SsoProviderType == (int)SsoProviderType.Oidc),
				HasSamlConfig = configList.Any(c => c.SsoProviderType == (int)SsoProviderType.Saml2),
				EncryptedDepartmentToken = Uri.EscapeDataString(encryptedToken),
				ScimBaseUrl = $"{apiBase}{Config.SsoConfig.ScimBasePath}",
				ApiBaseUrl = apiBase,
				SsoDiscoveryUrl = $"{apiBase}{Config.SsoConfig.SsoDiscoveryPath}?departmentToken={Uri.EscapeDataString(encryptedToken)}"
			};

			return View("Sso", model);
		}

		// -- SCIM setup -------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> ScimSetup(string id, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs?.FirstOrDefault(c => c.DepartmentSsoConfigId == id);
			if (config == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var encryptedToken = _encryptionService.Encrypt($"{department.DepartmentId}:{department.Code}");

			var model = new ScimSetupView
			{
				DepartmentSsoConfigId = config.DepartmentSsoConfigId,
				ProviderType = ((SsoProviderType)config.SsoProviderType).ToString().ToLowerInvariant(),
				ScimEnabled = config.ScimEnabled,
				HasScimBearerToken = !string.IsNullOrWhiteSpace(config.EncryptedScimBearerToken),
				ScimBaseUrl = $"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}{Config.SsoConfig.ScimBasePath}",
				EncryptedDepartmentToken = Uri.EscapeDataString(encryptedToken),
				DepartmentId = department.DepartmentId
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> RotateScimToken(string id, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var config = configs?.FirstOrDefault(c => c.DepartmentSsoConfigId == id);
			if (config == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			// Generate a cryptographically random bearer token using the configured byte length
			var tokenBytes = new byte[Config.SsoConfig.ScimBearerTokenByteLength];
			System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
			var newToken = Convert.ToBase64String(tokenBytes);

			config.EncryptedScimBearerToken = newToken; // SaveSsoConfigAsync encrypts it
			config.ScimEnabled = true;
			config.UpdatedByUserId = UserId;
			// Null out other secrets so they are not overwritten
			config.EncryptedClientSecret = null;
			config.EncryptedIdpCertificate = null;
			config.EncryptedSigningCertificate = null;

			await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

			var encryptedToken = _encryptionService.Encrypt($"{department.DepartmentId}:{department.Code}");

			var model = new ScimSetupView
			{
				DepartmentSsoConfigId = config.DepartmentSsoConfigId,
				ProviderType = ((SsoProviderType)config.SsoProviderType).ToString().ToLowerInvariant(),
				ScimEnabled = true,
				HasScimBearerToken = true,
				NewScimBearerToken = newToken, // one-time plaintext exposure
				ScimBaseUrl = $"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}{Config.SsoConfig.ScimBasePath}",
				EncryptedDepartmentToken = Uri.EscapeDataString(encryptedToken),
				DepartmentId = department.DepartmentId
			};

			return View("ScimSetup", model);
		}

		// -- Department API keys ----------------------------------------------

		[HttpGet]
		public async Task<IActionResult> ApiKeys()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var model = await BuildApiKeysViewAsync(new ApiKeysView());
			model.SuccessMessage = TempData["ApiKeySuccess"] as string;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> CreateApiKey(ApiKeysView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			model ??= new ApiKeysView();
			var scopes = DepartmentApiKeyScopes.Parse(string.Join(" ", model.SelectedScopes ?? new List<string>()));
			var maxDays = Math.Max(1, Config.SecurityConfig.DepartmentApiKeyMaxLifetimeDays);
			var days = Math.Min(Math.Max(1, model.ExpiresInDays), maxDays);

			if (string.IsNullOrWhiteSpace(model.Name))
				model.ErrorMessage = _secLocalizer["ApiKeyErrorName"].Value;
			else if (scopes.Count == 0)
				model.ErrorMessage = _secLocalizer["ApiKeyErrorScopes"].Value;
			else
			{
				var badRange = _departmentApiKeysService.ValidateAllowedIpRanges(model.AllowedIpRanges);
				if (badRange != null)
					model.ErrorMessage = string.Format(_secLocalizer["ApiKeyErrorIp"].Value, badRange);
			}

			if (model.ErrorMessage == null)
			{
				var result = await _departmentApiKeysService.CreateKeyAsync(DepartmentId, model.Name, scopes, DateTime.UtcNow.AddDays(days),
					model.AllowedIpRanges, UserId, IpAddressHelper.GetRequestIP(Request, true), cancellationToken);

				if (result.Success)
				{
					// The one response that carries the key: keep it out of every cache.
					Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
					Response.Headers["Pragma"] = "no-cache";

					var created = await BuildApiKeysViewAsync(new ApiKeysView());
					created.NewKey = result.Key;
					created.NewKeyName = result.ApiKey.Name;
					return View("ApiKeys", created);
				}

				model.ErrorMessage = string.Format(_secLocalizer["ApiKeyErrorCreate"].Value, result.Error);
			}

			model.ExpiresInDays = days;
			model.SelectedScopes = scopes;
			return View("ApiKeys", await BuildApiKeysViewAsync(model));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> RevokeApiKey(string id, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			if (await _departmentApiKeysService.RevokeKeyAsync(DepartmentId, id, UserId, IpAddressHelper.GetRequestIP(Request, true), cancellationToken))
				TempData["ApiKeySuccess"] = _secLocalizer["ApiKeyRevoked"].Value;

			return RedirectToAction("ApiKeys");
		}

		private async Task<ApiKeysView> BuildApiKeysViewAsync(ApiKeysView model)
		{
			var maxDays = Math.Max(1, Config.SecurityConfig.DepartmentApiKeyMaxLifetimeDays);

			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Keys = await _departmentApiKeysService.GetKeysForDepartmentAsync(DepartmentId);
			model.ApiBaseUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			model.ExpiryChoices = new[] { 30, 90, 180, 365, 730 }.Where(x => x <= maxDays).ToList();
			if (!model.ExpiryChoices.Contains(maxDays) && maxDays < 730)
				model.ExpiryChoices.Add(maxDays);
			if (!model.ExpiryChoices.Contains(model.ExpiresInDays))
				model.ExpiresInDays = model.ExpiryChoices.Contains(90) ? 90 : model.ExpiryChoices.Last();

			return model;
		}

		// -- Security policy --------------------------------------------------

		[HttpGet]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SecurityPolicy(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var policy = await _ssoService.GetSecurityPolicyForDepartmentAsync(DepartmentId, cancellationToken);
			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var hasActiveConfig = configs?.Any(c => c.IsEnabled) ?? false;

			var model = new SecurityPolicyEditView
			{
				HasActiveSsoConfig = hasActiveConfig,
				DataClassificationLevels = BuildDataClassificationList(policy?.DataClassificationLevel ?? 0)
			};

			if (policy != null)
			{
				model.DepartmentSecurityPolicyId = policy.DepartmentSecurityPolicyId;
				model.RequireMfa = policy.RequireMfa;
				model.RequireSso = policy.RequireSso;
				model.SessionTimeoutMinutes = policy.SessionTimeoutMinutes;
				model.MaxConcurrentSessions = policy.MaxConcurrentSessions;
				model.AllowedIpRanges = policy.AllowedIpRanges;
				model.PasswordExpirationDays = policy.PasswordExpirationDays;
				model.MinPasswordLength = policy.MinPasswordLength >= 8 ? policy.MinPasswordLength : 8;
				model.DataClassificationLevel = policy.DataClassificationLevel;
			}
			else
			{
				model.MinPasswordLength = 8;
			}

			CopyMethodSwitches(policy ?? new DepartmentSecurityPolicy(), model);
			await DescribeMethodSwitchesAsync(model);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> SecurityPolicy(SecurityPolicyEditView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var configs = await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken);
			var hasActiveConfig = configs?.Any(c => c.IsEnabled) ?? false;
			model.HasActiveSsoConfig = hasActiveConfig;
			await DescribeMethodSwitchesAsync(model);
			model.DataClassificationLevels = BuildDataClassificationList(model.DataClassificationLevel);

			if (!ModelState.IsValid)
				return View(model);

			if (model.RequireSso && !hasActiveConfig)
			{
				ModelState.AddModelError("RequireSso", _secLocalizer["SecurityPolicyCannotEnableRequireSso"].Value);
				return View(model);
			}

			// Enforce system minimum — department policies cannot be weaker than 8 chars.
			if (model.MinPasswordLength > 0 && model.MinPasswordLength < 8)
			{
				ModelState.AddModelError("MinPasswordLength", _secLocalizer["PwdMinLengthTooLow"].Value);
				return View(model);
			}

			var existing = await _ssoService.GetSecurityPolicyForDepartmentAsync(DepartmentId, cancellationToken);
			var policy = existing ?? new DepartmentSecurityPolicy
			{
				DepartmentId = DepartmentId,
				CreatedOn = DateTime.UtcNow
			};
			var before = DepartmentSecurityPolicyDecisions.SnapshotMfaRules(policy);

			policy.RequireMfa = model.RequireMfa;
			policy.RequireSso = model.RequireSso;
			policy.SessionTimeoutMinutes = model.SessionTimeoutMinutes;
			policy.MaxConcurrentSessions = model.MaxConcurrentSessions;
			policy.AllowedIpRanges = model.AllowedIpRanges;
			policy.PasswordExpirationDays = model.PasswordExpirationDays;
			// Ensure stored value is never below the system minimum of 8.
			policy.MinPasswordLength = model.MinPasswordLength >= 8 ? model.MinPasswordLength : 8;
			// Complexity is always system-enforced (digit + uppercase + lowercase); do not store a weaker override.
			policy.RequirePasswordComplexity = true;
			policy.DataClassificationLevel = model.DataClassificationLevel;

			// Which second factors are accepted is the managing member's decision (passkey plan section 10.1). Other
			// administrators see the switches read-only, and whatever their form posts for them is ignored: a disabled
			// checkbox still posts its hidden "false", which must never switch a method off.
			if (model.CanChangeMethodSwitches)
			{
				policy.AllowPasskeysForLoginMfa = model.AllowPasskeysForLoginMfa;
				policy.AllowPasskeysForAdp = model.AllowPasskeysForAdp;
				policy.AllowFederatedMfaForLoginMfa = model.AllowFederatedMfaForLoginMfa;
				policy.AllowFederatedMfaForAdp = model.AllowFederatedMfaForAdp;
				policy.AllowResponderApproval = model.AllowResponderApproval;
				policy.AcceptRecentLoginMfaForAdp = model.AcceptRecentLoginMfaForAdp;
				policy.AcceptRecentUnlockMfaForAdp = model.AcceptRecentUnlockMfaForAdp;

				// The shared-device policy is the managing member's too (plan section 10.5). Stricter values reach running
				// sessions at their next request; a new app requirement needs the deployment to offer shared mode.
				policy.SharedIdleLockMinutes = model.SharedIdleLockMinutes;
				policy.SharedShiftHours = model.SharedShiftHours;
				policy.SharedModeRequiredApps = model.SharedModeRequiredApps;
				if (policy.SharedIdleLockMinutes < 1 || policy.SharedIdleLockMinutes > SharedSessionRules.MaxIdleLockMinutes)
				{
					ModelState.AddModelError("SharedIdleLockMinutes", string.Format(_secLocalizer["SecurityPolicySharedIdleOutOfRange"].Value,
						SharedSessionRules.MaxIdleLockMinutes));
					return View(model);
				}
				if (policy.SharedShiftHours < 1 || policy.SharedShiftHours > SharedSessionRules.MaxShiftHours)
				{
					ModelState.AddModelError("SharedShiftHours", string.Format(_secLocalizer["SecurityPolicySharedShiftOutOfRange"].Value,
						SharedSessionRules.MaxShiftHours));
					return View(model);
				}
				if (!_passkeyGates.SharedDeviceModeEnabled && DepartmentSecurityPolicyDecisions.AddsSharedRequirement(before, policy))
				{
					ModelState.AddModelError(string.Empty, _secLocalizer["SecurityPolicySharedRequirementUnavailable"].Value);
					return View(model);
				}
			}

			// Turning provider step-up on needs a mapping that passed its test, and cannot be authorized by provider step-up
			// itself: the 5-minute proof above must be a Resgrid factor (plan section 7.8).
			if (DepartmentSecurityPolicyDecisions.EnablesFederatedMfa(before, policy))
			{
				if (await _ssoService.GetTestedFederatedMfaConfigAsync(DepartmentId, cancellationToken) == null)
				{
					ModelState.AddModelError(string.Empty, _secLocalizer["SecurityPolicyFederatedMappingUntested"].Value);
					return View(model);
				}

				var user = await _userManager.GetUserAsync(User);
				if (await StepUpEvidence.GetLatestSecondFactorUtcAsync(_mfaEvidence, user, HttpContext, _mfaPolicy, DepartmentId,
						Resgrid.Model.Security.MfaMethodScope.SecurityChange, cancellationToken, excludeFederated: true) == null)
				{
					ModelState.AddModelError(string.Empty, _secLocalizer["SecurityPolicyFederatedNeedsResgridMfa"].Value);
					return View(model);
				}
			}

			await _ssoService.SaveSecurityPolicyAsync(policy, UserId, cancellationToken);
			TempData["PolicySuccess"] = _secLocalizer["SecurityPolicySaveSuccess"].Value;
			return RedirectToAction("SecurityPolicy");
		}

		// -- Provider step-up mapping (passkey plan section 7.8) --------------

		[HttpGet]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> FederatedMfa(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			var config = await ActiveSsoConfigAsync(cancellationToken);
			var model = new FederatedMfaEditView();
			model.CopyFrom(Resgrid.Model.Security.FederatedMfaMapping.Parse(config?.FederatedMfaMappingJson));
			await DescribeFederatedMfaAsync(model, config);

			return View(model);
		}

		/// <summary>
		/// Saves, or with <paramref name="command"/> "remove" removes, the active SSO configuration's provider step-up mapping.
		/// Managing member only. Every change advances the mapping version, so the new mapping counts only after its own test,
		/// and provider step-up cannot approve a change to what counts as provider step-up.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5, MethodScope = Resgrid.Model.Security.MfaMethodScope.SecurityChange)]
		public async Task<IActionResult> FederatedMfa(FederatedMfaEditView model, string command, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return RedirectToAction("Index");

			model ??= new FederatedMfaEditView();
			var config = await ActiveSsoConfigAsync(cancellationToken);
			await DescribeFederatedMfaAsync(model, config);
			if (config == null)
				return View(model);

			if (!model.CanChange)
			{
				ModelState.AddModelError(string.Empty, _secLocalizer["SecurityPolicyMfaMethodsManagingMemberOnly"].Value);
				return View(model);
			}

			var user = await _userManager.GetUserAsync(User);
			if (await StepUpEvidence.GetLatestSecondFactorUtcAsync(_mfaEvidence, user, HttpContext, _mfaPolicy, DepartmentId,
					Resgrid.Model.Security.MfaMethodScope.SecurityChange, cancellationToken, excludeFederated: true) == null)
			{
				ModelState.AddModelError(string.Empty, _secLocalizer["FederatedMfaNeedsResgridMfa"].Value);
				return View(model);
			}

			var removing = string.Equals(command, "remove", StringComparison.Ordinal);
			string mappingJson = null;
			if (!removing)
			{
				var mapping = model.ToMapping();
				var problem = Resgrid.Model.Security.FederatedMfaMapping.Validate(mapping, (SsoProviderType)config.SsoProviderType);
				if (problem != null)
				{
					ModelState.AddModelError(string.Empty, string.Format(_secLocalizer["FederatedMfaInvalid"].Value, problem));
					return View(model);
				}
				mappingJson = mapping.Serialize();
			}

			if (!string.Equals(config.FederatedMfaMappingJson ?? string.Empty, mappingJson ?? string.Empty, StringComparison.Ordinal))
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				config.FederatedMfaMappingJson = mappingJson;
				config.UpdatedByUserId = UserId;
				var saved = await _ssoService.SaveSsoConfigAsync(config, department.Code, cancellationToken);

				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Website,
					Type = (int)SystemAuditTypes.FederatedMfaMappingChanged,
					UserId = UserId,
					Username = UserName,
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					Data = removing
						? $"Provider step-up mapping for SSO configuration {config.DepartmentSsoConfigId} removed (now version {saved?.FederatedMfaMappingVersion})."
						: $"Provider step-up mapping for SSO configuration {config.DepartmentSsoConfigId} saved as version {saved?.FederatedMfaMappingVersion}; " +
						  "it counts once it passes its test."
				}, cancellationToken);
			}

			TempData["FederatedMfaSuccess"] = _secLocalizer[removing ? "FederatedMfaRemoved" : "FederatedMfaSaved"].Value;
			return RedirectToAction("FederatedMfa");
		}

		// -- Private helpers --------------------------------------------------

		private async Task<DepartmentSsoConfig> ActiveSsoConfigAsync(CancellationToken cancellationToken) =>
			(await _ssoService.GetSsoConfigsForDepartmentAsync(DepartmentId, cancellationToken))?.FirstOrDefault(c => c.IsEnabled);

		/// <summary>The stored mapping's state and who may change it, always from the server.</summary>
		private async Task DescribeFederatedMfaAsync(FederatedMfaEditView model, DepartmentSsoConfig config)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CanChange = department != null && department.ManagingUserId == UserId;
			model.ProviderStepUpAvailable = _passkeyGates.ProviderStepUpEnabled;
			model.HasActiveSsoConfig = config != null;
			model.IsOidc = config?.SsoProviderType == (int)SsoProviderType.Oidc;
			model.HasMapping = !string.IsNullOrWhiteSpace(config?.FederatedMfaMappingJson);
			model.MappingVersion = config?.FederatedMfaMappingVersion ?? 0;
			model.Effective = Resgrid.Model.Security.FederatedMfaMapping.IsTested(config);
			model.TestedOnUtc = model.Effective ? config.FederatedMfaTestedOnUtc : null;
		}

		/// <summary>Who may change the method switches, and which methods this deployment offers yet (never from the client).</summary>
		private async Task DescribeMethodSwitchesAsync(SecurityPolicyEditView model)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CanChangeMethodSwitches = department != null && department.ManagingUserId == UserId;
			model.PasskeysAvailable = _passkeyGates.LoginAcceptanceEnabled || _passkeyGates.AdpAcceptanceEnabled;
			model.ResponderApprovalAvailable = _passkeyGates.ResponderApprovalEnabled;
			model.ProviderStepUpAvailable = _passkeyGates.ProviderStepUpEnabled;
			model.SharedDeviceModeAvailable = _passkeyGates.SharedDeviceModeEnabled;
			model.MaxSharedIdleLockMinutes = SharedSessionRules.MaxIdleLockMinutes;
			model.MaxSharedShiftHours = SharedSessionRules.MaxShiftHours;
		}

		private static void CopyMethodSwitches(DepartmentSecurityPolicy policy, SecurityPolicyEditView model)
		{
			model.AllowPasskeysForLoginMfa = policy.AllowPasskeysForLoginMfa;
			model.AllowPasskeysForAdp = policy.AllowPasskeysForAdp;
			model.AllowFederatedMfaForLoginMfa = policy.AllowFederatedMfaForLoginMfa;
			model.AllowFederatedMfaForAdp = policy.AllowFederatedMfaForAdp;
			model.AllowResponderApproval = policy.AllowResponderApproval;
			model.AcceptRecentLoginMfaForAdp = policy.AcceptRecentLoginMfaForAdp;
			model.AcceptRecentUnlockMfaForAdp = policy.AcceptRecentUnlockMfaForAdp;
			model.SharedIdleLockMinutes = policy.SharedIdleLockMinutes;
			model.SharedShiftHours = policy.SharedShiftHours;
			var required = (SharedModeApps)policy.SharedModeRequiredApps;
			model.RequireSharedModeForUnit = required.HasFlag(SharedModeApps.Unit);
			model.RequireSharedModeForCommand = required.HasFlag(SharedModeApps.Command);
			model.RequireSharedModeForDispatch = required.HasFlag(SharedModeApps.Dispatch);
		}

		private SelectList BuildDataClassificationList(int selected) =>
			new SelectList(new[]
			{
				new { Id = 0, Name = _secLocalizer["SecurityPolicyDataClassUnclassified"].Value },
				new { Id = 1, Name = _secLocalizer["SecurityPolicyDataClassCui"].Value },
				new { Id = 2, Name = _secLocalizer["SecurityPolicyDataClassConfidential"].Value }
			}, "Id", "Name", selected);

		private SelectList BuildProviderTypeList(string selected) =>
			new SelectList(new[]
			{
				new { Id = "oidc", Name = _secLocalizer["SsoEditProviderTypeOidcOption"].Value },
				new { Id = "saml2", Name = _secLocalizer["SsoEditProviderTypeSamlOption"].Value }
			}, "Id", "Name", selected);

		private void ValidateSsoProviderConfiguration(SsoConfigEditView model, SsoProviderType providerType, bool hasStoredIdpCertificate)
		{
			if (providerType == SsoProviderType.Oidc)
			{
				if (string.IsNullOrWhiteSpace(model.ClientId))
					ModelState.AddModelError("ClientId", _secLocalizer["SsoErrorOidcClientIdRequired"].Value);

				if (!Uri.TryCreate(model.Authority, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
					ModelState.AddModelError("Authority", _secLocalizer["SsoErrorOidcAuthorityInvalid"].Value);

				return;
			}

			if (!hasStoredIdpCertificate && string.IsNullOrWhiteSpace(model.IdpCertificate))
				ModelState.AddModelError("IdpCertificate", _secLocalizer["SsoErrorSamlCertificateRequired"].Value);

			if (!string.IsNullOrWhiteSpace(model.IdpSsoUrl) &&
				(!Uri.TryCreate(model.IdpSsoUrl, UriKind.Absolute, out var idpSsoUrl) || idpSsoUrl.Scheme != Uri.UriSchemeHttps))
				ModelState.AddModelError("IdpSsoUrl", _secLocalizer["SsoErrorIdpSsoUrlInvalid"].Value);
		}

		/// <summary>The redirect URI a department registers with its OIDC IdP for brokered sign-in (plan section 7.7.2 item 7).</summary>
		private static string OidcBrokerRedirectUri(string apiBase) => $"{apiBase?.TrimEnd('/')}{Config.SsoConfig.OidcCallbackPath}";

		private async Task PopulateSsoEditViewContextAsync(SsoConfigEditView model)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var apiBase = Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			model.ProviderTypes = BuildProviderTypeList(model.ProviderType);
			model.RankList = await BuildRankListAsync(model.DefaultRankId);
			model.ApiBaseUrl = apiBase;
			model.OidcBrokerRedirectUri = OidcBrokerRedirectUri(apiBase);
			model.AcsUrl = $"{apiBase}{Config.SsoConfig.SamlAcsPath}?departmentToken={Uri.EscapeDataString(_encryptionService.Encrypt($"{department.DepartmentId}:{department.Code}"))}";
		}

		private async Task<SelectList> BuildRankListAsync(int? selectedRankId)
		{
			// Ranks are not currently implemented as a standalone service � return empty with placeholder
			await Task.CompletedTask;
			return new SelectList(
				new[] { new { Id = (int?)null, Name = _secLocalizer["SsoEditDefaultRankNone"].Value } },
				"Id", "Name", selectedRankId);
		}

		#endregion SSO / SCIM Management
	}
}
