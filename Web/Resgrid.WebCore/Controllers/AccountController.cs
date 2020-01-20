using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Models;
using Resgrid.Web.Models.AccountViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Web.Controllers
{
#if (DEBUG)
	[Authorize]
#else
		[RequireHttps]
		[Authorize]
#endif
	public class AccountController : Controller
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

		public AccountController(
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

		//
		// GET: /Account/Login
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LogOn(string returnUrl = null)
		{
			//RemoveCookies();

			ViewData["ReturnUrl"] = returnUrl;
			return View();
		}

		//
		// POST: /Account/Login
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LogOn(LoginViewModel model, string returnUrl = null)
		{
			await _signInManager.SignOutAsync();
			await HttpContext.Authentication.SignOutAsync("ResgridCookieMiddlewareInstance");

			ViewData["ReturnUrl"] = returnUrl;
			if (ModelState.IsValid)
			{
				// This doesn't count login failures towards account lockout
				// To enable password failures to trigger account lockout, set lockoutOnFailure: true

				try
				{
					var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, model.RememberMe, lockoutOnFailure: false);
					if (result.Succeeded)
					{
						if (_usersService.DoesUserHaveAnyActiveDepartments(model.Username))
						{

							await HttpContext.Authentication.SignInAsync("ResgridCookieMiddlewareInstance", HttpContext.User,
								new AuthenticationProperties
								{
									ExpiresUtc = DateTime.UtcNow.AddHours(8),
									IsPersistent = false,
									AllowRefresh = false
								});

							if (!String.IsNullOrWhiteSpace(returnUrl))
								return RedirectToLocal(returnUrl);
							else
							{
								if (HttpContext.User.IsInRole("Admins"))
									return RedirectToAction("Index", "Home", new { Area = "Admin" });
								else
								{
									return RedirectToAction("Dashboard", "Home", new { Area = "User" });
								}
							}
						}
						else
						{
							ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. To log into Resgrid you need at least one active department. You can have a department add you by sending an email based invite to your Resgrid accounts email address.");
							return View(model);
						}
					}
					if (result.IsLockedOut)
					{
						return View("Lockout");
					}
					else
					{
						ModelState.AddModelError(string.Empty, "Invalid username or password, please check them and try again.");
						return View(model);
					}
				}
				catch (Exception ex)
				{
					if (!_usersService.DoesUserHaveAnyActiveDepartments(model.Username))
					{
						ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. This usually happens when you only belong to one department and you have been removed (deleted) from that department. To log into Resgrid you need at least one active department. You can have a department add you by sending an email based invite to your Resgrid accounts email address.");
						return View(model);
					}
					else
					{
						ModelState.AddModelError(string.Empty, "An unknown login error has occurred, please check your credentials, ensure you are an active member of a department and have a department to log into.");
						return View(model);
					}
				}
			}

			// If we got this far, something failed, redisplay form
			return View(model);
		}

		//
		// GET: /Account/Register
		[HttpGet]
		[AllowAnonymous]
		public IActionResult Register(string returnUrl = null)
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			RegisterViewModel model = new RegisterViewModel();
			ViewBag.DepartmentTypes = new SelectList(model.DepartmentTypes);
			ViewData["ReturnUrl"] = returnUrl;

			return View(model);
		}

		//
		// POST: /Account/Register
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null)
		{
			ViewBag.DepartmentTypes = new SelectList(model.DepartmentTypes);

			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			ViewData["ReturnUrl"] = returnUrl;
			if (ModelState.IsValid)
			{
				var user = new Microsoft.AspNet.Identity.EntityFramework6.IdentityUser { UserName = model.Username, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString() };
				var result = await _userManager.CreateAsync(user, model.Password);
				if (result.Succeeded)
				{
					UserProfile up = new UserProfile();
					up.UserId = user.Id;
					up.FirstName = model.FirstName;
					up.LastName = model.LastName;
					_userProfileService.SaveProfile(0, up);

					_usersService.AddUserToUserRole(user.Id);
					_usersService.InitUserExtInfo(user.Id);

					var savedUser = await _userManager.FindByIdAsync(user.Id);

					Department department = _departmentsService.CreateDepartment(model.DepartmentName, user.Id, model.DepartmentType);
					_departmentsService.AddUserToDepartment(department.DepartmentId, user.Id);
					_subscriptionsService.CreateFreePlanPayment(department.DepartmentId, user.Id);
					_emailMarketingProvider.SubscribeUserToAdminList(model.FirstName, model.LastName, model.Email);

					_departmentsService.InvalidateDepartmentMembers();

					_emailService.SendWelcomeEmail(department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.Username, model.Password, department.DepartmentId);

					//await _signInManager.SignInAsync(savedUser, isPersistent: false);
					//return RedirectToLocal(returnUrl);

					var loginResult = await _signInManager.PasswordSignInAsync(model.Username, model.Password, true, lockoutOnFailure: false);
					if (loginResult.Succeeded)
					{
						await HttpContext.Authentication.SignInAsync("ResgridCookieMiddlewareInstance", HttpContext.User, new AuthenticationProperties
						{
							ExpiresUtc = DateTime.UtcNow.AddHours(24),
							IsPersistent = false,
							AllowRefresh = false
						});

						if (!String.IsNullOrWhiteSpace(returnUrl))
							return RedirectToLocal(returnUrl);
						else
							return RedirectToAction("Dashboard", "Home", new { Area = "User" });
					}
					else
					{
						return View(model);
					}
				}
				AddErrors(result);
			}

			// If we got this far, something failed, redisplay form
			return View(model);
		}

		//
		// POST: /Account/LogOff
		[HttpGet]
		public async Task<IActionResult> LogOff()
		{
			await _signInManager.SignOutAsync();
			await HttpContext.Authentication.SignOutAsync("ResgridCookieMiddlewareInstance");
			RemoveCookies();
			return RedirectToAction("LogOn", "Account", new { Area = "" });
		}

		//
		// GET: /Account/ForgotPassword
		[HttpGet]
		[AllowAnonymous]
		public IActionResult ForgotPassword()
		{
			return View();
		}

		//
		// POST: /Account/ForgotPassword
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
		{
			if (ModelState.IsValid)
			{
				var user = await _userManager.FindByEmailAsync(model.Email);
				if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
				{
					// Don't reveal that the user does not exist or is not confirmed
					return View("ForgotPasswordConfirmation");
				}

				var profile = _userProfileService.GetProfileByUserId(user.Id);
				var department = _departmentsService.GetDepartmentForUser(user.UserName);

				var token = await _userManager.GeneratePasswordResetTokenAsync(user);
				var newPassword = RandomGenerator.GenerateRandomString(6, 10, false, false, true, true, false, true, null);
				var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

				if (result.Succeeded)
				{
					_emailService.SendPasswordResetEmail(user.Email, profile.FullName.AsFirstNameLastName, user.UserName, newPassword, department.Name);
				}

				return View("ForgotPasswordConfirmation");

				// For more information on how to enable account confirmation and password reset please visit http://go.microsoft.com/fwlink/?LinkID=532713
				// Send an email with this link
				//var code = await _userManager.GeneratePasswordResetTokenAsync(user);
				//var callbackUrl = Url.Action("ResetPassword", "Account", new { userId = user.Id, code = code }, protocol: HttpContext.Request.Scheme);
				//await _emailSender.SendEmailAsync(model.Email, "Reset Password",
				//   $"Please reset your password by clicking here: <a href='{callbackUrl}'>link</a>");
				//return View("ForgotPasswordConfirmation");
			}

			// If we got this far, something failed, redisplay form
			return View(model);
		}

		//
		// GET: /Account/ForgotPasswordConfirmation
		[HttpGet]
		[AllowAnonymous]
		public IActionResult ForgotPasswordConfirmation()
		{
			return View();
		}

		[AllowAnonymous]
		[HttpGet]
		public IActionResult MissingInvite()
		{
			return View();
		}

		[AllowAnonymous]
		[HttpGet]
		public IActionResult CompletedInvite()
		{
			return View();
		}

		[AllowAnonymous]
		[HttpGet]
		public IActionResult CompleteInvite(string inviteCode)
		{
			Guid code;

			if (!Guid.TryParse(inviteCode, out code))
				return RedirectToAction("MissingInvite");

			CompleteInviteModel model = new CompleteInviteModel();
			model.Invite = _invitesService.GetInviteByCode(code);

			if (model.Invite == null)
				return RedirectToAction("MissingInvite");

			if (model.Invite.CompletedOn.HasValue)
				return RedirectToAction("CompletedInvite");

			model.Email = model.Invite.EmailAddress;
			model.Code = inviteCode.ToString();

			return View(model);
		}

		[AllowAnonymous]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteInvite(CompleteInviteModel model)
		{
			model.Invite = _invitesService.GetInviteByCode(Guid.Parse(model.Code));
			model.Email = model.Invite.EmailAddress;

			if (!StringHelpers.ValidateEmail(model.Email))
			{
				ModelState.AddModelError("EmailAddresses", string.Format("{0} does not appear to be valid. Check the address and try again.", model.Email));
			}

			var existingUser = _usersService.GetUserByEmail(model.Email);
			if (existingUser != null)
			{
				ModelState.AddModelError("EmailAddresses", string.Format("The email address {0} is already in use in this department on another. Email address can only be used once per account in the system. Use the account recovery form to recover your username and password.", model.Email));
			}

			if (ModelState.IsValid)
			{
				var user = new Microsoft.AspNet.Identity.EntityFramework6.IdentityUser { UserName = model.UserName, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString() };
				var result = await _userManager.CreateAsync(user, model.Password);
				if (result.Succeeded)
				{
					UserProfile up = new UserProfile();
					up.UserId = user.Id;
					up.FirstName = model.FirstName;
					up.LastName = model.LastName;
					_userProfileService.SaveProfile(model.Invite.DepartmentId, up);

					_usersService.AddUserToUserRole(user.Id);
					_usersService.InitUserExtInfo(user.Id);
					_departmentsService.AddUserToDepartment(model.Invite.DepartmentId, user.Id);

					_eventAggregator.SendMessage<UserCreatedEvent>(new UserCreatedEvent()
					{
						DepartmentId = model.Invite.Department.DepartmentId,
						Name = $"{model.FirstName} {model.LastName}",
						User = user
					});

					_departmentsService.InvalidateDepartmentUsersInCache(model.Invite.DepartmentId);
					_departmentsService.InvalidatePersonnelNamesInCache(model.Invite.DepartmentId);
					_usersService.ClearCacheForDepartment(model.Invite.DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();

					_invitesService.CompleteInvite(model.Invite.Code, user.UserId);
					_emailMarketingProvider.SubscribeUserToUsersList(model.FirstName, model.LastName, user.Email);

					_emailService.SendWelcomeEmail(model.Invite.Department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.UserName, model.Password, model.Invite.DepartmentId);

					await _signInManager.SignInAsync(user, isPersistent: false);

					return RedirectToAction("Dashboard", "Home", new { area = "User" });
				}
				AddErrors(result);
			}

			return View(model);
		}

		[AllowAnonymous]
		[HttpGet]
		public IActionResult MissingCode()
		{
			return View();
		}

		public IActionResult AccessDenied()
		{
			return RedirectToAction("Unauthorized", "Public");
		}

		#region Helpers
		private void RemoveCookies()
		{
			var myCookies = Request.Cookies.Select(x => x.Key).ToList();
			foreach (string cookie in myCookies)
			{
				var requestCookie = Request.Cookies[cookie];//..Expires = DateTime.Now.AddDays(-1);

				Response.Cookies.Append(cookie, requestCookie, new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTime.Now.AddDays(-1) });
			}
		}

		private void AddErrors(IdentityResult result)
		{
			foreach (var error in result.Errors)
			{
				ModelState.AddModelError(string.Empty, error.Description);
			}
		}

		private Task<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> GetCurrentUserAsync()
		{
			return _userManager.GetUserAsync(HttpContext.User);
		}

		private IActionResult RedirectToLocal(string returnUrl)
		{
			if (Url.IsLocalUrl(returnUrl))
			{
				return Redirect(returnUrl);
			}
			else
			{
				return RedirectToAction(nameof(HomeController.Index), "Home");
			}
		}

		#endregion
	}
}
