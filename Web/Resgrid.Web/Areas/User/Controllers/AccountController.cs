using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Account;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class AccountController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly ILimitsService _limitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEmailService _emailService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDeleteService _deleteService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ISystemAuditsService _systemAuditsService;

		public AccountController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IEmailService emailService, IUserProfileService userProfileService, IDeleteService deleteService, IAuthorizationService authorizationService,
			ILimitsService limitsService, IPersonnelRolesService personnelRolesService, ISystemAuditsService systemAuditsService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_emailService = emailService;
			_userProfileService = userProfileService;
			_deleteService = deleteService;
			_authorizationService = authorizationService;
			_limitsService = limitsService;
			_personnelRolesService = personnelRolesService;
			_systemAuditsService = systemAuditsService;
		}
		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Roles = SystemRoles.Users)]
		public async Task<IActionResult> DeleteAccount()
		{
			DeleteAccountModel model = new DeleteAccountModel();

			var allDepartments = await _departmentsService.GetAllDepartmentsForUserAsync(UserId);

			foreach (var dm in allDepartments)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(dm.DepartmentId);

				if (department != null && department.ManagingUserId == UserId)
					model.IsDepartmentOwner = true;
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Roles = SystemRoles.Users)]
		public async Task<IActionResult> DeleteAccount(DeleteAccountModel model, CancellationToken cancellationToken)
		{
			if (model.AreYouSure == false)
				ModelState.AddModelError("AreYouSure", "You need to confirm the delete.");

			if (ModelState.IsValid)
			{
				var user = _usersService.GetUserById(UserId);
				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Website,
					Type = (int)SystemAuditTypes.AccountDeletionRequested,
					DepartmentId = DepartmentId,
					UserId = UserId,
					Username = user?.UserName,
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					Data = $"Account deletion requested. {Request.Headers["User-Agent"]}"
				}, cancellationToken);

				await _deleteService.DeleteUserAccountAsync(DepartmentId, UserId, UserId, IpAddressHelper.GetRequestIP(Request, true), $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);
				return RedirectToAction("LogOff", "Account", new { area = "" });
			}

			return View(model);
		}
	}
}
