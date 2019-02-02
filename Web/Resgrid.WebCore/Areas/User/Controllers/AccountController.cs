using System;
using System.Web.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Account;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

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

		public AccountController(IDepartmentsService departmentsService, IUsersService usersService, IActionLogsService actionLogsService,
			IEmailService emailService, IUserProfileService userProfileService, IDeleteService deleteService, IAuthorizationService authorizationService,
			ILimitsService limitsService, IPersonnelRolesService personnelRolesService)
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
		}
		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Roles = SystemRoles.Users)]
		public IActionResult DeleteAccount()
		{
			DeleteAccountModel model = new DeleteAccountModel();

			return View(model);
		}
	}
}