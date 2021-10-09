using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Resgrid.Config;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Controllers
{
#if (!DEBUG || !DOCKER)
	//[RequireHttps]
#endif
	public class AccountController : Controller
	{
		#region Private Members and Constructors
		private readonly UserManager<IdentityUser> _userManager;
		private readonly SignInManager<IdentityUser> _signInManager;
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
						UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
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
			ViewData["LoginNotice"] = SystemBehaviorConfig.LoginPageNotice;
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
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

			ViewData["ReturnUrl"] = returnUrl;
			if (ModelState.IsValid)
			{
				// This doesn't count login failures towards account lockout
				// To enable password failures to trigger account lockout, set lockoutOnFailure: true

				try
				{
					var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, true, lockoutOnFailure: false);
					if (result != null && result.Succeeded)
					{
						if (await _usersService.DoesUserHaveAnyActiveDepartments(model.Username))
						{
							await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, User,
								new AuthenticationProperties
								{
									ExpiresUtc = DateTime.UtcNow.AddHours(8),
									IsPersistent = false,
									AllowRefresh = false
								});

							Response.Cookies.Delete(".AspNetCore.Identity.Application");
							Response.Cookies.Delete(".AspNetCore.Identity.ApplicationC1");
							Response.Cookies.Delete(".AspNetCore.Identity.ApplicationC2");
							if (!String.IsNullOrWhiteSpace(returnUrl))
								return RedirectToLocal(returnUrl);
							else
							{
								return RedirectToAction("Dashboard", "Home", new { Area = "User" });
							}
						}
						else
						{
							ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. To log into Resgrid you need at least one active department. You can have a department add you by sending an email based invite to your Resgrid accounts email address.");
							return View(model);
						}
					}
					if (result != null && result.IsLockedOut)
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
					Logging.LogException(ex);

					if (!await _usersService.DoesUserHaveAnyActiveDepartments(model.Username))
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
			model.SiteKey = WebConfig.RecaptchaPublicKey;
			ViewData["ReturnUrl"] = returnUrl;

			return View(model);
		}

		//
		// POST: /Account/Register
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken, string returnUrl = null)
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");


			ViewBag.DepartmentTypes = new SelectList(model.DepartmentTypes);
			model.SiteKey = WebConfig.RecaptchaPublicKey;
			ViewData["ReturnUrl"] = returnUrl;

			if (ModelState.IsValid)
			{
				var user = new IdentityUser { UserName = model.Username, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString() };
				var result = await _userManager.CreateAsync(user, model.Password);
				if (result.Succeeded)
				{
					UserProfile up = new UserProfile();
					up.UserId = user.Id;
					up.FirstName = model.FirstName;
					up.LastName = model.LastName;
					await _userProfileService.SaveProfileAsync(0, up, cancellationToken);

					_usersService.AddUserToUserRole(user.Id);
					_usersService.InitUserExtInfo(user.Id);

					Department department = await _departmentsService.CreateDepartmentAsync(model.DepartmentName, user.Id, model.DepartmentType, null, cancellationToken);
					await _departmentsService.AddUserToDepartmentAsync(department.DepartmentId, user.Id, true, cancellationToken);
					await _subscriptionsService.CreateFreePlanPaymentAsync(department.DepartmentId, user.Id, cancellationToken);

					// Guard, in case testing has caching turned on for the shared redis cache there can be artifacts
					_departmentsService.InvalidateAllDepartmentsCache(department.DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();

					_emailMarketingProvider.SubscribeUserToAdminList(model.FirstName, model.LastName, model.Email);
					await _emailService.SendWelcomeEmail(department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.Username, model.Password, department.DepartmentId);

					var loginResult = await _signInManager.PasswordSignInAsync(model.Username, model.Password, true, lockoutOnFailure: false);
					if (loginResult.Succeeded)
					{
						await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, HttpContext.User, new AuthenticationProperties
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
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			RemoveCookies();
			return RedirectToAction("LogOn", "Account", new { Area = "" });
		}

		//
		// GET: /Account/ForgotPassword
		[HttpGet]
		[AllowAnonymous]
		public IActionResult ForgotPassword()
		{
			ForgotPasswordViewModel model = new ForgotPasswordViewModel();
			model.SiteKey = WebConfig.RecaptchaPublicKey;
			return View(model);
		}

		//
		// POST: /Account/ForgotPassword
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
		{
			model.SiteKey = WebConfig.RecaptchaPublicKey;

			if (ModelState.IsValid)
			{
				var user = await _userManager.FindByEmailAsync(model.Email);
				if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
				{
					// Don't reveal that the user does not exist or is not confirmed
					return View("ForgotPasswordConfirmation");
				}

				var profile = await _userProfileService.GetProfileByUserIdAsync(user.Id);
				var department = await _departmentsService.GetDepartmentForUserAsync(user.UserName);

				var token = await _userManager.GeneratePasswordResetTokenAsync(user);
				var newPassword = RandomGenerator.GenerateRandomString(6, 8, false, false, true, true, false, true, null);
				var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

				if (result.Succeeded)
				{
					await _emailService.SendPasswordResetEmail(user.Email, profile.FullName.AsFirstNameLastName, user.UserName, newPassword, department.Name);
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
		public async Task<IActionResult> CompleteInvite(string inviteCode)
		{
			Guid code;

			if (!Guid.TryParse(inviteCode, out code))
				return RedirectToAction("MissingInvite");

			CompleteInviteModel model = new CompleteInviteModel();
			model.Invite = await _invitesService.GetInviteByCodeAsync(code);

			if (model.Invite == null)
				return RedirectToAction("MissingInvite");

			if (model.Invite.CompletedOn.HasValue)
				return RedirectToAction("CompletedInvite");

			var department = await _departmentsService.GetDepartmentByIdAsync(model.Invite.DepartmentId, true);

			if (department == null)
				return RedirectToAction("MissingInvite");

			model.DepartmentName = department.Name;
			model.Email = model.Invite.EmailAddress;
			model.Code = inviteCode.ToString();

			return View(model);
		}

		[AllowAnonymous]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteInvite(CompleteInviteModel model, CancellationToken cancellationToken)
		{
			model.Invite = await _invitesService.GetInviteByCodeAsync(Guid.Parse(model.Code));
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
				var user = new IdentityUser { UserName = model.UserName, Email = model.Email, SecurityStamp = Guid.NewGuid().ToString() };
				var result = await _userManager.CreateAsync(user, model.Password);
				if (result.Succeeded)
				{
					UserProfile up = new UserProfile();
					up.UserId = user.Id;
					up.FirstName = model.FirstName;
					up.LastName = model.LastName;
					await _userProfileService.SaveProfileAsync(model.Invite.DepartmentId, up, cancellationToken);

					_usersService.AddUserToUserRole(user.Id);
					_usersService.InitUserExtInfo(user.Id);
					await _departmentsService.AddUserToDepartmentAsync(model.Invite.DepartmentId, user.Id, false, cancellationToken);

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

					await _invitesService.CompleteInviteAsync(model.Invite.Code, user.UserId, cancellationToken);
					_emailMarketingProvider.SubscribeUserToUsersList(model.FirstName, model.LastName, user.Email);

					await _emailService.SendWelcomeEmail(model.Invite.Department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.UserName, model.Password, model.Invite.DepartmentId);

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

		private Task<IdentityUser> GetCurrentUserAsync()
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
