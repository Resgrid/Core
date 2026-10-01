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
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Localization;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Web.Attributes;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Security;

namespace Resgrid.Web.Controllers
{
#if (!DEBUG || !DOCKER)
	//[RequireHttps]
#endif
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): signing in
	// and out, locking and unlocking a shared session, and verifying a second factor touch no department data.
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public partial class AccountController : Controller
	{
		private const string RecoveryGrantCookie = ".Resgrid.PasswordRecovery";

		/// <summary>
		/// The restricted login transaction's secret between the password and the second factor (passkey plan section 5.2): HttpOnly,
		/// Secure and same-site, sent only to /Account, and useless except for finishing that one sign-in. The server keeps only its hash.
		/// </summary>
		private const string MfaLoginCookie = ".Resgrid.MfaLogin";
		private const string MfaLoginCookiePath = "/Account";
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
		private readonly IServiceProvider _serviceProvider;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> _secLocalizer;
		private readonly IUserSessionService _userSessionService;
		private readonly IExternalIdentityLinkService _externalIdentityLinkService;
		private readonly IPasswordRecoveryService _passwordRecoveryService;
		private readonly ILimitsService _limitsService;
		private readonly IMfaEvidenceService _mfaEvidenceService;
		private readonly IMfaActivityService _mfaActivity;
		private readonly IMfaLoginTransactionService _loginTransactions;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> _twoFactorLocalizer;
		private readonly ISsoBrokerService _ssoBroker;
		private readonly ISsoReturnTargetRegistry _ssoReturnTargets;
		private readonly Microsoft.AspNetCore.DataProtection.IDataProtectionProvider _dataProtection;
		private readonly IAdpStepUpService _adpStepUp;
		private readonly Resgrid.Model.Repositories.IUserMfaStateRepository _mfaState;
		private readonly IUserStore<IdentityUser> _userStore;
		private readonly IFactorRecoveryService _recoveries;
		private readonly Resgrid.Model.Repositories.IUserPasskeyRepository _passkeyRows;
		private readonly IAuthenticationChallengeService _challenges;
		private readonly Resgrid.Model.Repositories.IMfaApprovalRequestRepository _approvalRows;

		/// <summary>A refused second factor at sign-in is the account's recent activity (plan section 6.5); no session exists yet.</summary>
		private Task RecordDeniedLoginAsync(IdentityUser user, MfaEvidenceMethod method, CancellationToken cancellationToken) =>
			user == null
				? Task.CompletedTask
				: _mfaActivity.RecordAsync(new MfaActivityEntry
				{
					UserId = user.Id, Method = method, Purpose = MfaEvidencePurpose.Login, Successful = false, ClientApplication = UserSessionClientApplication.Web
				}, cancellationToken);

		/// <summary>A factor verified during this sign-in, recorded as server-side evidence against the new session.</summary>
		private readonly record struct VerifiedFactor(MfaEvidenceKind Kind, MfaEvidenceMethod Method, DateTime VerifiedOnUtc, string FactorReference = null);

		public AccountController(
						UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
						IDepartmentsService departmentsService, IUsersService usersService, IEmailService emailService, IInvitesService invitesService, IUserProfileService userProfileService,
						ISubscriptionsService subscriptionsService, IAffiliateService affiliateService, IEventAggregator eventAggregator, IEmailMarketingProvider emailMarketingProvider,
						ISystemAuditsService systemAuditsService, IServiceProvider serviceProvider,
						IDepartmentSsoService departmentSsoService,
						IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> secLocalizer,
						IUserSessionService userSessionService, IExternalIdentityLinkService externalIdentityLinkService,
						IPasswordRecoveryService passwordRecoveryService, ILimitsService limitsService,
						IMfaEvidenceService mfaEvidenceService, ISecurityNoticeService securityNotices, IMfaActivityService mfaActivity,
						IMfaLoginTransactionService loginTransactions, IPasskeyService passkeys, IMfaApprovalService approvals, IMfaPolicyService mfaPolicy,
						IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor> twoFactorLocalizer,
						ISsoBrokerService ssoBroker, ISsoReturnTargetRegistry ssoReturnTargets,
						Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtection, IAdpStepUpService adpStepUp,
						Resgrid.Model.Repositories.IUserMfaStateRepository mfaState, IUserStore<IdentityUser> userStore, IFactorRecoveryService recoveries,
						Resgrid.Model.Repositories.IUserPasskeyRepository passkeyRows, IAuthenticationChallengeService challenges,
						Resgrid.Model.Repositories.IMfaApprovalRequestRepository approvalRows, ISharedSessionService sharedSessions)
		{
			_sharedSessions = sharedSessions;
			_mfaState = mfaState;
			_userStore = userStore;
			_recoveries = recoveries;
			_passkeyRows = passkeyRows;
			_challenges = challenges;
			_approvalRows = approvalRows;
			_ssoBroker = ssoBroker;
			_ssoReturnTargets = ssoReturnTargets;
			_dataProtection = dataProtection;
			_adpStepUp = adpStepUp;
			_mfaActivity = mfaActivity;
			_loginTransactions = loginTransactions;
			_passkeys = passkeys;
			_approvals = approvals;
			_mfaPolicy = mfaPolicy;
			_twoFactorLocalizer = twoFactorLocalizer;
			_securityNotices = securityNotices;
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
			_serviceProvider = serviceProvider;
			_departmentSsoService = departmentSsoService;
			_secLocalizer = secLocalizer;
			_userSessionService = userSessionService;
			_externalIdentityLinkService = externalIdentityLinkService;
			_passwordRecoveryService = passwordRecoveryService;
			_limitsService = limitsService;
			_mfaEvidenceService = mfaEvidenceService;
		}

		private readonly ISecurityNoticeService _securityNotices;
		private readonly ISharedSessionService _sharedSessions;
		#endregion Private Members and Constructors

		/// <summary>Resolves a password-error key returned by ValidatePasswordAgainstPolicyAsync into a localised message.</summary>
		private string ResolvePwdError(string key)
		{
			if (string.IsNullOrEmpty(key)) return key;
			if (key.StartsWith("PwdErrorTooShort:", StringComparison.Ordinal))
			{
				var minLen = key.Substring("PwdErrorTooShort:".Length);
				return string.Format(_secLocalizer["PwdErrorTooShort"], minLen);
			}
			return _secLocalizer[key];
		}

		//
		// GET: /Account/Login
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LogOn(string returnUrl = null)
		{
			//RemoveCookies();

			ViewData["ReturnUrl"] = returnUrl;
			ViewData["LoginNotice"] = NoticeConfig.EffectiveLoginPageNotice;
			ViewData["LoginMfaMessage"] = TempData["LoginMfaMessage"];
			ViewData["SsoSignInAvailable"] = WebSsoAvailable;
			ViewData["SharedWorkstationAvailable"] = WebSharedSession.IsAvailable;
			ViewData["SharedWorkstation"] = WebSharedSession.IsAvailable ? WebSharedSession.WorkstationLabel(Request) : null;
			if (ViewData["LoginMfaMessage"] == null && string.Equals(Request.Query["reason"], "shift_ended", StringComparison.Ordinal))
				ViewData["LoginMfaMessage"] = _twoFactorLocalizer["SharedShiftEnded"].Value;
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
			// A shared workstation never remembers a browser for anyone (plan section 12.5.2): the second factor is always asked.
			if (WebSharedSession.IsWorkstation(Request))
				await _signInManager.ForgetTwoFactorClientAsync();

			ViewData["ReturnUrl"] = returnUrl;
			if (ModelState.IsValid)
			{
				try
				{
					var passwordUser = await _userManager.FindByNameAsync(model.Username);
					if (passwordUser != null && !await IsPasswordLoginAllowedAsync(passwordUser, cancellationToken))
					{
						await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
						{
							System = (int)SystemAuditSystems.Website,
							Type = (int)SystemAuditTypes.Login,
							UserId = passwordUser.Id,
							Username = model.Username,
							Successful = false,
							IpAddress = IpAddressHelper.GetRequestIP(Request, true),
							ServerName = Environment.MachineName,
							Data = $"Web LogOn blocked by SSO policy {Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
						}, cancellationToken);
						ModelState.AddModelError(string.Empty, "Invalid username or password, please check them and try again.");
						return View(model);
					}

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
						// The password was verified now; the session is created after the second factor.
						if (passwordUser != null && UseLoginTransaction)
							return await BeginLoginTransactionAsync(passwordUser, model, returnUrl, cancellationToken);
						if (passwordUser != null)
							MfaEvidenceSession.StashFirstFactor(HttpContext, passwordUser.Id, DateTime.UtcNow);
						return RedirectToAction(nameof(LoginWith2fa), new { returnUrl });
					}
					if (result != null && result.Succeeded)
					{
						if (await _usersService.DoesUserHaveAnyActiveDepartments(model.Username))
						{
							var signedInUser = await _userManager.FindByNameAsync(model.Username);
							var loginDepartment = UseLoginTransaction ? await _departmentsService.GetDepartmentByUserIdAsync(signedInUser.Id) : null;
							if (loginDepartment != null && await MustSetUpMfaAsync(signedInUser, loginDepartment.DepartmentId, false, cancellationToken))
							{
								// Required MFA the account does not have yet (plan section 7.5 rule 9): a setup transaction, never ordinary access.
								await _signInManager.SignOutAsync();
								return await BeginSetupTransactionAsync(new MfaLoginTransactionRequest
								{
									UserId = signedInUser.Id,
									DepartmentId = loginDepartment.DepartmentId,
									ClientApplication = UserSessionClientApplication.Web,
									FirstFactorMethod = MfaEvidenceMethod.Password,
									FirstFactorVerifiedOnUtc = DateTime.UtcNow,
									AuthenticationGeneration = signedInUser.AuthenticationGeneration,
									TotpEnrolled = false,
									SharedModeRequested = WebInstallation().Shared,
									InstallationLabel = WebInstallation().Label
								}, returnUrl, cancellationToken);
							}

							if (!await SignInTrackedWebSessionAsync(signedInUser,
								UserSessionAuthenticationMethod.LocalPassword, TimeSpan.FromHours(8), cancellationToken,
								new VerifiedFactor(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, DateTime.UtcNow)))
							{
								ModelState.AddModelError(string.Empty,
									"Your department's maximum number of active sessions has been reached. Revoke an existing session or contact your administrator.");
								return View(model);
							}

							// Check whether the department's password expiration policy has been exceeded.
							// Only check for password-based logins (SSO users are unaffected).
							try
							{
								var identityUser = await _userManager.FindByNameAsync(model.Username);
								if (identityUser != null)
								{
									var department = await _departmentsService.GetDepartmentForUserAsync(model.Username);
									if (department != null)
									{
										var member = await _departmentsService.GetDepartmentMemberAsync(identityUser.Id, department.DepartmentId);

										// Check if admin flagged this user to change password on next login
										if (member != null && member.MustChangePassword)
										{
											HttpContext.Session.SetString("ForcePasswordChangeUserId", identityUser.Id);
											HttpContext.Session.SetString("ForcePasswordChangeDeptId", department.DepartmentId.ToString());
											return RedirectToAction(nameof(ForcePasswordChange));
										}

										var policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(department.DepartmentId, cancellationToken);
										if (policy != null && policy.PasswordExpirationDays > 0)
										{
											if (_departmentSsoService.IsPasswordExpired(policy, member?.PasswordLastSetOn))
											{
												HttpContext.Session.SetString("ForcePasswordChangeUserId", identityUser.Id);
												HttpContext.Session.SetString("ForcePasswordChangeDeptId", department.DepartmentId.ToString());
												return RedirectToAction(nameof(ForcePasswordChange));
											}
										}
									}
								}
							}
							catch (Exception ex)
							{
								Logging.LogException(ex);
								// Don't block login on a policy-check error — fail open.
							}

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
		public IActionResult Register(string returnUrl = null, string affiliateCode = null)
		{
			if (String.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) || String.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
				return RedirectToAction("LogOn", "Account");

			RegisterViewModel model = new RegisterViewModel();
			ViewBag.DepartmentTypes = new SelectList(model.DepartmentTypes);
			model.SiteKey = WebConfig.RecaptchaPublicKey;
			model.AffiliateCode = affiliateCode;
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

					if (!String.IsNullOrWhiteSpace(model.AffiliateCode))
					{
						var affiliate = await _affiliateService.GetActiveAffiliateByCodeAsync(model.AffiliateCode);
						if (affiliate != null)
						{
							department.AffiliateCode = affiliate.AffiliateCode;
							await _departmentsService.SaveDepartmentAsync(department, cancellationToken);
						}
					}

					// Only provision free plan when paid plan selection is not required
					if (!SystemBehaviorConfig.RequirePlanSelectionDuringSignup)
						await _subscriptionsService.CreateFreePlanPaymentAsync(department.DepartmentId, user.Id, cancellationToken);

					// Guard, in case testing has caching turned on for the shared redis cache there can be artifacts
					await _departmentsService.InvalidateAllDepartmentsCache(department.DepartmentId);
					_departmentsService.InvalidateDepartmentMembers();

					await _emailMarketingProvider.SubscribeUserToAdminList(model.FirstName, model.LastName, model.Email);
					await _emailService.SendWelcomeEmail(department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.Username, department.DepartmentId);

					var loginResult = await _signInManager.PasswordSignInAsync(model.Username, model.Password, true, lockoutOnFailure: false);
					if (loginResult.Succeeded)
					{
						if (!await SignInTrackedWebSessionAsync(user,
							UserSessionAuthenticationMethod.LocalPassword, TimeSpan.FromHours(24), cancellationToken,
							new VerifiedFactor(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, DateTime.UtcNow)))
						{
							ModelState.AddModelError(string.Empty,
								"Your department's maximum number of active sessions has been reached.");
							return View(model);
						}

						if (!String.IsNullOrWhiteSpace(returnUrl))
							return RedirectToLocal(returnUrl);
						else if (SystemBehaviorConfig.RequirePlanSelectionDuringSignup)
							return RedirectToAction("SelectRegistrationPlan", "Subscription", new { Area = "User", discountCode = model.DiscountCode });
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
			var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, model.RememberMe,
				model.RememberBrowser && !WebSharedSession.IsWorkstation(Request));

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
			if (!result.Succeeded)
				await RecordDeniedLoginAsync(user, MfaEvidenceMethod.Totp, cancellationToken);

			if (result.Succeeded)
			{
				if (user == null || !await _usersService.DoesUserHaveAnyActiveDepartments(user.UserName))
				{
					ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. To log into Resgrid you need at least one active department.");
					return View(model);
				}

				// Build the full claims principal and sign into the app's cookie scheme. Both factors become server-side
				// evidence for the new session; the password time is the one stashed when it was verified.
				var secondFactorAt = DateTime.UtcNow;
				if (!await SignInTrackedWebSessionAsync(user,
					UserSessionAuthenticationMethod.LocalPassword, TimeSpan.FromHours(8), cancellationToken, MfaEvidenceMethod.Totp, null,
					new VerifiedFactor(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, MfaEvidenceSession.TakeFirstFactor(HttpContext, user.Id) ?? secondFactorAt),
					new VerifiedFactor(MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp, secondFactorAt)))
				{
					ModelState.AddModelError(string.Empty,
						"Your department's maximum number of active sessions has been reached. Revoke an existing session or contact your administrator.");
					return View(model);
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
		public async Task<IActionResult> LoginWithRecoveryCode(string returnUrl = null, CancellationToken cancellationToken = default)
		{
			if (HoldsLoginTransaction)
			{
				var (transaction, _, outcome) = await OpenLoginTransactionAsync(cancellationToken);
				if (transaction == null)
					return RestartSignIn(outcome, returnUrl);

				ViewData["ReturnUrl"] = returnUrl;
				ViewData["BackAction"] = nameof(LoginMfa);
				return View(new VerifyCodeViewModel { ReturnUrl = returnUrl });
			}

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
			if (HoldsLoginTransaction)
				return await LoginTransactionRecoveryCodeAsync(model, returnUrl ?? model?.ReturnUrl, cancellationToken);

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
			if (!result.Succeeded)
				await RecordDeniedLoginAsync(user, MfaEvidenceMethod.RecoveryCode, cancellationToken);

			// The code is spent even if the sign-in stops below, so the account holder hears about it either way (plan section 6.4).
			if (result.Succeeded && user != null)
				await _securityNotices.QueueAsync(new SecurityNoticeRequest
				{
					UserId = user.Id, Kind = SecurityNoticeKind.RecoveryCodeUsed, ClientApplication = UserSessionClientApplication.Web
				}, cancellationToken);

			if (result.Succeeded)
			{
				if (user == null || !await _usersService.DoesUserHaveAnyActiveDepartments(user.UserName))
				{
					ModelState.AddModelError(string.Empty, "You do not have any active departments for this user. To log into Resgrid you need at least one active department.");
					return View(model);
				}

				// Build the full claims principal and sign into the app's cookie scheme
				// The recovery code is recorded as recovery evidence only: it never satisfies MFA or ADP.
				var recoveredAt = DateTime.UtcNow;
				if (!await SignInTrackedWebSessionAsync(user,
					UserSessionAuthenticationMethod.Recovery, TimeSpan.FromHours(8), cancellationToken, MfaEvidenceMethod.RecoveryCode, null,
					new VerifiedFactor(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, MfaEvidenceSession.TakeFirstFactor(HttpContext, user.Id) ?? recoveredAt),
					new VerifiedFactor(MfaEvidenceKind.Recovery, MfaEvidenceMethod.RecoveryCode, recoveredAt)))
				{
					ModelState.AddModelError(string.Empty,
						"Your department's maximum number of active sessions has been reached. Revoke an existing session or contact your administrator.");
					return View(model);
				}

				// A recovery code is not recent MFA (passkey plan section 6.1 item 10): no step-up stamp is written, so
				// sensitive operations still require a real factor. The recovery session may replace the authenticator
				// (TwoFactorController.ReplaceAuthenticator) for a short window.
				var redirect = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : model.ReturnUrl;
				if (!string.IsNullOrWhiteSpace(redirect) && Url.IsLocalUrl(redirect))
					return Redirect(redirect);

				TempData["StatusMessage"] = "You signed in with a recovery code. If you no longer have your authenticator app, replace it now.";
				return RedirectToAction("Index", "TwoFactor", new { Area = "User" });
			}
			if (result.IsLockedOut)
				return View("Lockout");

			ModelState.AddModelError(string.Empty, "Invalid recovery code.");
			return View(model);
		}

		// ── Web sign-in on the login transaction (passkey plan sections 5.2, 7.1 and 7.5) ─────────────────────────

		private bool UseLoginTransaction => TwoFactorConfig.WebLoginMfaTransactionEnabled && _loginTransactions.IsEnabled;

		private bool HoldsLoginTransaction => UseLoginTransaction && !string.IsNullOrWhiteSpace(Request.Cookies[MfaLoginCookie]);

		/// <summary>
		/// Starts the restricted login transaction after a verified password. Identity's own partial sign-in is dropped: until a
		/// second factor verifies, this browser holds only the transaction, which carries no password and grants nothing else.
		/// </summary>
		private async Task<IActionResult> BeginLoginTransactionAsync(IdentityUser user, LoginViewModel model, string returnUrl,
			CancellationToken cancellationToken)
		{
			await _signInManager.SignOutAsync();

			MfaLoginTransactionStart start;
			try
			{
				var department = await _departmentsService.GetDepartmentByUserIdAsync(user.Id);
				start = await _loginTransactions.BeginAsync(new MfaLoginTransactionRequest
				{
					UserId = user.Id,
					DepartmentId = department?.DepartmentId,
					ClientApplication = UserSessionClientApplication.Web,
					FirstFactorMethod = MfaEvidenceMethod.Password,
					FirstFactorVerifiedOnUtc = DateTime.UtcNow,
					AuthenticationGeneration = user.AuthenticationGeneration,
					TotpEnrolled = await _userManager.GetTwoFactorEnabledAsync(user),
					SharedModeRequested = WebInstallation().Shared,
					InstallationLabel = WebInstallation().Label
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The Web login transaction could not be started; the sign-in was refused.");
				ModelState.AddModelError(string.Empty, _twoFactorLocalizer["LoginMfaUnavailable"]);
				return View(model);
			}

			SetLoginTransactionCookie(start);
			return RedirectToAction(nameof(LoginMfa), new { returnUrl });
		}

		/// <summary>
		/// This browser as a session records it: a browser set up as a shared workstation asks for a shared session, labeled
		/// with the station unless the request names its device (plan section 10.5). A login transaction records the same, so
		/// the member's Responder sees the sign-in it is asked to approve as the session it will be.
		/// </summary>
		private (bool Shared, string Label) WebInstallation()
		{
			var workstation = WebSharedSession.WorkstationLabel(Request);
			string label = Request.Headers["X-Resgrid-Device-Name"];
			if (string.IsNullOrWhiteSpace(label) && !string.IsNullOrEmpty(workstation))
				label = workstation;
			return (workstation != null, label);
		}

		private void SetLoginTransactionCookie(MfaLoginTransactionStart start) =>
			Response.Cookies.Append(MfaLoginCookie, start.Secret, new CookieOptions
			{
				HttpOnly = true,
				Secure = true,
				SameSite = SameSiteMode.Strict,
				IsEssential = true,
				Path = MfaLoginCookiePath,
				Expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, start.ExpiresInSeconds))
			});

		private void EndLoginTransactionCookie() =>
			Response.Cookies.Delete(MfaLoginCookie, new CookieOptions { Path = MfaLoginCookiePath, Secure = true, SameSite = SameSiteMode.Strict });

		/// <summary>This browser's pending login transaction and its user, or why the sign-in cannot continue.</summary>
		private async Task<(MfaLoginTransaction Transaction, IdentityUser User, MfaLoginTransactionOutcome Outcome)> OpenLoginTransactionAsync(
			CancellationToken cancellationToken)
		{
			var secret = Request.Cookies[MfaLoginCookie];
			if (!UseLoginTransaction || string.IsNullOrWhiteSpace(secret))
				return (null, null, MfaLoginTransactionOutcome.Invalid);

			MfaLoginTransactionResult opened;
			try
			{
				opened = await _loginTransactions.OpenAsync(secret, UserSessionClientApplication.Web, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The Web login transaction could not be read.");
				return (null, null, MfaLoginTransactionOutcome.Unavailable);
			}

			if (!opened.IsUsable)
				return (null, null, opened.Outcome);

			var user = await _userManager.FindByIdAsync(opened.Transaction.UserId);
			return user == null
				? (null, null, MfaLoginTransactionOutcome.SessionRevoked)
				: (opened.Transaction, user, MfaLoginTransactionOutcome.Usable);
		}

		private string LoginMfaMessage(MfaLoginTransactionOutcome outcome) => _twoFactorLocalizer[outcome switch
		{
			MfaLoginTransactionOutcome.Expired => "LoginMfaExpired",
			MfaLoginTransactionOutcome.TooManyAttempts => "LoginMfaTooManyAttempts",
			MfaLoginTransactionOutcome.PolicyChanged => "LoginMfaPolicyChanged",
			MfaLoginTransactionOutcome.SessionRevoked => "LoginMfaSignInChanged",
			MfaLoginTransactionOutcome.Unavailable => "LoginMfaUnavailable",
			_ => "LoginMfaInvalid"
		}].Value;

		/// <summary>The sign-in cannot continue: the transaction is dropped and the user starts again with the reason shown.</summary>
		private IActionResult RestartSignIn(MfaLoginTransactionOutcome outcome, string returnUrl)
		{
			EndLoginTransactionCookie();
			TempData["LoginMfaMessage"] = LoginMfaMessage(outcome);
			return RedirectToAction(nameof(LogOn), new { returnUrl = SafeReturnUrl(returnUrl) });
		}

		/// <summary>The JSON form of <see cref="RestartSignIn"/>: the page goes back to sign-in.</summary>
		private IActionResult RestartSignInJson(MfaLoginTransactionOutcome outcome, string returnUrl)
		{
			EndLoginTransactionCookie();
			TempData["LoginMfaMessage"] = LoginMfaMessage(outcome);
			return Json(new
			{
				success = false,
				error = MfaLoginTransactions.ErrorCode(outcome) ?? "mfa_transaction_invalid",
				restart = Url.Action(nameof(LogOn), new { returnUrl = SafeReturnUrl(returnUrl) })
			});
		}

		private string SafeReturnUrl(string returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

		/// <summary>
		/// Whether this sign-in may use <paramref name="method"/> now: the account is not locked out, the department accepts the
		/// method, and code-based methods have an authenticator behind them. Null when it may; otherwise whether the sign-in must start
		/// again (a locked account) and the message.
		/// </summary>
		private async Task<(bool Restart, MfaLoginTransactionOutcome Outcome, string Error)?> RefuseLoginMethodAsync(MfaLoginTransaction transaction,
			IdentityUser user, MfaEvidenceMethod method, CancellationToken cancellationToken)
		{
			if (await _userManager.IsLockedOutAsync(user))
				return (true, MfaLoginTransactionOutcome.TooManyAttempts, "too_many_attempts");

			if (((method is MfaEvidenceMethod.Totp or MfaEvidenceMethod.RecoveryCode) && !await _userManager.GetTwoFactorEnabledAsync(user)) ||
				!await _loginTransactions.IsMethodAcceptedAsync(transaction, method, cancellationToken))
				return (false, MfaLoginTransactionOutcome.Usable, "mfa_method_not_allowed");

			return null;
		}

		/// <summary>
		/// A second factor that did not verify: it counts against the transaction and the account lockout alike (plan section 7.5
		/// rule 6) and is the account's denied activity. Returns the outcome when this ended the sign-in.
		/// </summary>
		private async Task<MfaLoginTransactionOutcome?> LoginFactorFailedAsync(MfaLoginTransaction transaction, IdentityUser user,
			MfaEvidenceMethod method, CancellationToken cancellationToken)
		{
			await _userManager.AccessFailedAsync(user);
			await _loginTransactions.RecordFailedAttemptAsync(transaction, cancellationToken);
			await AuditLoginTransactionAsync(user, method, false, cancellationToken);
			await RecordDeniedLoginAsync(user, method, cancellationToken);

			if (await _userManager.IsLockedOutAsync(user))
				return MfaLoginTransactionOutcome.TooManyAttempts;

			var (open, _, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			return open == null ? outcome : null;
		}

		private Task AuditLoginTransactionAsync(IdentityUser user, MfaEvidenceMethod method, bool successful, CancellationToken cancellationToken) =>
			_systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)(method == MfaEvidenceMethod.RecoveryCode ? SystemAuditTypes.TwoFactorRecoveryCodeUsed : SystemAuditTypes.TwoFactorLoginVerified),
				UserId = user.Id,
				Username = user.UserName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Web login transaction with {(method == MfaEvidenceMethod.RecoveryCode ? "a recovery code" : MfaMethodNames.From(method))}: " +
					$"{(successful ? "verified" : "verification failed")}. {Request.Headers["User-Agent"]}"
			}, cancellationToken);

		private readonly record struct LoginFinish(string Redirect, MfaLoginTransactionOutcome? Restart, string Error);

		/// <summary>
		/// A verified second factor finishes the sign-in (plan section 7.1 step 4): the transaction is completed and redeemed once,
		/// what allowed the password is checked again, and only then is the normal session created, with the password's own time
		/// and the second factor's as its evidence. A lost response means signing in again; nothing is issued twice.
		/// </summary>
		private async Task<LoginFinish> FinishLoginTransactionAsync(MfaLoginTransaction transaction, IdentityUser user, MfaEvidenceMethod method,
			string factorReference, DateTime verifiedOnUtc, string returnUrl, bool rememberBrowser, CancellationToken cancellationToken)
		{
			await _userManager.ResetAccessFailedCountAsync(user);

			MfaLoginTransactionResult redeemed;
			try
			{
				var completion = await _loginTransactions.CompleteAsync(transaction, method, factorReference, verifiedOnUtc, cancellationToken);
				if (!completion.Succeeded)
					return new LoginFinish(null, completion.Outcome, null);

				redeemed = await _loginTransactions.RedeemAsync(Request.Cookies[MfaLoginCookie], completion.CompletionCode,
					UserSessionClientApplication.Web, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A verified Web login second factor could not be recorded; the sign-in was refused.");
				return new LoginFinish(null, MfaLoginTransactionOutcome.Unavailable, null);
			}

			EndLoginTransactionCookie();
			if (!redeemed.IsUsable)
				return new LoginFinish(null, redeemed.Outcome, null);

			return await SignInRedeemedAsync(redeemed.Transaction, user, method, factorReference, verifiedOnUtc, returnUrl, rememberBrowser,
				cancellationToken);
		}

		/// <summary>
		/// Creates the normal Web session for a redeemed login transaction, once what allowed its first factor is checked again: for
		/// a password, an active department where password sign-in is still permitted; for single sign-on, an active membership in the
		/// department whose provider signed the user in, with that configuration still enabled. The session records the method that
		/// completed the sign-in; its evidence carries the first factor's own time and the second factor's, if there was one.
		/// </summary>
		private async Task<LoginFinish> SignInRedeemedAsync(MfaLoginTransaction done, IdentityUser user, MfaEvidenceMethod? method, string factorReference,
			DateTime verifiedOnUtc, string returnUrl, bool rememberBrowser, CancellationToken cancellationToken)
		{
			var viaSso = done.FirstFactorMethod == (int)MfaEvidenceMethod.Sso;
			var authentication = UserSessionAuthenticationMethod.LocalPassword;
			if (viaSso)
			{
				var membership = done.DepartmentId is int ssoDepartment
					? await _departmentsService.GetDepartmentMemberAsync(user.Id, ssoDepartment, bypassCache: true)
					: null;
				authentication = membership == null ? authentication : await SsoAuthenticationMethodAsync(done.DepartmentId.Value, done.DepartmentSsoConfigId,
					cancellationToken);
				if (membership == null || membership.IsDeleted || membership.IsDisabled == true || authentication == UserSessionAuthenticationMethod.LocalPassword)
					return new LoginFinish(null, MfaLoginTransactionOutcome.SessionRevoked, null);

				// The provider signed the user in to this department, so it becomes the active one, as switching to it would.
				var active = await _departmentsService.GetDepartmentByUserIdAsync(user.Id, true);
				if (active?.DepartmentId != done.DepartmentId)
					await _departmentsService.SetActiveDepartmentForUserAsync(user.Id, done.DepartmentId.Value, user, cancellationToken);
			}
			else if (!await _usersService.DoesUserHaveAnyActiveDepartments(user.UserName) || !await IsPasswordLoginAllowedAsync(user, cancellationToken))
			{
				return new LoginFinish(null, MfaLoginTransactionOutcome.SessionRevoked, null);
			}

			var recovery = method == MfaEvidenceMethod.RecoveryCode;
			var secondFactorAt = done.CompletionVerifiedOnUtc ?? verifiedOnUtc;
			var factors = new System.Collections.Generic.List<VerifiedFactor>
			{
				new(MfaEvidenceKind.FirstFactor, viaSso ? MfaEvidenceMethod.Sso : MfaEvidenceMethod.Password, done.FirstFactorVerifiedOnUtc)
			};
			if (method is MfaEvidenceMethod secondFactor)
				factors.Add(recovery
					? new VerifiedFactor(MfaEvidenceKind.Recovery, MfaEvidenceMethod.RecoveryCode, secondFactorAt)
					: new VerifiedFactor(MfaEvidenceKind.SecondFactor, secondFactor, secondFactorAt, factorReference));

			if (!await SignInTrackedWebSessionCoreAsync(user, recovery ? UserSessionAuthenticationMethod.Recovery : authentication, TimeSpan.FromHours(8),
				method, factorReference, viaSso ? done.DepartmentId : null, viaSso ? done.DepartmentSsoConfigId : null, factors.ToArray(), cancellationToken))
				return new LoginFinish(null, null, "maximum_sessions");

			if (method is MfaEvidenceMethod verified)
				await AuditLoginTransactionAsync(user, verified, true, cancellationToken);

			// A remembered browser may skip the prompt next time; it never counts as a verification (plan section 7.6 row 1). A shared
			// workstation is never remembered (plan section 12.5.2).
			if (rememberBrowser && !recovery && method != null && !WebSharedSession.IsWorkstation(Request))
				await _signInManager.RememberTwoFactorClientAsync(user);

			if (recovery)
			{
				// A recovery code is not recent MFA (plan section 6.1 item 10); the lost factor should be replaced now.
				TempData["StatusMessage"] = _twoFactorLocalizer["LoginMfaRecoveryUsed"].Value;
				return new LoginFinish(SafeReturnUrl(returnUrl) ?? Url.Action("Index", "TwoFactor", new { Area = "User" }), null, null);
			}

			return new LoginFinish(SafeReturnUrl(returnUrl) ?? Url.Action("Dashboard", "Home", new { Area = "User" }), null, null);
		}

		private IActionResult FinishedJson(LoginFinish finish, string returnUrl) =>
			finish.Redirect != null
				? Json(new { success = true, redirect = finish.Redirect })
				: finish.Restart is MfaLoginTransactionOutcome restart
					? RestartSignInJson(restart, returnUrl)
					: Json(new { success = false, error = finish.Error });

		/// <summary>The choices for this sign-in, preferred first; every usable method stays an equal choice (plan section 7.5 rule 5).</summary>
		private async Task<LoginMfaViewModel> LoginMfaModelAsync(MfaLoginTransaction transaction, IdentityUser user, LoginMfaViewModel model,
			CancellationToken cancellationToken)
		{
			var totp = await _userManager.GetTwoFactorEnabledAsync(user);
			bool passkey = false, approval = false, federated = false;
			try
			{
				passkey = await _passkeys.HasActiveForClientAsync(user.Id, UserSessionClientApplication.Web, cancellationToken);
				approval = await _approvals.IsAvailableAsync(user.Id, UserSessionClientApplication.Web, cancellationToken);
				// Provider step-up: a round trip through the department's identity provider, for an account that signs in through it.
				federated = WebSsoAvailable && transaction.DepartmentId is int departmentId &&
					await _departmentSsoService.IsFederatedMfaAvailableAsync(departmentId, user.Id, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Display only: without them the authenticator code is still offered.
				Logging.LogException(ex, "Passkey or approval availability could not be read for Web sign-in.");
			}

			var choice = await _mfaPolicy.GetMethodChoiceAsync(user.Id, totp, transaction.DepartmentId, MfaMethodScope.Login, passkey,
				federatedEnrolled: federated, approvalEnrolled: approval, cancellationToken: cancellationToken);
			var usable = choice.AllowedMethods.Where(choice.EnrolledMethods.Contains)
				.Where(m => m is MfaMethodNames.Totp or MfaMethodNames.Passkey or MfaMethodNames.PasskeyApproval or MfaMethodNames.Federated).ToList();
			if (choice.Preferred != null && usable.Remove(choice.Preferred))
				usable.Insert(0, choice.Preferred);

			model.Methods = usable;
			model.Preferred = usable.FirstOrDefault();
			model.RecoveryAvailable = totp;
			return model;
		}

		//
		// GET: /Account/LoginMfa
		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> LoginMfa(string returnUrl = null, CancellationToken cancellationToken = default)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, returnUrl);

			if (TempData["LoginMfaChoiceMessage"] is string choiceMessage)
				ModelState.AddModelError(string.Empty, choiceMessage);
			var model = await LoginMfaModelAsync(transaction, user, new LoginMfaViewModel { ReturnUrl = SafeReturnUrl(returnUrl) }, cancellationToken);
			if (model.Methods.Count == 0 && !model.RecoveryAvailable)
				return RedirectToAction(nameof(LoginMfaSetup), new { returnUrl = model.ReturnUrl });
			return View(model);
		}

		//
		// POST: /Account/LoginMfa (the authenticator code)
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfa(LoginMfaViewModel model, CancellationToken cancellationToken)
		{
			model ??= new LoginMfaViewModel();
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, model.ReturnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.Totp, cancellationToken);
			if (refusal?.Restart == true)
				return RestartSignIn(refusal.Value.Outcome, model.ReturnUrl);
			if (refusal != null || string.IsNullOrWhiteSpace(model.Code))
			{
				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer[refusal != null ? "LoginMfaMethodUnavailable" : "InvalidCodeLogin"]);
				return View(await LoginMfaModelAsync(transaction, user, model, cancellationToken));
			}

			// One-time: ResgridAuthenticatorTokenProvider accepts each time step once per user, on every surface.
			var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
			if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code))
			{
				var ended = await LoginFactorFailedAsync(transaction, user, MfaEvidenceMethod.Totp, cancellationToken);
				if (ended != null)
					return RestartSignIn(ended.Value, model.ReturnUrl);

				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["InvalidCodeLogin"]);
				model.Code = null;
				return View(await LoginMfaModelAsync(transaction, user, model, cancellationToken));
			}

			var finish = await FinishLoginTransactionAsync(transaction, user, MfaEvidenceMethod.Totp, null, DateTime.UtcNow, model.ReturnUrl,
				model.RememberBrowser, cancellationToken);
			if (finish.Redirect != null)
				return LocalRedirect(finish.Redirect);
			if (finish.Restart is MfaLoginTransactionOutcome restart)
				return RestartSignIn(restart, model.ReturnUrl);

			TempData["LoginMfaMessage"] = _twoFactorLocalizer["LoginMfaMaximumSessions"].Value;
			return RedirectToAction(nameof(LogOn), new { returnUrl = SafeReturnUrl(model.ReturnUrl) });
		}

		/// <summary>Assertion options for the user's Web passkeys, bound to this sign-in's transaction.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaPasskeyOptions(string returnUrl, CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignInJson(outcome, returnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.Passkey, cancellationToken);
			if (refusal != null)
				return refusal.Value.Restart ? RestartSignInJson(refusal.Value.Outcome, returnUrl) : Json(new { success = false, error = refusal.Value.Error });

			var start = await _passkeys.BeginAssertionAsync(PasskeyCaller.ForLoginTransaction(transaction, user.UserName, SystemAuditSystems.Website,
				IpAddressHelper.GetRequestIP(Request, true)), AuthenticationChallengePurpose.LoginSecondFactor, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Json(new { success = false, error = PasskeyOutcomes.ErrorCode(start.Outcome) });
		}

		/// <summary>Finishes the sign-in with a Web passkey; a signature that does not verify counts as a failed attempt.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaPasskey(string requestId, string credential, string returnUrl, bool rememberBrowser,
			CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignInJson(outcome, returnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.Passkey, cancellationToken);
			if (refusal != null)
				return refusal.Value.Restart ? RestartSignInJson(refusal.Value.Outcome, returnUrl) : Json(new { success = false, error = refusal.Value.Error });

			var assertion = await _passkeys.CompleteAssertionAsync(PasskeyCaller.ForLoginTransaction(transaction, user.UserName, SystemAuditSystems.Website,
				IpAddressHelper.GetRequestIP(Request, true)), AuthenticationChallengePurpose.LoginSecondFactor, requestId, credential, cancellationToken);
			if (!assertion.Succeeded)
			{
				// A signature that did not verify is a failed second factor like a wrong code; an expired or reused request is not.
				if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
				{
					var ended = await LoginFactorFailedAsync(transaction, user, MfaEvidenceMethod.Passkey, cancellationToken);
					if (ended != null)
						return RestartSignInJson(ended.Value, returnUrl);
				}

				return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(assertion.Outcome) });
			}

			return FinishedJson(await FinishLoginTransactionAsync(transaction, user, MfaEvidenceMethod.Passkey,
				UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId), assertion.VerifiedOnUtc, returnUrl, rememberBrowser, cancellationToken),
				returnUrl);
		}

		/// <summary>Asks the user's Responder to approve this sign-in; the number is shown on this page only (plan section 7.9).</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaRequestApproval(string returnUrl, CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignInJson(outcome, returnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.PasskeyApproval, cancellationToken);
			if (refusal != null)
				return refusal.Value.Restart ? RestartSignInJson(refusal.Value.Outcome, returnUrl) : Json(new { success = false, error = "approval_unavailable" });

			var start = await _approvals.RequestAsync(MfaApprovalRequester.ForLoginTransaction(transaction, user.UserName,
				IpAddressHelper.GetRequestIP(Request, true), SystemAuditSystems.Website), cancellationToken);
			return start.Succeeded
				? Json(new { success = true, approvalRequestId = start.ApprovalRequestId, matchNumber = start.MatchNumber, expiresIn = start.ExpiresInSeconds })
				: Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(start.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>The state of this sign-in's own approval request: pending, approved, denied, expired or canceled.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaApprovalStatus(string approvalRequestId, string returnUrl, CancellationToken cancellationToken)
		{
			var (transaction, _, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignInJson(outcome, returnUrl);

			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.LoginTransaction, transaction.MfaLoginTransactionId,
				cancellationToken);
			return found.Succeeded
				? Json(new { success = true, state = MfaApprovalOutcomes.StateName(found.Request.EffectiveState(DateTime.UtcNow)) })
				: Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(found.Outcome) ?? "approval_unavailable" });
		}

		/// <summary>Uses this sign-in's approved request once and finishes the sign-in, as the approving passkey and Responder allow.</summary>
		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LoginMfaCompleteApproval(string approvalRequestId, string returnUrl, bool rememberBrowser,
			CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignInJson(outcome, returnUrl);

			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.PasskeyApproval, cancellationToken);
			if (refusal != null)
				return refusal.Value.Restart ? RestartSignInJson(refusal.Value.Outcome, returnUrl) : Json(new { success = false, error = "approval_unavailable" });

			var consumed = await _approvals.ConsumeAsync(approvalRequestId, MfaApprovalRequesterKind.LoginTransaction, transaction.MfaLoginTransactionId,
				transaction.UserId, transaction.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
				return Json(new { success = false, error = MfaApprovalOutcomes.ErrorCode(consumed.Outcome) ?? "approval_unavailable" });

			var approval = consumed.Request;
			return FinishedJson(await FinishLoginTransactionAsync(transaction, user, MfaEvidenceMethod.PasskeyApproval,
				MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId), approval.DecidedOnUtc ?? DateTime.UtcNow,
				returnUrl, rememberBrowser, cancellationToken), returnUrl);
		}

		/// <summary>
		/// A one-time recovery code finishes the sign-in as a recovery session: it never satisfies a later MFA check or protected
		/// data, and the user is sent to replace the lost factor (plan sections 6.1 item 10 and 6.3).
		/// </summary>
		private async Task<IActionResult> LoginTransactionRecoveryCodeAsync(VerifyCodeViewModel model, string returnUrl, CancellationToken cancellationToken)
		{
			var (transaction, user, outcome) = await OpenLoginTransactionAsync(cancellationToken);
			if (transaction == null)
				return RestartSignIn(outcome, returnUrl);

			ViewData["ReturnUrl"] = returnUrl;
			ViewData["BackAction"] = nameof(LoginMfa);
			var refusal = await RefuseLoginMethodAsync(transaction, user, MfaEvidenceMethod.RecoveryCode, cancellationToken);
			if (refusal?.Restart == true)
				return RestartSignIn(refusal.Value.Outcome, returnUrl);
			if (refusal != null || string.IsNullOrWhiteSpace(model?.Code))
			{
				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer[refusal != null ? "LoginMfaMethodUnavailable" : "InvalidRecoveryCode"]);
				return View(model ?? new VerifyCodeViewModel { ReturnUrl = returnUrl });
			}

			var redeemedCode = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, model.Code.Replace(" ", string.Empty).Trim());
			if (!redeemedCode.Succeeded)
			{
				var ended = await LoginFactorFailedAsync(transaction, user, MfaEvidenceMethod.RecoveryCode, cancellationToken);
				if (ended != null)
					return RestartSignIn(ended.Value, returnUrl);

				ModelState.AddModelError(nameof(model.Code), _twoFactorLocalizer["InvalidRecoveryCode"]);
				model.Code = null;
				return View(model);
			}

			// The code is spent even if the sign-in stops below, so the account holder hears about it either way (plan section 6.4).
			await _securityNotices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.RecoveryCodeUsed, ClientApplication = UserSessionClientApplication.Web
			}, cancellationToken);

			var finish = await FinishLoginTransactionAsync(transaction, user, MfaEvidenceMethod.RecoveryCode, null, DateTime.UtcNow, returnUrl, false,
				cancellationToken);
			if (finish.Redirect != null)
				return LocalRedirect(finish.Redirect);
			if (finish.Restart is MfaLoginTransactionOutcome restart)
				return RestartSignIn(restart, returnUrl);

			TempData["LoginMfaMessage"] = _twoFactorLocalizer["LoginMfaMaximumSessions"].Value;
			return RedirectToAction(nameof(LogOn), new { returnUrl = SafeReturnUrl(returnUrl) });
		}

		// ── Forced Password Change (expired password) ─────────────────────────

		[HttpGet]
		[Authorize]
		public async Task<IActionResult> ForcePasswordChange(CancellationToken cancellationToken)
		{
			var userId = HttpContext.Session.GetString("ForcePasswordChangeUserId");
			var deptIdStr = HttpContext.Session.GetString("ForcePasswordChangeDeptId");

			// If there's no pending forced-change session key the user shouldn't be here.
			if (string.IsNullOrWhiteSpace(userId) || !int.TryParse(deptIdStr, out int deptId))
				return RedirectToAction("Dashboard", "Home", new { Area = "User" });

			var minLength = await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(deptId, cancellationToken);
			return View(new ForcePasswordChangeViewModel { MinPasswordLength = minLength });
		}

		[HttpPost]
		[Authorize]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ForcePasswordChange(ForcePasswordChangeViewModel model, CancellationToken cancellationToken)
		{
			var userId = HttpContext.Session.GetString("ForcePasswordChangeUserId");
			var deptIdStr = HttpContext.Session.GetString("ForcePasswordChangeDeptId");

			if (string.IsNullOrWhiteSpace(userId) || !int.TryParse(deptIdStr, out int deptId))
				return RedirectToAction("Dashboard", "Home", new { Area = "User" });

			model.MinPasswordLength = await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(deptId, cancellationToken);

			if (!ModelState.IsValid)
				return View(model);

			// Validate against system complexity + department min-length policy
			var policyError = await _departmentSsoService.ValidatePasswordAgainstPolicyAsync(deptId, model.NewPassword, cancellationToken);
			if (policyError != null)
			{
				ModelState.AddModelError("NewPassword", ResolvePwdError(policyError));
				return View(model);
			}

			var user = await _userManager.FindByIdAsync(userId);
			if (user == null)
				return RedirectToAction("LogOn");

			if (!string.Equals(user.Id, User.FindFirstValue(ClaimTypes.NameIdentifier), StringComparison.Ordinal) ||
				await IsSsoManagedAsync(user.Id, deptId))
				return Forbid();

			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
			if (!result.Succeeded)
			{
				foreach (var error in result.Errors)
					ModelState.AddModelError(string.Empty, error.Description);
				return View(model);
			}

			// Record the password change and clear the forced-change session flags
			await _departmentSsoService.RecordPasswordChangedAsync(deptId, userId, cancellationToken);

			// Clear the must-change-password flag if it was set by an admin
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, deptId);
			if (member != null && member.MustChangePassword)
			{
				member.MustChangePassword = false;
				await _departmentsService.SaveDepartmentMemberAsync(member, cancellationToken);
			}

			HttpContext.Session.Remove("ForcePasswordChangeUserId");
			HttpContext.Session.Remove("ForcePasswordChangeDeptId");

			await _userSessionService.RevokeAllAfterCredentialChangeAsync(userId, userId,
				UserSessionRevocationReason.PasswordChanged, now, cancellationToken);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.PasswordChanged,
				DepartmentId = deptId,
				UserId = userId,
				TargetUserId = userId,
				Username = user.UserName,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				CorrelationId = HttpContext.TraceIdentifier,
				ServerName = Environment.MachineName,
				Successful = true,
				Data = "Forced password change completed; all authentication sessions and tokens revoked."
			}, cancellationToken);
			await _signInManager.SignOutAsync();
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

			return RedirectToAction("LogOn", new { reason = "password-changed" });
		}

		//
		// GET: /Account/LogOff
		// Sign out itself stays POST + antiforgery (see below). Bookmarks, legacy links and the
		// cookie handler's configured LogoutPath still issue a GET here, which would otherwise 404,
		// so render a confirmation the user can submit.
		[HttpGet]
		[ActionName("LogOff")]
		[AllowAnonymous]
		public IActionResult LogOffConfirmation()
		{
			if (User?.Identity == null || !User.Identity.IsAuthenticated)
				return RedirectToAction("LogOn", "Account", new { Area = "" });

			return View();
		}

		//
		// POST: /Account/LogOff
		[HttpPost]
		[Authorize]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LogOff(CancellationToken cancellationToken)
		{
			var sessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			if (!string.IsNullOrWhiteSpace(sessionId))
			{
				var revoked = await _userSessionService.RevokeSessionAsync(
					User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.NameIdentifier),
					sessionId, UserSessionRevocationReason.LoggedOut, cancellationToken);
				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Website,
					Type = (int)SystemAuditTypes.SessionRevoked,
					UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
					TargetUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
					SessionId = sessionId,
					Successful = revoked.RevokedSessionCount > 0,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					CorrelationId = HttpContext.TraceIdentifier,
					Data = "Web session logged out."
				}, cancellationToken);
			}

			await _signInManager.SignOutAsync();
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
				try
				{
					var requestedOn = DateTime.UtcNow;
					var ipAddress = IpAddressHelper.GetRequestIP(Request, true);
					var user = await _userManager.FindByEmailAsync(model.Email);
					if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
					{
						// Count unknown-account requests too. The response is deliberately indistinguishable.
						await _passwordRecoveryService.IssueAsync(null, model.Email, ipAddress, 0, null, cancellationToken);
						return View("ForgotPasswordConfirmation");
					}

					var profile = await _userProfileService.GetProfileByUserIdAsync(user.Id);
					var department = await _departmentsService.GetDepartmentForUserAsync(user.UserName);
					var isSsoManaged = await IsSsoManagedAsync(user.Id, department?.DepartmentId);
					var issue = await _passwordRecoveryService.IssueAsync(isSsoManaged ? null : user.Id,
						model.Email, ipAddress, user.AuthenticationGeneration, user.SecurityStamp, cancellationToken);

					if (!issue.RateLimited && (issue.Issued || isSsoManaged))
					{
						// Put the opaque grant in the URL fragment. Browsers do not send fragments to
						// Resgrid, reverse proxies, or access logs; self-hosted page JavaScript posts it
						// once to establish the HttpOnly recovery cookie.
						var resetPageUrl = Url.Action(nameof(ResetPassword), "Account", null, Request.Scheme);
						var resetUrl = issue.Issued
							? $"{resetPageUrl}#token={Uri.EscapeDataString(issue.Token)}"
							: null;
						await _emailService.SendPasswordRecoveryEmail(user.Email,
							profile?.FullName.AsFirstNameLastName ?? "Resgrid user",
							department?.Name ?? "Resgrid", resetUrl, ipAddress,
							BoundSecurityContext(Request.Headers.UserAgent.ToString(), 512), requestedOn, isSsoManaged);
					}
				}
				catch (Exception ex)
				{
					// Recovery is fail-closed, but the public response stays generic to avoid
					// account enumeration and infrastructure-state disclosure.
					Logging.LogException(ex, "Public password recovery request processing failed.");
				}

				return View("ForgotPasswordConfirmation");
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

		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> ResetPassword(CancellationToken cancellationToken)
		{
			SetPasswordRecoveryResponseHeaders();

			// No query-string grant is accepted: that form writes a single-use secret into access logs,
			// proxy logs and browser history. The emailed link carries the grant in the URL fragment and
			// the page posts it to BeginPasswordReset, which establishes this cookie.
			var token = Request.Cookies[RecoveryGrantCookie];
			var lookup = await _passwordRecoveryService.GetAsync(token, cancellationToken);
			var request = lookup.Request;
			if (!lookup.Found || request == null)
			{
				Response.Cookies.Delete(RecoveryGrantCookie, new CookieOptions { Path = "/Account/ResetPassword" });
				return View(new ResetPasswordViewModel { InvalidOrExpired = true });
			}

			var user = await _userManager.FindByIdAsync(request.UserId);
			var department = user == null ? null : await _departmentsService.GetDepartmentForUserAsync(user.UserName);
			if (user == null || !IsCurrentRecoveryGrant(request, user) ||
				await IsSsoManagedAsync(user.Id, department?.DepartmentId))
			{
				return View(new ResetPasswordViewModel { InvalidOrExpired = true });
			}

			return View(new ResetPasswordViewModel
			{
				MinPasswordLength = department == null
					? 8
					: await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(department.DepartmentId, cancellationToken)
			});
		}

		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> BeginPasswordReset(string token, CancellationToken cancellationToken)
		{
			SetPasswordRecoveryResponseHeaders();
			try
			{
				if (!IsPlausibleRecoveryToken(token) ||
					!(await _passwordRecoveryService.GetAsync(token, cancellationToken)).Found)
					return RedirectToAction(nameof(ResetPassword));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Password recovery grant validation unavailable.");
				return RedirectToAction(nameof(ResetPassword));
			}

			Response.Cookies.Append(RecoveryGrantCookie, token, new CookieOptions
			{
				HttpOnly = true,
				// Never conditional on the per-request scheme: a recovery grant must not travel in clear.
				Secure = true,
				SameSite = SameSiteMode.Strict,
				Expires = DateTimeOffset.UtcNow.AddMinutes(Math.Max(5, SessionSecurityConfig.PublicResetLinkLifetimeMinutes)),
				IsEssential = true,
				Path = "/Account/ResetPassword"
			});
			return RedirectToAction(nameof(ResetPassword));
		}

		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
		{
			SetPasswordRecoveryResponseHeaders();
			var token = Request.Cookies[RecoveryGrantCookie];
			var lookup = await _passwordRecoveryService.GetAsync(token, cancellationToken);
			var request = lookup.Request;
			var user = !lookup.Found || request == null
				? null
				: await _userManager.FindByIdAsync(request.UserId);
			var department = user == null ? null : await _departmentsService.GetDepartmentForUserAsync(user.UserName);
			model.MinPasswordLength = department == null
				? 8
				: await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(department.DepartmentId, cancellationToken);

			if (!lookup.Found || request == null || user == null || !IsCurrentRecoveryGrant(request, user) ||
				await IsSsoManagedAsync(user.Id, department?.DepartmentId))
			{
				model.InvalidOrExpired = true;
				return View(model);
			}

			if (!ModelState.IsValid)
				return View(model);

			if (department != null)
			{
				var policyError = await _departmentSsoService.ValidatePasswordAgainstPolicyAsync(
					department.DepartmentId, model.Password, cancellationToken);
				if (policyError != null)
				{
					ModelState.AddModelError(nameof(model.Password), ResolvePwdError(policyError));
					return View(model);
				}
			}

			if (!await _passwordRecoveryService.TryConsumeAsync(token, cancellationToken))
			{
				model.InvalidOrExpired = true;
				return View(model);
			}

			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			var identityToken = await _userManager.GeneratePasswordResetTokenAsync(user);
			var result = await _userManager.ResetPasswordAsync(user, identityToken, model.Password);
			if (!result.Succeeded)
			{
				// The grant was consumed before the reset ran. Nothing changed, so hand it back rather
				// than forcing the user to request a new recovery email.
				await _passwordRecoveryService.ReleaseAsync(token, cancellationToken);
				foreach (var error in result.Errors)
					ModelState.AddModelError(string.Empty, error.Description);
				return View(model);
			}

			if (department != null)
				await _departmentSsoService.RecordPasswordChangedAsync(department.DepartmentId, user.Id, cancellationToken);

			await _userSessionService.RevokeAllAfterCredentialChangeAsync(user.Id, user.Id, UserSessionRevocationReason.PasswordReset,
				now, cancellationToken);
			await _passwordRecoveryService.RemoveAsync(token, cancellationToken);
			Response.Cookies.Delete(RecoveryGrantCookie, new CookieOptions { Path = "/Account/ResetPassword" });

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.PublicPasswordResetCompleted,
				DepartmentId = department?.DepartmentId,
				UserId = user.Id,
				TargetUserId = user.Id,
				Username = user.UserName,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				CorrelationId = HttpContext.TraceIdentifier,
				ServerName = Environment.MachineName,
				Successful = true,
				Data = "Public password recovery completed; all authentication sessions and tokens revoked."
			}, cancellationToken);

			return View("ResetPasswordConfirmation");
		}

		private async Task<bool> IsSsoManagedAsync(string userId, int? departmentId)
		{
			var state = await _externalIdentityLinkService.GetSsoManagementStateAsync(userId);
			if (state == null || state.IsSsoManaged)
				return true;

			if (!departmentId.HasValue)
				return false;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId.Value);
			return member != null && (!string.IsNullOrWhiteSpace(member.ExternalSsoId) || member.SsoLinkedOn.HasValue);
		}

		private void SetPasswordRecoveryResponseHeaders()
		{
			Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
			Response.Headers.Pragma = "no-cache";
			Response.Headers["Referrer-Policy"] = "no-referrer";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
			Response.Headers["Content-Security-Policy"] =
				"default-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:";
		}

		private static bool IsCurrentRecoveryGrant(PasswordRecoveryRequest request, IdentityUser user)
		{
			if (request == null || user == null ||
				!user.EmailConfirmed ||
				!string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase) ||
				request.AuthenticationGeneration != user.AuthenticationGeneration)
				return false;

			var expected = Encoding.UTF8.GetBytes(request.SecurityStampHash ?? string.Empty);
			var actual = Encoding.UTF8.GetBytes(Convert.ToHexString(
				SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp ?? string.Empty))).ToLowerInvariant());
			return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
		}

		private static bool IsPlausibleRecoveryToken(string token)
		{
			if (string.IsNullOrWhiteSpace(token) || token.Length < 40 || token.Length > 64)
				return false;

			return token.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_');
		}

		/// <summary>
		/// The session's first-factor method for a single sign-on, from the department configuration that authenticated it;
		/// <see cref="UserSessionAuthenticationMethod.LocalPassword"/> when that configuration is gone or disabled.
		/// </summary>
		private async Task<UserSessionAuthenticationMethod> SsoAuthenticationMethodAsync(int departmentId, string departmentSsoConfigId,
			CancellationToken cancellationToken)
		{
			var config = (await _departmentSsoService.GetSsoConfigsForDepartmentAsync(departmentId, cancellationToken))?
				.FirstOrDefault(c => c.IsEnabled && string.Equals(c.DepartmentSsoConfigId, departmentSsoConfigId, StringComparison.Ordinal));
			return (SsoProviderType?)config?.SsoProviderType switch
			{
				SsoProviderType.Oidc => UserSessionAuthenticationMethod.OidcSso,
				SsoProviderType.Saml2 => UserSessionAuthenticationMethod.SamlSso,
				_ => UserSessionAuthenticationMethod.LocalPassword
			};
		}

		private async Task<bool> IsPasswordLoginAllowedAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			if (!await _externalIdentityLinkService.IsLocalLoginAllowedAsync(user.Id, cancellationToken))
				return false;

			var department = await _departmentsService.GetDepartmentForUserAsync(user.UserName);
			if (department == null)
				return true;

			if (!await _externalIdentityLinkService.IsLocalLoginAllowedAsync(
					user.Id, department.DepartmentId, cancellationToken))
				return false;

			var requiresSso = await _departmentSsoService.IsRequireSsoPolicyActiveAsync(
				department.DepartmentId, cancellationToken);
			return !requiresSso ||
				!await _departmentSsoService.IsSsoEnabledForDepartmentAsync(department.DepartmentId, cancellationToken);
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
			model.DepartmentFull = !await _limitsService.CanDepartmentAddNewUserAsync(department.DepartmentId, true);

			return View(model);
		}

		[AllowAnonymous]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteInvite(CompleteInviteModel model, CancellationToken cancellationToken)
		{
			if (!Guid.TryParse(model.Code, out var code))
				return RedirectToAction("MissingInvite");

			model.Invite = await _invitesService.GetInviteByCodeAsync(code);

			if (model.Invite == null)
				return RedirectToAction("MissingInvite");

			if (model.Invite.CompletedOn.HasValue)
				return RedirectToAction("CompletedInvite");

			model.DepartmentName = (await _departmentsService.GetDepartmentByIdAsync(model.Invite.DepartmentId, true))?.Name;
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

			// The new member would take a personnel seat: at the plan's limit no account is created (fresh counts, not the 14-day cache).
			model.DepartmentFull = !await _limitsService.CanDepartmentAddNewUserAsync(model.Invite.DepartmentId, true);

			if (ModelState.IsValid && !model.DepartmentFull)
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

					await _emailService.SendWelcomeEmail(department.Name, $"{model.FirstName} {model.LastName}", model.Email, model.UserName, model.Invite.DepartmentId);

					if (!await SignInTrackedWebSessionAsync(user,
						UserSessionAuthenticationMethod.LocalPassword, TimeSpan.FromHours(8), cancellationToken,
						new VerifiedFactor(MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password, DateTime.UtcNow)))
					{
						ModelState.AddModelError(string.Empty,
							"Your department's maximum number of active sessions has been reached.");
						return View(model);
					}

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
			// Whitelist the shipped locales: this anonymous endpoint gets scanner garbage ("'",
			// paths, SQL fragments) and RequestCulture/CultureInfo throw on invalid names.
			// Anything not supported is silently ignored instead of turning into a 500.
			var supported = String.IsNullOrWhiteSpace(culture)
				? null
				: Resgrid.Localization.SupportedLocales.GetSupportedCultures()
					.FirstOrDefault(c => String.Equals(c, culture.Trim(), StringComparison.OrdinalIgnoreCase));

			if (supported != null)
			{
				Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(supported)), new CookieOptions { Expires = DateTime.UtcNow.AddYears(1) });
				// This guy I think is causing issues with like DateTime rendering mm/dd/yy vs dd/mm/yy, so need to look into that more. -SJ
				//Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
				Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(supported);

				if (!String.IsNullOrWhiteSpace(returnUrl))
					return RedirectToLocal(returnUrl);
			}

			return RedirectToAction("LogOn");
		}

		#region Helpers
		private static string BoundSecurityContext(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value))
				return "Unknown";

			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
			return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength);
		}

		private Task<bool> SignInTrackedWebSessionAsync(IdentityUser user,
			UserSessionAuthenticationMethod authenticationMethod, TimeSpan lifetime, CancellationToken cancellationToken,
			params VerifiedFactor[] verifiedFactors) =>
			SignInTrackedWebSessionCoreAsync(user, authenticationMethod, lifetime, null, null, null, null, verifiedFactors, cancellationToken);

		/// <param name="loginMfaMethod">The second factor that completed this sign-in, stored on the session with its reference.</param>
		private Task<bool> SignInTrackedWebSessionAsync(IdentityUser user,
			UserSessionAuthenticationMethod authenticationMethod, TimeSpan lifetime, CancellationToken cancellationToken,
			MfaEvidenceMethod? loginMfaMethod, string loginMfaFactorReference, params VerifiedFactor[] verifiedFactors) =>
			SignInTrackedWebSessionCoreAsync(user, authenticationMethod, lifetime, loginMfaMethod, loginMfaFactorReference, null, null, verifiedFactors,
				cancellationToken);

		/// <param name="departmentId">The department the session is for; the user's active department when null.</param>
		/// <param name="departmentSsoConfigId">The SSO configuration that signed the user in, for a single sign-on session.</param>
		private async Task<bool> SignInTrackedWebSessionCoreAsync(IdentityUser user, UserSessionAuthenticationMethod authenticationMethod, TimeSpan lifetime,
			MfaEvidenceMethod? loginMfaMethod, string loginMfaFactorReference, int? departmentId, string departmentSsoConfigId,
			VerifiedFactor[] verifiedFactors, CancellationToken cancellationToken)
		{
			if (user == null)
				throw new InvalidOperationException("The authenticated user could not be loaded.");

			var principal = await _signInManager.CreateUserPrincipalAsync(user);
			string trackedSessionId = null;
			var expiresUtc = DateTimeOffset.UtcNow.Add(lifetime);
			if (SessionSecurityConfig.TrackingEnabled)
			{
				var (sharedWorkstation, deviceName) = WebInstallation();
				var department = departmentId is int chosen
					? new Department { DepartmentId = chosen }
					: await _departmentsService.GetDepartmentByUserIdAsync(user.Id);
				UserSession session;
				try
				{
					session = await _userSessionService.CreateSessionAsync(new SessionIssueContext
					{
						UserId = user.Id,
						DepartmentId = department?.DepartmentId,
						AuthenticationGeneration = user.AuthenticationGeneration,
						ClientApplication = UserSessionClientApplication.Web,
						DeviceName = deviceName,
						DeviceType = Request.Headers["X-Resgrid-Device-Type"],
						OperatingSystem = Request.Headers["X-Resgrid-Operating-System"],
						Browser = Request.Headers["X-Resgrid-Browser"],
						AuthenticationMethod = authenticationMethod,
						ExpiresOn = DateTime.UtcNow.Add(lifetime),
						IpAddress = IpAddressHelper.GetRequestIP(Request, true),
						UserAgent = Request.Headers["User-Agent"],
						LoginMfaMethod = loginMfaMethod,
						LoginMfaFactorReference = loginMfaFactorReference,
						DepartmentSsoConfigId = departmentSsoConfigId,
						SharedModeRequested = sharedWorkstation
					}, cancellationToken);
				}
				catch (SessionCreationDeniedException)
				{
					await _signInManager.SignOutAsync();
					await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
					return false;
				}

				if (principal.Identity is ClaimsIdentity identity)
				{
					identity.AddClaim(new Claim(SessionClaimTypes.SessionId, session.UserSessionId));
				}

				trackedSessionId = session.UserSessionId;
				if (session.SharedMode)
				{
					// The cookie never outlives the shift ceiling the server set (plan section 10.5).
					var shiftEnds = new DateTimeOffset(DateTime.SpecifyKind(session.ExpiresOn, DateTimeKind.Utc));
					if (shiftEnds < expiresUtc)
						expiresUtc = shiftEnds;
					await AuditSharedSessionStartedAsync(user, session, cancellationToken);
				}
			}

			await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
				new AuthenticationProperties
				{
					IssuedUtc = DateTimeOffset.UtcNow,
					ExpiresUtc = expiresUtc,
					IsPersistent = false,
					AllowRefresh = false
				});

			await RecordSignInEvidenceAsync(user, MfaEvidenceSession.KeyForNewSignIn(trackedSessionId, HttpContext), verifiedFactors,
				cancellationToken);
			return true;
		}

		private async Task AuditSharedSessionStartedAsync(IdentityUser user, UserSession session, CancellationToken cancellationToken)
		{
			try
			{
				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Website,
					Type = (int)SystemAuditTypes.SharedSessionStarted,
					DepartmentId = session.DepartmentId,
					UserId = user.Id,
					Username = user.UserName,
					TargetUserId = user.Id,
					SessionId = SharedSessionAudit.SessionSuffix(session.UserSessionId),
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					CorrelationId = HttpContext.TraceIdentifier,
					Data = SharedSessionAudit.Describe("started", session, "shared workstation"),
					LoggedOn = DateTime.UtcNow
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The session exists; a missing audit row must not undo the sign-in.
				Resgrid.Framework.Logging.LogException(ex, "Shared session start audit failed.");
			}
		}

		/// <summary>
		/// Records the factors this sign-in verified as server-side evidence for the new session (passkey plan section 5.3).
		/// A failure here never blocks the sign-in; the only effect is that a later credential change asks the user to
		/// reauthenticate first.
		/// </summary>
		private async Task RecordSignInEvidenceAsync(IdentityUser user, string sessionKey, VerifiedFactor[] verifiedFactors,
			CancellationToken cancellationToken)
		{
			if (sessionKey == null || verifiedFactors == null || verifiedFactors.Length == 0)
				return;

			foreach (var factor in verifiedFactors)
			{
				try
				{
					await _mfaEvidenceService.RecordAsync(user.Id, sessionKey, UserSessionClientApplication.Web, factor.Kind,
						factor.Method, MfaEvidencePurpose.Login, factor.VerifiedOnUtc, user.AuthenticationGeneration,
						factorReference: factor.FactorReference, cancellationToken: cancellationToken);
				}
				catch (Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "Failed to record sign-in MFA evidence.");
				}
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
