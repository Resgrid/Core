using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Reports.Units;
using Resgrid.Web.Areas.User.Models.Units;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Resgrid.WebCore.Areas.User.Models.Units;
using Resgrid.WebCore.Areas.User.Models;
using Resgrid.WebCore.Areas.User.Models.Personnel;
using System.Web;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class UnitsController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly ILimitsService _limitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallsService _callsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly IGeoService _geoService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly INovuProvider _novuProvider;
		private readonly IMappingService _mappingService;
		private readonly IUserDefinedFieldsService _userDefinedFieldsService;
		private readonly IUdfRenderingService _udfRenderingService;

		public UnitsController(IDepartmentsService departmentsService, IUsersService usersService, IUnitsService unitsService, Model.Services.IAuthorizationService authorizationService,
			ILimitsService limitsService, IDepartmentGroupsService departmentGroupsService, ICallsService callsService, IEventAggregator eventAggregator, ICustomStateService customStateService,
			IGeoService geoService, IDepartmentSettingsService departmentSettingsService, IGeoLocationProvider geoLocationProvider, INovuProvider novuProvider, IMappingService mappingService,
			IUserDefinedFieldsService userDefinedFieldsService, IUdfRenderingService udfRenderingService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_limitsService = limitsService;
			_departmentGroupsService = departmentGroupsService;
			_callsService = callsService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_geoService = geoService;
			_departmentSettingsService = departmentSettingsService;
			_geoLocationProvider = geoLocationProvider;
			_novuProvider = novuProvider;
			_mappingService = mappingService;
			_userDefinedFieldsService = userDefinedFieldsService;
			_udfRenderingService = udfRenderingService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> Index()
		{
			var model = new UnitsIndexView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CanUserAddUnit = await _limitsService.CanDepartmentAddNewUnit(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			model.Units = new List<Unit>();
			var units = await _unitsService.GetUnitsForDepartmentUnlimitedAsync(DepartmentId);
			model.States = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			model.UnitStatuses = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId);
			model.UnitCustomStates = new Dictionary<int, CustomState>();

			foreach (var unit in units)
			{
				if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
					continue;

				var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);
				if (type != null && type.CustomStatesId.HasValue)
				{
					var customStates = await _customStateService.GetCustomSateByIdAsync(type.CustomStatesId.Value);

					if (customStates != null)
					{
						model.UnitCustomStates.Add(unit.UnitId, customStates);
					}
				}

				model.Units.Add(unit);
			}

			if (model.Department.IsUserAnAdmin(UserId))
				model.IsUserAdminOrGroupAdmin = true;
			else
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

				foreach (var group in groups)
				{
					if (group.IsUserGroupAdmin(UserId))
					{
						model.IsUserAdminOrGroupAdmin = true;
						break;
					}
				}
			}

			List<BSTreeModel> trees = new List<BSTreeModel>();
			var tree0 = new BSTreeModel();
			tree0.id = "TreeGroup_-1";
			tree0.text = "All Units";
			tree0.icon = "";
			trees.Add(tree0);

			var tree1 = new BSTreeModel();
			tree1.id = "TreeGroup_0";
			tree1.text = "Ungrouped Units";
			tree1.icon = "";
			trees.Add(tree1);

			if (model.Groups != null && model.Groups.Any())
			{
				foreach (var topLevelGroup in model.Groups.Where(x => !x.ParentDepartmentGroupId.HasValue).ToList())
				{
					var group = new BSTreeModel();
					group.id = $"TreeGroup_{topLevelGroup.DepartmentGroupId.ToString()}";
					group.text = topLevelGroup.Name;
					group.icon = "";

					if (topLevelGroup.Children != null && topLevelGroup.Children.Any())
					{
						foreach (var secondLevelGroup in topLevelGroup.Children)
						{
							var secondLevelGroupTree = new BSTreeModel();
							secondLevelGroupTree.id = $"TreeGroup_{secondLevelGroup.DepartmentGroupId.ToString()}";
							secondLevelGroupTree.text = secondLevelGroup.Name;
							secondLevelGroupTree.icon = "";

							group.nodes.Add(secondLevelGroupTree);
						}
					}

					trees.Add(group);
				}
			}
			model.TreeData = Newtonsoft.Json.JsonConvert.SerializeObject(trees);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> UnitStaffing()
		{
			var model = new UnitStaffingView();
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.Users = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);
			model.ActiveRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> UnitStaffing(UnitStaffingView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.Users = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);
			model.ActiveRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);


			if (ModelState.IsValid)
			{
				List<int> unitRoles = (from object key in form.Keys
									   where key.ToString().StartsWith("Role_")
									   select int.Parse(key.ToString().Replace("Role_", ""))).ToList();

				foreach (var unit in model.Units)
				{
					await _unitsService.DeleteActiveRolesForUnitAsync(unit.UnitId, cancellationToken);
				}

				foreach (var unitRole in unitRoles)
				{
					var unitRoleStaffingUserId = form[$"Role_{unitRole}"];

					if (!String.IsNullOrWhiteSpace(unitRoleStaffingUserId))
					{
						var role = await _unitsService.GetRoleByIdAsync(unitRole);

						if (role != null)
						{
							UnitActiveRole activeRole = new UnitActiveRole();
							activeRole.UnitId = role.UnitId;
							activeRole.Role = role.Name;
							activeRole.UserId = unitRoleStaffingUserId;
							activeRole.DepartmentId = DepartmentId;
							activeRole.UpdatedBy = UserId;
							activeRole.UpdatedOn = DateTime.UtcNow;

							await _unitsService.SaveActiveRoleAsync(activeRole, cancellationToken);
						}
					}
				}

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Create)]
		public async Task<IActionResult> NewUnit()
		{
			var model = new NewUnitView();
			model.Unit = new Unit();
			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId));
			model.States = states;

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			var udfDefinition = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit);
			if (udfDefinition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit, isDeptAdmin, isGroupAdmin);
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinition, udfFields, new List<UdfFieldValue>());
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Create)]
		public async Task<IActionResult> NewUnit(NewUnitView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			if ((await _unitsService.GetUnitByNameDepartmentIdAsync(DepartmentId, model.Unit.Name)) != null)
				ModelState.AddModelError("Name", "Unit with that name already exists.");

			var unitRoleNames = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select form[key.ToString()]).ToList();

			var query = unitRoleNames.GroupBy(x => x)
			  .Where(g => g.Count() > 1)
			  .Select(y => new { Element = y.Key, Counter = y.Count() })
			  .ToList();

			if (query.Any(x => x.Counter > 1))
				ModelState.AddModelError("", "Role Names need to be Unique, please ensure each name is not repeated.");

			//if (!model.Unit.StationGroupId.HasValue || model.Unit.StationGroupId.Value == 0)
			//	ModelState.AddModelError("", "You must select a Station Group to assign this unit to.");

			if (ModelState.IsValid)
			{
				model.Unit.DepartmentId = DepartmentId;

				if (model.Unit.StationGroupId.HasValue && model.Unit.StationGroupId.Value == 0)
					model.Unit.StationGroupId = null;

				model.Unit = await _unitsService.SaveUnitAsync(model.Unit, cancellationToken);

				var roles = new List<UnitRole>();
				if (unitRoleNames.Count > 0)
				{
					foreach (var roleName in unitRoleNames)
					{
						var role = new UnitRole();
						role.Name = roleName;
						role.UnitId = model.Unit.UnitId;

						roles.Add(role);
					}
				}

				if (roles.Count > 0)
					await _unitsService.SetRolesForUnitAsync(model.Unit.UnitId, roles, cancellationToken);

				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				await _novuProvider.CreateUnitSubscriber(model.Unit.UnitId, department.Code, DepartmentId, model.Unit.Name, null);

				// Detect whether the UDF section was included in this POST via the hidden "_exists" sentinel
				// keys emitted by UdfRenderingService. Using the sentinel rather than value-keys alone ensures
				// we still call SaveFieldValuesForEntityAsync even when every visible field was cleared to an
				// empty string (so the service can delete existing/stale values).
				bool udfSectionWasPosted = form.Keys.Any(k => k.StartsWith("udf_") && k.EndsWith("_exists"));

				if (udfSectionWasPosted)
				{
					var udfValues = form.Keys
						.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
						.Select(k => new UdfFieldValue
						{
							UdfFieldId = k.Substring(4),
							Value = form[k]
						}).ToList();

					bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
					bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
					var udfValidationErrors = await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Unit, model.Unit.UnitId.ToString(), udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);

					if (udfValidationErrors != null && udfValidationErrors.Count > 0)
					{
						foreach (var kvp in udfValidationErrors)
						{
							foreach (var errorMessage in kvp.Value)
								ModelState.AddModelError(kvp.Key, errorMessage);
						}
					}
				}

				if (!ModelState.IsValid)
				{
					// UDF validation failed — re-display the form with errors.
					var udfDefinitionOnError = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit);
					if (udfDefinitionOnError != null)
					{
						bool isDeptAdminOnError = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
						bool isGroupAdminOnError = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
						var udfFieldsOnError = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit, isDeptAdminOnError, isGroupAdminOnError);
						var submittedUdfValues = form.Keys
							.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
							.Select(k => new UdfFieldValue
							{
								UdfFieldId = k.Substring(4),
								Value = form[k]
							}).ToList();
						model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinitionOnError, udfFieldsOnError, submittedUdfValues);
					}

					return View(model);
				}

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.UnitAdded;
				auditEvent.After = model.Unit.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				_eventAggregator.SendMessage<UnitAddedEvent>(new UnitAddedEvent() { DepartmentId = DepartmentId, Unit = model.Unit });

				return RedirectToAction("Index");
			}

			var udfDefinition = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit);
			if (udfDefinition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit, isDeptAdmin, isGroupAdmin);
				var submittedUdfValues = form.Keys
					.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
					.Select(k => new UdfFieldValue
					{
						UdfFieldId = k.Substring(4),
						Value = form[k]
					}).ToList();
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinition, udfFields, submittedUdfValues);
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> EditUnit(int unitId)
		{
			var model = new NewUnitView();
			model.Unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				return Unauthorized();

			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			model.UnitRoles = await _unitsService.GetRolesForUnitAsync(unitId);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			await _novuProvider.CreateUnitSubscriber(model.Unit.UnitId, department.Code, DepartmentId, model.Unit.Name, null);

			var udfDefinition = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit);
			if (udfDefinition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFields = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit, isDeptAdmin, isGroupAdmin);
				var udfValues = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Unit, unitId.ToString());
				var visibleFieldIds = udfFields.Select(f => f.UdfFieldId).ToHashSet();
				var filteredValues = (udfValues ?? new List<UdfFieldValue>()).Where(v => visibleFieldIds.Contains(v.UdfFieldId)).ToList();
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinition, udfFields, filteredValues);
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> EditUnit(NewUnitView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, model.Unit.UnitId))
				return Unauthorized();

			var unitCheck = await _unitsService.GetUnitByNameDepartmentIdAsync(DepartmentId, model.Unit.Name);
			if (unitCheck != null && unitCheck.UnitId != model.Unit.UnitId)
				ModelState.AddModelError("Name", "Unit with that name already exists.");

			var unitRoleNames = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select form[key.ToString()]).ToList();

			var query = unitRoleNames.GroupBy(x => x)
									  .Where(g => g.Count() > 1)
									  .Select(y => new { Element = y.Key, Counter = y.Count() })
									  .ToList();

			if (query.Any(x => x.Counter > 1))
				ModelState.AddModelError("", "Role Names need to be Unique, please ensure each name is not repeated.");

			//if (!model.Unit.StationGroupId.HasValue || model.Unit.StationGroupId.Value == 0)
			//	ModelState.AddModelError("", "You must select a Station Group to assign this unit to.");

			var unit = await _unitsService.GetUnitByIdAsync(model.Unit.UnitId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.UnitChanged;
			auditEvent.Before = unit.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

			unit.Name = model.Unit.Name;
			unit.Type = model.Unit.Type;

			if (model.Unit.StationGroupId.HasValue && model.Unit.StationGroupId.Value != 0)
				unit.StationGroupId = model.Unit.StationGroupId;
			else
				unit.StationGroupId = null;

			if (ModelState.IsValid)
			{
				await _unitsService.SaveUnitAsync(unit, cancellationToken);

				var roles = new List<UnitRole>();
				if (unitRoleNames.Count > 0)
				{
					foreach (var roleName in unitRoleNames)
					{
						var role = new UnitRole();
						role.Name = roleName;
						role.UnitId = unit.UnitId;

						roles.Add(role);
					}
				}

				if (roles.Count > 0)
					await _unitsService.SetRolesForUnitAsync(unit.UnitId, roles, cancellationToken);
				else
					await _unitsService.ClearRolesForUnitAsync(unit.UnitId, cancellationToken);

				// Detect whether the UDF section was included in this POST via the hidden "_exists" sentinel
				// keys emitted by UdfRenderingService. Using the sentinel rather than value-keys alone ensures
				// we still call SaveFieldValuesForEntityAsync even when every visible field was cleared to an
				// empty string (so the service can delete existing/stale values).
				bool udfSectionWasPosted = form.Keys.Any(k => k.StartsWith("udf_") && k.EndsWith("_exists"));

				if (udfSectionWasPosted)
				{
					var udfValues = form.Keys
						.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
						.Select(k => new UdfFieldValue
						{
							UdfFieldId = k.Substring(4),
							Value = form[k]
						}).ToList();

					bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
					bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
					var udfValidationErrors = await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Unit, unit.UnitId.ToString(), udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);

					if (udfValidationErrors != null && udfValidationErrors.Count > 0)
					{
						foreach (var kvp in udfValidationErrors)
						{
							foreach (var errorMessage in kvp.Value)
								ModelState.AddModelError(kvp.Key, errorMessage);
						}
					}
				}

				if (!ModelState.IsValid)
				{
					// UDF validation failed — re-display the form with errors.
					model.Unit = await _unitsService.GetUnitByIdAsync(model.Unit.UnitId);
					model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

					var groupsOnUdfError = new List<DepartmentGroup>();
					groupsOnUdfError.Add(new DepartmentGroup { Name = "No Station" });
					groupsOnUdfError.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
					model.Stations = groupsOnUdfError;

					model.UnitRoles = await _unitsService.GetRolesForUnitAsync(model.Unit.UnitId);

					var udfDefinitionOnError = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit);
					if (udfDefinitionOnError != null)
					{
						bool isDeptAdminOnError = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
						bool isGroupAdminOnError = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
						var udfFieldsOnError = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit, isDeptAdminOnError, isGroupAdminOnError);
						var submittedUdfValues = form.Keys
							.Where(k => k.StartsWith("udf_") && !k.EndsWith("_exists"))
							.Select(k => new UdfFieldValue
							{
								UdfFieldId = k.Substring(4),
								Value = form[k]
							}).ToList();
						model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinitionOnError, udfFieldsOnError, submittedUdfValues);
					}

					return View(model);
				}

				auditEvent.After = unit.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index");
			}

			model.Unit = await _unitsService.GetUnitByIdAsync(model.Unit.UnitId);

			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			model.UnitRoles = await _unitsService.GetRolesForUnitAsync(model.Unit.UnitId);

			var udfDefinitionEdit = await _userDefinedFieldsService.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit);
			if (udfDefinitionEdit != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId);
				var udfFieldsEdit = await _userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Unit, isDeptAdmin, isGroupAdmin);
				var udfValuesEdit = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Unit, model.Unit.UnitId.ToString());
				var visibleFieldIds = udfFieldsEdit.Select(f => f.UdfFieldId).ToHashSet();
				var filteredValues = (udfValuesEdit ?? new List<UdfFieldValue>()).Where(v => visibleFieldIds.Contains(v.UdfFieldId)).ToList();
				model.UdfFormHtml = _udfRenderingService.GenerateHtmlFormFields(udfDefinitionEdit, udfFieldsEdit, filteredValues);
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitState(int unitId, int stateType, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit != null)
			{
				await _unitsService.SetUnitStateAsync(unit.UnitId, stateType, DepartmentId, cancellationToken);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitStateWithDest(int unitId, int stateType, int type, int destination, string note, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit != null)
			{
				var state = new UnitState();
				state.UnitId = unit.UnitId;
				state.LocalTimestamp = DateTimeHelpers.GetLocalDateTime(DateTime.UtcNow, department.TimeZone);
				state.State = stateType;
				state.Timestamp = DateTime.UtcNow;

				if (destination > 0)
				{
					if (!await IsValidDestinationAsync(destination, type))
						return BadRequest();

					state.DestinationId = destination;
					state.DestinationType = type;
				}

				if (!String.IsNullOrWhiteSpace(note))
					state.Note = HttpUtility.UrlDecode(note);

				try
				{
					var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitStateForMultiple(string unitIds, int stateType, int type, int destination, string note, CancellationToken cancellationToken)
		{
			if (!String.IsNullOrWhiteSpace(unitIds) && unitIds.Split(char.Parse("|")).Any())
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

				foreach (var unitId in unitIds.Split(char.Parse("|")))
				{
					var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

					if (!await _authorizationService.CanUserViewUnitAsync(UserId, unit.UnitId))
						return Unauthorized();


					var state = new UnitState();
					state.UnitId = int.Parse(unitId);
					state.LocalTimestamp = DateTimeHelpers.GetLocalDateTime(DateTime.UtcNow, department.TimeZone);
					state.State = stateType;
					state.Timestamp = DateTime.UtcNow;

					if (destination > 0)
					{
						if (!await IsValidDestinationAsync(destination, type))
							return BadRequest();

						state.DestinationId = destination;
						state.DestinationType = type;
					}

					if (!String.IsNullOrWhiteSpace(note))
						state.Note = HttpUtility.UrlDecode(note);

					try
					{
						var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitStateWithDestForMultiple(string unitIds, int stateType, int type, int destination, CancellationToken cancellationToken)
		{
			if (!String.IsNullOrWhiteSpace(unitIds) && unitIds.Split(char.Parse("|")).Any())
			{
				foreach (var unitId in unitIds.Split(char.Parse("|")))
				{
					var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

					if (!await _authorizationService.CanUserViewUnitAsync(UserId, unit.UnitId))
						return Unauthorized();

					var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

					var state = new UnitState();
					state.UnitId = unit.UnitId;
					state.LocalTimestamp = DateTimeHelpers.GetLocalDateTime(DateTime.UtcNow, department.TimeZone);
					state.State = stateType;
					state.Timestamp = DateTime.UtcNow;
					state.DestinationId = destination;
					state.DestinationType = type;

					if (!await IsValidDestinationAsync(destination, type))
						return BadRequest();

					try
					{
						var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId, cancellationToken);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Delete)]
		public async Task<IActionResult> DeleteUnit(int unitId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				return Unauthorized();

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.UnitRemoved;
			auditEvent.Before = unit.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _unitsService.DeleteUnitAsync(unitId, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.UnitLog_Create)]
		public async Task<IActionResult> AddLog(int unitId)
		{
			var model = new AddLogView();
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit == null)
				return Unauthorized();

			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			model.Log = new UnitLog();
			model.Log.Timestamp = DateTime.UtcNow;
			model.Log.UnitId = unit.UnitId;
			model.UnitName = unit.Name;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.UnitLog_Create)]
		public async Task<IActionResult> AddLog(AddLogView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, model.Log.UnitId))
				return Unauthorized();

			if (ModelState.IsValid)
			{
				model.Log.Narrative = System.Net.WebUtility.HtmlDecode(model.Log.Narrative);
				await _unitsService.SaveUnitLogAsync(model.Log, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.UnitLog_View)]
		public async Task<IActionResult> ViewLogs(int unitId)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var model = new ViewLogsView();
			model.Unit = await _unitsService.GetUnitByIdAsync(unitId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (model.Unit == null)
				return Unauthorized();

			model.Logs = await _unitsService.GetLogsForUnitAsync(model.Unit.UnitId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> ViewEvents(int unitId)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var model = new ViewLogsView();
			model.Unit = await _unitsService.GetUnitByIdAsync(unitId);
			model.OSMKey = Config.MappingConfig.OSMKey;
			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var gpsCoordinates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			double? centerLat = null;
			double? centerLon = null;

			if (!String.IsNullOrWhiteSpace(gpsCoordinates))
			{
				string[] coordinates = gpsCoordinates.Split(char.Parse(","));

				if (coordinates.Count() == 2)
				{
					double newLat;
					double newLon;
					if (double.TryParse(coordinates[0], out newLat) && double.TryParse(coordinates[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue && !centerLon.HasValue && address != null)
			{
				string coordinates = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", address.Address1,
					address.City, address.State, address.PostalCode));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue && !centerLon.HasValue && department.Address != null)
			{
				string coordinates = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", department.Address.Address1,
					department.Address.City,
					department.Address.State,
					department.Address.PostalCode));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue || !centerLon.HasValue)
			{
				centerLat = 39.14086268299356;
				centerLon = -119.7583809782715;
			}

			model.CenterLat = centerLat.Value;
			model.CenterLon = centerLon.Value;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> GenerateReport(IFormCollection form)
		{
			var eventIds = new List<int>();
			foreach (var key in form.Keys)
			{
				if (key.ToString().StartsWith("selectEvent_"))
				{
					var eventId = int.Parse(key.ToString().Replace("selectEvent_", ""));
					eventIds.Add(eventId);
				}
			}

			var model = new UnitEventsReportView();
			model.Rows = new List<UnitEventJson>();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var pois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);

			foreach (var eventId in eventIds)
			{
				var eventJson = new UnitEventJson();
				var eventRecord = await _unitsService.GetUnitStateByIdAsync(eventId);

				model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

				eventJson.UnitName = eventRecord.Unit.Name;
				eventJson.State = StringHelpers.GetDescription(((UnitStateTypes)eventRecord.State));
				eventJson.Timestamp = eventRecord.Timestamp.TimeConverterToString(model.Department).ToString();
				eventJson.Note = eventRecord.Note;
				var customState = await _customStateService.GetCustomUnitStateAsync(eventRecord);
				var destination = DestinationResolutionHelper.Resolve(eventRecord.DestinationId, eventRecord.DestinationType, customState?.DetailType, activeCalls, stations, pois);
				eventJson.DestinationName = destination.Name;

				if (eventRecord.LocalTimestamp.HasValue)
					eventJson.LocalTimestamp = eventRecord.LocalTimestamp.Value.ToString();

				if (eventRecord.Latitude.HasValue)
					eventJson.Latitude = eventRecord.Latitude.Value.ToString();

				if (eventRecord.Longitude.HasValue)
					eventJson.Longitude = eventRecord.Longitude.Value.ToString();

				model.Rows.Add(eventJson);
			}

			return View("~/Areas/User/Views/Reports/UnitEventsReport.cshtml", model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> ClearAllUnitEvents(ViewLogsView model, CancellationToken cancellationToken)
		{
			if (model.ConfirmClearAll)
				await _unitsService.DeleteStatesForUnitAsync(model.Unit.UnitId, cancellationToken);

			return RedirectToAction("ViewEvents", new { unitId = model.Unit.UnitId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitEvents(int unitId)
		{
			var unitEvents = new List<UnitEventJson>();

			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var events = await _unitsService.GetAllStatesForUnitAsync(unitId);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var pois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);

			foreach (var e in events)
			{
				var unitEvent = new UnitEventJson();
				unitEvent.EventId = e.UnitStateId;
				unitEvent.UnitId = e.UnitId;
				unitEvent.UnitName = e.Unit.Name;

				var customState = await _customStateService.GetCustomUnitStateAsync(e);

				unitEvent.State = customState?.ButtonText;
				unitEvent.Timestamp = e.Timestamp.TimeConverterToString(department);
				unitEvent.Note = e.Note;

				var destination = DestinationResolutionHelper.Resolve(e.DestinationId, e.DestinationType, customState?.DetailType, activeCalls, stations, pois);
				unitEvent.DestinationName = destination.Name;


				if (e.LocalTimestamp.HasValue)
					unitEvent.LocalTimestamp = e.LocalTimestamp.Value.ToString();

				if (e.Latitude.HasValue)
					unitEvent.Latitude = e.Latitude.Value.ToString();

				if (e.Longitude.HasValue)
					unitEvent.Longitude = e.Longitude.Value.ToString();

				if (e.Accuracy.HasValue)
					unitEvent.Accuracy = e.Accuracy.Value.ToString();

				if (e.Altitude.HasValue)
					unitEvent.Altitude = e.Altitude.Value.ToString();

				if (e.AltitudeAccuracy.HasValue)
					unitEvent.AltitudeAccuracy = e.AltitudeAccuracy.Value.ToString();

				if (e.Speed.HasValue)
					unitEvent.Speed = e.Speed.Value.ToString();

				if (e.Heading.HasValue)
					unitEvent.Heading = e.Heading.Value.ToString();

				unitEvents.Add(unitEvent);
			}

			return Json(unitEvents);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public PartialViewResult SmallUnitsGrid()
		{
			return PartialView("_SmallUnitsGridPartial");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetUnits()
		{
			var units = new List<UnitJson>();
			var savedUnits = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);

			foreach (var unit in savedUnits)
			{
				var unitJson = new UnitJson();
				unitJson.UnitId = unit.UnitId;
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				units.Add(unitJson);
			}

			return Json(units);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetUnitsForGroup(int groupId)
		{
			var units = new List<UnitJson>();
			var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);

			if (group == null || group.DepartmentId != DepartmentId)
				return Unauthorized();

			var savedUnits = await _unitsService.GetAllUnitsForGroupAsync(groupId);

			foreach (var unit in savedUnits)
			{
				var unitJson = new UnitJson();
				unitJson.UnitId = unit.UnitId;
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				units.Add(unitJson);
			}

			return Json(units);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetUnitsAndRolesForGroup(int groupId)
		{
			var units = new List<UnitJson>();
			var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);

			if (group == null || group.DepartmentId != DepartmentId)
				return Unauthorized();

			var savedUnits = await _unitsService.GetAllUnitsForGroupAsync(groupId);

			foreach (var unit in savedUnits)
			{
				var unitJson = new UnitJson();
				unitJson.UnitId = unit.UnitId;
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.GroupId = unit.StationGroupId.GetValueOrDefault();

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var unitRoles = await _unitsService.GetRolesForUnitAsync(unit.UnitId);

				if (unitRoles != null && unitRoles.Any())
				{
					unitJson.Roles = new List<WebCore.Areas.User.Models.Units.UnitRoleJson>();

					foreach (var unitRole in unitRoles)
					{
						var role = new WebCore.Areas.User.Models.Units.UnitRoleJson();
						role.UnitId = unitRole.UnitId;
						role.UnitRoleId = unitRole.UnitRoleId;
						role.Name = unitRole.Name;

						unitJson.Roles.Add(role);
					}
				}

				units.Add(unitJson);
			}

			return Json(units);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitTypes()
		{
			var unitTypes = new List<UnitTypeJson>();
			var types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			foreach (var type in types)
			{
				var unitType = new UnitTypeJson();
				unitType.Id = type.UnitTypeId;
				unitType.Name = type.Type;

				unitTypes.Add(unitType);
			}

			return Json(unitTypes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetUnitsList()
		{
			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			foreach (var unit in units)
			{
				if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
					continue;

				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroupId.HasValue)
				{
					var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unit.StationGroupId.Value);

					if (group != null)
						unitJson.Station = group.Name;
				}

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = await CustomStatesHelper.GetCustomUnitState(state);

					if (customState != null)
					{
						unitJson.StateId = state.State;
						unitJson.State = customState.ButtonText;
						unitJson.StateColor = customState.ButtonColor;
						unitJson.TextColor = customState.TextColor;
						unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);
					}
					else
					{
						unitJson.StateId = state.State;
						unitJson.State = "Unknown";
						unitJson.StateColor = "#d1dade";
						unitJson.TextColor = "5E5E5E";
						unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);
					}
				}

				unitsJson.Add(unitJson);
			}

			return Json(unitsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetUnitsForCallGrid(string callLat, string callLong)
		{
			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var unit in units)
			{
				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = await CustomStatesHelper.GetCustomUnitState(state);

					unitJson.StateId = state.State;
					unitJson.State = customState.ButtonText;
					unitJson.StateColor = customState.ButtonColor;
					unitJson.TextColor = customState.TextColor;
					unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);


					if (String.IsNullOrWhiteSpace(callLat) || String.IsNullOrWhiteSpace(callLong))
						unitJson.Eta = "N/A";
					else
					{
						var location = await _unitsService.GetLatestUnitLocationAsync(state.UnitId, state.Timestamp);

						if (location != null)
						{
							var eta = await _geoService.GetEtaInSecondsAsync($"{location.Latitude},{location.Longitude}", String.Format("{0},{1}", callLat, callLong));

							if (eta > 0)
								unitJson.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
							else
								unitJson.Eta = "N/A";
						}
						else if (!String.IsNullOrWhiteSpace(state.GeoLocationData))
						{
							var eta = await _geoService.GetEtaInSecondsAsync(state.GeoLocationData, String.Format("{0},{1}", callLat, callLong));

							if (eta > 0)
								unitJson.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
							else
								unitJson.Eta = "N/A";
						}
						else
							unitJson.Eta = "N/A";
					}
				}

				unitsJson.Add(unitJson);
			}

			return Json(unitsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitStatusHtmlForDropdown(int unitId)
		{
			string buttonHtml = string.Empty;

			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);
			CustomState customStates = null;
			List<CustomStateDetail> activeDetails = null;

			if (type != null && type.CustomStatesId.HasValue)
			{
				customStates = await _customStateService.GetCustomSateByIdAsync(type.CustomStatesId.Value);

				if (customStates != null)
				{
					activeDetails = customStates.GetActiveDetails();
				}
			}

			if (activeDetails == null)
				activeDetails = _customStateService.GetDefaultUnitStatuses();

			StringBuilder sb = new StringBuilder();

			foreach (var state in activeDetails.OrderBy(x => x.Order))
			{
				sb.Append($"<option value='{state.CustomStateDetailId}'>{state.ButtonText}</option>");
			}

			buttonHtml = sb.ToString();


			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitStatusHtmlForDropdownByStateId(int customStateId)
		{
			string buttonHtml = string.Empty;

			CustomState customStates = null;
			List<CustomStateDetail> activeDetails = null;

			if (customStateId > 25)
			{
				customStates = await _customStateService.GetCustomSateByIdAsync(customStateId);

				if (customStates != null)
				{
					activeDetails = customStates.GetActiveDetails();
				}
			}

			if (activeDetails == null)
				activeDetails = _customStateService.GetDefaultUnitStatuses();

			StringBuilder sb = new StringBuilder();

			foreach (var state in activeDetails.OrderBy(x => x.Order))
			{
				sb.Append($"<option value='{state.CustomStateDetailId}'>{state.ButtonText}</option>");
			}

			buttonHtml = sb.ToString();


			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitStatusDestinationHtmlForDropdown(int customStateId, int customStatusDetailId)
		{
			var activeDetails = await GetActiveUnitStatusDetailsAsync(customStateId);
			var state = activeDetails.FirstOrDefault(x => x.CustomStateDetailId == customStatusDetailId);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var destinationPois = await _mappingService.GetDestinationPOIsForDepartmentAsync(DepartmentId);
			var sb = new StringBuilder();

			sb.Append("<option value=''>None</option>");

			if (state != null)
			{
				AppendDestinationOptions(sb, state, activeCalls, stations, destinationPois);
			}

			return Content(sb.ToString());
		}


		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitOptionsDropdown(int unitId)
		{
			string buttonHtml = string.Empty;

			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				return Unauthorized();

			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);

			if (type != null && type.CustomStatesId.HasValue)
			{
				var customStates = await _customStateService.GetCustomSateByIdAsync(type.CustomStatesId.Value);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
				var destinationPois = await _mappingService.GetDestinationPOIsForDepartmentAsync(DepartmentId);
				var activeDetails = customStates.GetActiveDetails();

				buttonHtml = BuildUnitStateMenuHtml(unitId.ToString(CultureInfo.InvariantCulture), unitId, null, "SetUnitState", "SetUnitStateWithDest", activeDetails, activeCalls, stations, destinationPois);
			}

			if (String.IsNullOrWhiteSpace(buttonHtml))
			{
				StringBuilder sb = new StringBuilder();
				sb.Append($"<ul class='dropdown-menu unitStateList_{unitId}'>");
				sb.Append($"<li><a href='{BuildUnitStateHref("SetUnitState", unitId, null, 0)}'>Available</a></li>");
				sb.Append($"<li><a href='{BuildUnitStateHref("SetUnitState", unitId, null, 3)}'>Committed</a></li>");
				sb.Append($"<li><a href='{BuildUnitStateHref("SetUnitState", unitId, null, 1)}'>Delayed</a></li>");
				sb.Append($"<li><a href='{BuildUnitStateHref("SetUnitState", unitId, null, 4)}'>Out Of Service</a></li>");
				sb.Append($"<li><a href='{BuildUnitStateHref("SetUnitState", unitId, null, 2)}'>Unavailable</a></li>");
				sb.Append("</ul>");

				buttonHtml = sb.ToString();
			}

			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitOptionsDropdownForStates(int stateId, string units)
		{
			string buttonHtml = string.Empty;
			var targetUnitIds = ParseUnitIds(units);

			if (stateId > 1)
			{
				var customStates = await _customStateService.GetCustomSateByIdAsync(stateId);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
				var destinationPois = await _mappingService.GetDestinationPOIsForDepartmentAsync(DepartmentId);
				var activeDetails = customStates.GetActiveDetails();

				if (activeDetails.Any())
				{
					buttonHtml = BuildUnitStateMenuHtml(stateId.ToString(CultureInfo.InvariantCulture), null, targetUnitIds, "SetUnitStateForMultiple", "SetUnitStateWithDestForMultiple", activeDetails, activeCalls, stations, destinationPois);
				}
			}

			if (String.IsNullOrWhiteSpace(buttonHtml))
			{
				StringBuilder sb2 = new StringBuilder();
				sb2.Append($"<ul class='dropdown-menu unitStateList_{stateId}'>");
				sb2.Append($"<li><a href='{BuildUnitStateHref("SetUnitStateForMultiple", null, targetUnitIds, 0)}'>Available</a></li>");
				sb2.Append($"<li><a href='{BuildUnitStateHref("SetUnitStateForMultiple", null, targetUnitIds, 3)}'>Committed</a></li>");
				sb2.Append($"<li><a href='{BuildUnitStateHref("SetUnitStateForMultiple", null, targetUnitIds, 1)}'>Delayed</a></li>");
				sb2.Append($"<li><a href='{BuildUnitStateHref("SetUnitStateForMultiple", null, targetUnitIds, 4)}'>Out Of Service</a></li>");
				sb2.Append($"<li><a href='{BuildUnitStateHref("SetUnitStateForMultiple", null, targetUnitIds, 2)}'>Unavailable</a></li>");
				sb2.Append("</ul>");

				buttonHtml = sb2.ToString();
			}

			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetPersonnelForUnitStaffingJson(string search)
		{
			List<PersonGroupRolesJson> personsJson = new List<PersonGroupRolesJson>();

			var users = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);
			var activeRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);

			foreach (var user in users)
			{
				var personJson = new PersonGroupRolesJson();
				personJson.UserId = user.UserId;
				personJson.Name = user.Name;
				personJson.GroupName = user.DepartmentGroupName;
				personJson.Roles = user.RoleNames;

				if (String.IsNullOrWhiteSpace(search) || user.Name.ToLower().Contains(search.ToLower()))
				{
					personsJson.Add(personJson);
				}
			}

			return Json(personsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public async Task<IActionResult> GetActivePersonnelForUnitStaffingRoleJson(string unitId, string activeRole)
		{
			List<PersonGroupRolesJson> personsJson = new List<PersonGroupRolesJson>();

			var users = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);
			var activeRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);

			var role = activeRoles.FirstOrDefault(x => x.UnitId == int.Parse(unitId) && x.Role == activeRole);

			if (role != null)
			{
				var user = users.FirstOrDefault(x => x.UserId == role.UserId);

				if (user != null)
				{
					var personJson = new PersonGroupRolesJson();
					personJson.UserId = user.UserId;
					personJson.Name = user.Name;
					personJson.GroupName = user.DepartmentGroupName;
					personJson.Roles = user.RoleNames;

					personsJson.Add(personJson);
				}
			}

			return Json(personsJson);
		}

		private async Task<List<CustomStateDetail>> GetActiveUnitStatusDetailsAsync(int customStateId)
		{
			if (customStateId > 25)
			{
				var customStates = await _customStateService.GetCustomSateByIdAsync(customStateId);

				if (customStates != null)
					return customStates.GetActiveDetails();
			}

			return _customStateService.GetDefaultUnitStatuses();
		}

		private static void AppendDestinationOptions(StringBuilder sb, CustomStateDetail state, List<Call> activeCalls, List<DepartmentGroup> stations, List<Poi> destinationPois)
		{
			if (state.DetailType.SupportsCalls())
			{
				sb.Append("<optgroup label='Calls'>");
				foreach (var call in activeCalls)
				{
					sb.Append($"<option value='{(int)DestinationEntityTypes.Call}:{call.CallId}'>Call {call.GetIdentifier()}:{call.Name}</option>");
				}
				sb.Append("</optgroup>");
			}

			if (state.DetailType.SupportsStations())
			{
				sb.Append("<optgroup label='Stations'>");
				foreach (var station in stations)
				{
					sb.Append($"<option value='{(int)DestinationEntityTypes.Station}:{station.DepartmentGroupId}'>Station: {station.Name}</option>");
				}
				sb.Append("</optgroup>");
			}

			if (state.DetailType.SupportsPois())
			{
				foreach (var poiGroup in destinationPois.GroupBy(x => !String.IsNullOrWhiteSpace(x.Type?.Name) ? x.Type.Name : "POIs"))
				{
					sb.Append($"<optgroup label='{poiGroup.Key}'>");
					foreach (var poi in poiGroup.OrderBy(x => x.Name).ThenBy(x => x.Address).ThenBy(x => x.Note))
					{
						sb.Append($"<option value='{(int)DestinationEntityTypes.Poi}:{poi.PoiId}'>{GetPoiDisplayText(poi)}</option>");
					}
					sb.Append("</optgroup>");
				}
			}
		}

		private string BuildUnitStateMenuHtml(string cssKey, int? unitId, IReadOnlyCollection<int> unitIds, string actionWithoutDestination, string actionWithDestination, IEnumerable<CustomStateDetail> activeDetails, List<Call> activeCalls, List<DepartmentGroup> stations, List<Poi> destinationPois)
		{
			var sb = new StringBuilder();
			sb.Append($"<ul class='dropdown-menu multi-level unitStateList_{cssKey}'>");

			foreach (var state in activeDetails.OrderBy(x => x.Order))
			{
				var withoutDestinationHref = BuildUnitStateHref(actionWithoutDestination, unitId, unitIds, state.CustomStateDetailId);
				var buttonColor = HttpUtility.HtmlAttributeEncode(state.ButtonColor ?? string.Empty);
				var buttonText = HttpUtility.HtmlEncode(state.ButtonText);

				if (state.DetailType == (int)CustomStateDetailTypes.None)
				{
					sb.Append($"<li><a style='color:{buttonColor};' href='{withoutDestinationHref}'>{buttonText}</a></li>");
					continue;
				}

				sb.Append($"<li class='dropdown-submenu'><a style='color:{buttonColor};' tabindex='-1' href='#'>{buttonText}</a>");
				sb.Append($"<ul class='dropdown-menu unitStateList_{cssKey}'>");
				sb.Append($"<li><a href='{withoutDestinationHref}'>{buttonText}</a></li>");
				sb.Append("<li class='divider'></li>");
				AppendDestinationMenuEntries(sb, unitId, unitIds, actionWithDestination, activeCalls, stations, destinationPois, state);
				sb.Append("</ul>");
				sb.Append("</li>");
			}

			sb.Append("</ul>");
			return sb.ToString();
		}

		private static IReadOnlyCollection<int> ParseUnitIds(string units)
		{
			var parsedUnitIds = new List<int>();

			if (String.IsNullOrWhiteSpace(units))
				return parsedUnitIds;

			foreach (var unitIdValue in units.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(unitIdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedUnitId) &&
					parsedUnitId > 0 &&
					!parsedUnitIds.Contains(parsedUnitId))
				{
					parsedUnitIds.Add(parsedUnitId);
				}
			}

			return parsedUnitIds;
		}

		private static string BuildUnitStateHref(string actionName, int? unitId, IReadOnlyCollection<int> unitIds, int stateType, int? destinationType = null, int? destinationId = null)
		{
			var query = HttpUtility.ParseQueryString(string.Empty);

			if (unitId.HasValue && unitId.Value > 0)
				query["unitId"] = unitId.Value.ToString(CultureInfo.InvariantCulture);

			if (unitIds != null && unitIds.Count > 0)
				query["unitIds"] = string.Join("|", unitIds.Where(x => x > 0).Distinct().Select(x => x.ToString(CultureInfo.InvariantCulture)));

			query["stateType"] = stateType.ToString(CultureInfo.InvariantCulture);

			if (destinationType.HasValue)
				query["type"] = destinationType.Value.ToString(CultureInfo.InvariantCulture);

			if (destinationId.HasValue)
				query["destination"] = destinationId.Value.ToString(CultureInfo.InvariantCulture);

			var queryString = query.ToString();
			var href = $"/User/Units/{actionName}";

			if (!String.IsNullOrWhiteSpace(queryString))
				href = $"{href}?{queryString}";

			return HttpUtility.HtmlAttributeEncode(href);
		}

		private static void AppendDestinationMenuEntries(StringBuilder sb, int? unitId, IReadOnlyCollection<int> unitIds, string actionWithDestination, IEnumerable<Call> activeCalls, IEnumerable<DepartmentGroup> stations, IEnumerable<Poi> destinationPois, CustomStateDetail state)
		{
			if (state.DetailType.SupportsCalls())
			{
				sb.Append("<li class='dropdown-header'>Calls</li>");
				foreach (var call in activeCalls)
				{
					var callHref = BuildUnitStateHref(actionWithDestination, unitId, unitIds, state.CustomStateDetailId, (int)DestinationEntityTypes.Call, call.CallId);
					var callText = HttpUtility.HtmlEncode($"{call.GetIdentifier()}:{call.Name}");
					sb.Append($"<li><a href='{callHref}'>{callText}</a></li>");
				}
			}

			if (state.DetailType.SupportsStations())
			{
				sb.Append("<li class='dropdown-header'>Stations</li>");
				foreach (var station in stations)
				{
					var stationHref = BuildUnitStateHref(actionWithDestination, unitId, unitIds, state.CustomStateDetailId, (int)DestinationEntityTypes.Station, station.DepartmentGroupId);
					var stationText = HttpUtility.HtmlEncode(station.Name);
					sb.Append($"<li><a href='{stationHref}'>{stationText}</a></li>");
				}
			}

			if (state.DetailType.SupportsPois())
			{
				foreach (var poiGroup in destinationPois.GroupBy(x => !String.IsNullOrWhiteSpace(x.Type?.Name) ? x.Type.Name : "POIs"))
				{
					var poiGroupLabel = HttpUtility.HtmlEncode(poiGroup.Key);
					sb.Append($"<li class='dropdown-header'>{poiGroupLabel}</li>");
					foreach (var poi in poiGroup.OrderBy(x => x.Name).ThenBy(x => x.Address).ThenBy(x => x.Note))
					{
						var poiHref = BuildUnitStateHref(actionWithDestination, unitId, unitIds, state.CustomStateDetailId, (int)DestinationEntityTypes.Poi, poi.PoiId);
						var poiText = HttpUtility.HtmlEncode(GetPoiDisplayText(poi));
						sb.Append($"<li><a href='{poiHref}'>{poiText}</a></li>");
					}
				}
			}
		}

		private static string GetPoiDisplayText(Poi poi)
		{
			return PoiDisplayHelper.GetSelectionLabel(poi);
		}

		private async Task<bool> IsValidDestinationAsync(int destinationId, int destinationType)
		{
			var entityType = (DestinationEntityTypes)destinationType;

			switch (entityType)
			{
				case DestinationEntityTypes.Station:
					var station = await _departmentGroupsService.GetGroupByIdAsync(destinationId, false);
					return station != null && station.DepartmentId == DepartmentId;
				case DestinationEntityTypes.Call:
					var call = await _callsService.GetCallByIdAsync(destinationId, false);
					return call != null && call.DepartmentId == DepartmentId;
				case DestinationEntityTypes.Poi:
					return await _mappingService.GetDestinationPOIByIdAsync(DepartmentId, destinationId) != null;
				default:
					return false;
			}
		}
	}
}
