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
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Helpers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Localization;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
using PostHog;
using Microsoft.Extensions.DependencyInjection;

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
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IServiceProvider _serviceProvider;


		public AccountController(
						UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
						IDepartmentsService departmentsService, IUsersService usersService, IEmailService emailService, IInvitesService invitesService, IUserProfileService userProfileService,
						ISubscriptionsService subscriptionsService, IAffiliateService affiliateService, IEventAggregator eventAggregator, IEmailMarketingProvider emailMarketingProvider,
						ISystemAuditsService systemAuditsService, ICacheProvider cacheProvider, IServiceProvider serviceProvider)
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
			_systemAuditsService = systemAuditsService;
			_cacheProvider = cacheProvider;
			_serviceProvider = serviceProvider;
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
		public async Task<IActionResult> LogOn(LoginViewModel model, CancellationToken cancellationToken, string returnUrl = null)
		{
			await _signInManager.SignOutAsync();
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

			ViewData["ReturnUrl"] = returnUrl;
			if (ModelState.IsValid)
			{
				try
				{
					var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, true, lockoutOnFailure: true);

					SystemAudit audit = new SystemAudit();
					audit.System = (int)SystemAuditSystems.Website;
					audit.Type = (int)SystemAuditTypes.Login;
					audit.Username = model.Username;
					audit.Successful = result.Succeeded;
					audit.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
					audit.ServerName = Environment.MachineName;
					audit.Data = $"Web LogOn {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
					await _systemAuditsService.SaveSystemAuditAsync(audit, cancellationToken);

					if (result != null && result.RequiresTwoFactor)
					{
						// Persist credentials in session so the 2FA completion step can finish setting up the session
						HttpContext.Session.SetString("Resgrid2FAPendingUsername", model.Username);
						HttpContext.Session.SetString("Resgrid2FAPendingPassword", model.Password);
						return RedirectToAction(nameof(LoginWith2fa), new { returnUrl });
					}
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

							await SetupUserSessionAsync(model.Username, model.Password);

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
			if (String.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) || String.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
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
			if (String.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) || String.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
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
					await _departmentsService.InvalidateAllDepartmentsCache(department.DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();

					await _emailMarketingProvider.SubscribeUserToAdminList(model.FirstName, model.LastName, model.Email);
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
		// GET: /Account/LoginWith2fa
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LoginWith2fa(string returnUrl = null)
		{
			// Ensure the user has gone through the username & password screen first
			var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
			if (user == null)
				return RedirectToAction(nameof(LogOn));

			ViewData["ReturnUrl"] = returnUrl;
			return View(new VerifyCodeViewModel { ReturnUrl = returnUrl });
		}

		//
		// POST: /Account/LoginWith2fa
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginWith2fa(VerifyCodeViewModel model, CancellationToken cancellationToken, string returnUrl = null)
		{
			if (!ModelState.IsValid) return View(model);

			// Fetch the user before sign-in while the partial 2FA cookie is still present
			var user = await _signInManager.GetTwoFactorAuthenticationUserAsync()
						?? await _userManager.FindByNameAsync(model.Provider ?? string.Empty);

			var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, model.RememberMe, model.RememberBrowser);

			var audit = new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorLoginVerified,
				UserId = user?.Id,
				Username = user?.UserName,
				Successful = result.Succeeded,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"2FA login attempt. {Request.Headers["User-Agent"]}"
			};
			await _systemAuditsService.SaveSystemAuditAsync(audit, cancellationToken);

			if (result.Succeeded)
			{
				if (user == null || !await _usersService.DoesUserHaveAnyActiveDepartments(user.UserName))
				{
					ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. To log into Resgrid you need at least one active department.");
					return View(model);
				}

				// Build the full claims principal and sign into the app's cookie scheme
				var principal = await _signInManager.CreateUserPrincipalAsync(user);
				await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
					new AuthenticationProperties
					{
						ExpiresUtc = DateTime.UtcNow.AddHours(8),
						IsPersistent = false,
						AllowRefresh = false
					});

				// Stamp the step-up session key immediately after login 2FA
				HttpContext.Session.SetString("Resgrid2FAVerifiedAt", DateTime.UtcNow.ToString("O"));

				var pendingUsername = HttpContext.Session.GetString("Resgrid2FAPendingUsername");
				var pendingPassword = HttpContext.Session.GetString("Resgrid2FAPendingPassword");

				if (!string.IsNullOrWhiteSpace(pendingUsername) && !string.IsNullOrWhiteSpace(pendingPassword))
				{
					await SetupUserSessionAsync(pendingUsername, pendingPassword);
					HttpContext.Session.Remove("Resgrid2FAPendingUsername");
					HttpContext.Session.Remove("Resgrid2FAPendingPassword");
				}

				// Prefer the query-string returnUrl, fall back to the hidden-field value in the model
				var redirect = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : model.ReturnUrl;
				if (!string.IsNullOrWhiteSpace(redirect) && Url.IsLocalUrl(redirect))
					return Redirect(redirect);

				return RedirectToAction("Dashboard", "Home", new { Area = "User" });
			}
			if (result.IsLockedOut)
				return View("Lockout");

			ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
			return View(model);
		}

		//
		// GET: /Account/LoginWithRecoveryCode
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LoginWithRecoveryCode(string returnUrl = null)
		{
			var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
			if (user == null) return RedirectToAction(nameof(LogOn));

			ViewData["ReturnUrl"] = returnUrl;
			return View();
		}

		//
		// POST: /Account/LoginWithRecoveryCode
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginWithRecoveryCode(VerifyCodeViewModel model, CancellationToken cancellationToken, string returnUrl = null)
		{
			if (!ModelState.IsValid) return View(model);

			// Fetch the user before sign-in while the partial 2FA cookie is still present
			var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();

			var recoveryCode = model.Code.Replace(" ", string.Empty);
			var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

			var audit = new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.TwoFactorRecoveryCodeUsed,
				UserId = user?.Id,
				Username = user?.UserName,
				Successful = result.Succeeded,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Recovery code login attempt. {Request.Headers["User-Agent"]}"
			};
			await _systemAuditsService.SaveSystemAuditAsync(audit, cancellationToken);

			if (result.Succeeded)
			{
				if (user == null || !await _usersService.DoesUserHaveAnyActiveDepartments(user.UserName))
				{
					ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. To log into Resgrid you need at least one active department.");
					return View(model);
				}

				// Build the full claims principal and sign into the app's cookie scheme
				var principal = await _signInManager.CreateUserPrincipalAsync(user);
				await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
					new AuthenticationProperties
					{
						ExpiresUtc = DateTime.UtcNow.AddHours(8),
						IsPersistent = false,
						AllowRefresh = false
					});

				HttpContext.Session.SetString("Resgrid2FAVerifiedAt", DateTime.UtcNow.ToString("O"));

				var pendingUsername = HttpContext.Session.GetString("Resgrid2FAPendingUsername");
				var pendingPassword = HttpContext.Session.GetString("Resgrid2FAPendingPassword");

				if (!string.IsNullOrWhiteSpace(pendingUsername) && !string.IsNullOrWhiteSpace(pendingPassword))
				{
					await SetupUserSessionAsync(pendingUsername, pendingPassword);
					HttpContext.Session.Remove("Resgrid2FAPendingUsername");
					HttpContext.Session.Remove("Resgrid2FAPendingPassword");
				}

				var redirect = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : model.ReturnUrl;
				if (!string.IsNullOrWhiteSpace(redirect) && Url.IsLocalUrl(redirect))
					return Redirect(redirect);

				return RedirectToAction("Dashboard", "Home", new { Area = "User" });
			}
			if (result.IsLockedOut)
				return View("Lockout");

			ModelState.AddModelError(string.Empty, "Invalid recovery code.");
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
				var newPassword = RandomGenerator.GenerateRandomString(8, 10, false, false, false, true, false, true, null);
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
						DepartmentId = model.Invite.DepartmentId,
						Name = $"{model.FirstName} {model.LastName}",
						User = user
					});

					_departmentsService.InvalidateDepartmentUsersInCache(model.Invite.DepartmentId);
					_departmentsService.InvalidatePersonnelNamesInCache(model.Invite.DepartmentId);
					_usersService.ClearCacheForDepartment(model.Invite.DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();

					var department = await _departmentsService.GetDepartmentByIdAsync(model.Invite.DepartmentId);

					await _invitesService.CompleteInviteAsync(model.Invite.Code, user.UserId, cancellationToken);
					await _emailMarketingProvider.SubscribeUserToUsersList(model.FirstName, model.LastName, user.Email);

					await _emailService.SendWelcomeEmail(department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.UserName, model.Password, model.Invite.DepartmentId);

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

		[AllowAnonymous]
		[HttpPost]
		public IActionResult SetLanugage(string culture, string returnUrl)
		{
			if (!String.IsNullOrWhiteSpace(culture))
			{
				Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), new CookieOptions { Expires = DateTime.UtcNow.AddYears(1) });
				// This guy I think is causing issues with like DateTime rendering mm/dd/yy vs dd/mm/yy, so need to look into that more. -SJ
				//Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
				Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);

				if (!String.IsNullOrWhiteSpace(returnUrl))
					return RedirectToLocal(returnUrl);
				else
					return RedirectToAction("LogOn");
			}

			return RedirectToAction("LogOn");
		}

		#region Helpers
		private void RemoveCookies()
		{
			var myCookies = Request.Cookies.Select(x => x.Key).ToList();
			foreach (string cookie in myCookies)
			{
				var requestCookie = Request.Cookies[cookie];
				Response.Cookies.Append(cookie, requestCookie, new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTime.Now.AddDays(-1) });
			}
		}

		private async Task SetupUserSessionAsync(string username, string password)
		{
			try
			{
				var userId = User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.PrimarySid)?.Value;

				if (!string.IsNullOrWhiteSpace(userId))
				{
					var token = await ApiAuthHelper.GetBearerApiTokenAsync(username, password);
					await _cacheProvider.SetStringAsync(CacheConfig.ApiBearerTokenKeyName + $"_${userId}", token, new TimeSpan(48, 0, 0));

					if (!string.IsNullOrWhiteSpace(TelemetryConfig.PostHogApiKey))
					{
						var email = User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Email)?.Value;
						var departmentId = User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.PrimaryGroupSid)?.Value;
						var departmentName = User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Actor)?.Value;
						var name = User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.GivenName)?.Value;
						var createdOn = User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.OtherPhone)?.Value;

						if (!string.IsNullOrWhiteSpace(Config.TelemetryConfig.PostHogApiKey))
						{
							var posthog = _serviceProvider.GetService<IPostHogClient>();

							if (posthog != null)
							{
								await posthog.IdentifyAsync(
									userId,
									email,
									name,
									personPropertiesToSet: new()
									{
										["departmentId"] = departmentId,
										["departmentName"] = departmentName,
									},
									personPropertiesToSetOnce: new()
									{
										["createdOn"] = createdOn
									});
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
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
