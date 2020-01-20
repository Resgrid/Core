using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Model.Results;
using System;
using Resgrid.Model;

namespace Resgrid.Web.Controllers
{
	public class CoreBridgeController : Controller
	{
		#region Private Members and Constructors
		private readonly UserManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> _userManager;
		private readonly SignInManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> _signInManager;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IEmailService _emailService;
		private readonly IInvitesService _invitesService;
		private readonly IUserProfileService _userProfileService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IAffiliateService _affiliateService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IEmailMarketingProvider _emailMarketingProvider;

		public CoreBridgeController(
						UserManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> userManager, SignInManager<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> signInManager,
						IDepartmentsService departmentsService, IUsersService usersService, IEmailService emailService, IInvitesService invitesService, IUserProfileService userProfileService,
						ISubscriptionsService subscriptionsService, IAffiliateService affiliateService, IEventAggregator eventAggregator, IEmailMarketingProvider emailMarketingProvider)
		{
			_userManager = userManager;
			_signInManager = signInManager;
			_departmentsService = departmentsService;
			_usersService = usersService;
			_emailService = emailService;
			_invitesService = invitesService;
			_userProfileService = userProfileService;
			_subscriptionsService = subscriptionsService;
			_affiliateService = affiliateService;
			_eventAggregator = eventAggregator;
			_emailMarketingProvider = emailMarketingProvider;
		}
		#endregion Private Members and Constructors

		[HttpPost]
		[AllowAnonymous]
		public async Task<ValidateLogInResult> ValidateLogIn([FromBody]ValidateInput authInput)
		{
			var signInResult = await _signInManager.PasswordSignInAsync(authInput.Usr, authInput.Pass, false, lockoutOnFailure: false);

			ValidateLogInResult result = new ValidateLogInResult();
			result.Successful = signInResult.Succeeded;
			result.IsLockedOut = signInResult.IsLockedOut;
			result.NotAllowed = signInResult.IsNotAllowed;

			return result;
		}

		[HttpPost]
		[AllowAnonymous]
		public async Task<DepartmentCreationResult> RegisterDepartment([FromBody]DepartmentCreationInput model)
		{
			DepartmentCreationResult creationResult = new DepartmentCreationResult();
			var user = new Microsoft.AspNet.Identity.EntityFramework6.IdentityUser { UserName = model.Username, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString() };
			var result = await _userManager.CreateAsync(user, model.Password);
			if (result.Succeeded)
			{
				UserProfile up = new UserProfile();
				up.UserId = user.Id;

				var names = model.FullName.Split(char.Parse(" "));

				if (names.Length > 1)
				{
					up.FirstName = names[0];
					up.LastName = names[1];
				}
				else
				{
					up.FirstName = model.FullName;
					up.LastName = "";
				}
				_userProfileService.SaveProfile(0, up);

				_usersService.AddUserToUserRole(user.Id);
				_usersService.InitUserExtInfo(user.Id);

				var savedUser = await _userManager.FindByIdAsync(user.Id);

				Department department = _departmentsService.CreateDepartment(model.DepartmentName, user.Id, model.DepartmentType);

				//_departmentsService.AddUserToDepartment(model.DepartmentName, user.Id);
				_departmentsService.AddUserToDepartment(department.DepartmentId, user.Id);
				_subscriptionsService.CreateFreePlanPayment(department.DepartmentId, user.Id);
				_emailMarketingProvider.SubscribeUserToAdminList(up.FirstName, up.LastName, model.Email);
				_departmentsService.InvalidateDepartmentMembers();

				_emailService.SendWelcomeEmail(department.Name, $"{up.FirstName} {up.LastName}", model.Email, model.Username, model.Password, department.DepartmentId);


				creationResult.Successful = true;
			}
			else
			{
				creationResult.Successful = false;
			}

			return creationResult;
		}
	}

	public class ValidateInput
	{
		public string Usr { get; set; }
		public string Pass { get; set; }
	}

	public class DepartmentCreationInput
	{
		public string Username { get; set; }
		public string FullName { get; set; }
		public string DepartmentName { get; set; }
		public string Email { get; set; }
		public string Password { get; set; }
		public string DepartmentType { get; set; }
	}
	public class DepartmentCreationResult
	{
		public bool Successful { get; set; }
	}
}
