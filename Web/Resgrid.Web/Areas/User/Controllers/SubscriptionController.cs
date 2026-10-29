using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Billing.Api;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Options;
using Stripe;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus;
using Resgrid.Services;
using Resgrid.Web.Helpers;
using Resgrid.Web.Attributes;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[ClaimsResource(ResgridClaimTypes.Resources.Department)]
	// Billing must stay available while an ADP migration window holds the department operation
	// lock: the managing member manages the addon (and can revoke a scheduled offboarding) here,
	// and billing state is not department operational data (plan sections 17, 20.2).
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public class SubscriptionController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly ISubscriptionsService _subscriptionsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEmailService _emailService;
		private readonly IAffiliateService _affiliateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IOptions<AppOptions> _appOptionsAccessor;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IAdpBillingService _adpBillingService;

		public SubscriptionController(IDepartmentsService departmentsService, IUsersService usersService, IDepartmentGroupsService departmentGroupsService,
			Model.Services.IAuthorizationService authorizationService, ISubscriptionsService subscriptionsService, IPersonnelRolesService personnelRolesService, IUnitsService unitsService,
			IDepartmentSettingsService departmentSettingsService, IEmailService emailService, IAffiliateService affiliateService,
			IUserProfileService userProfileService, IOptions<AppOptions> appOptionsAccessor, IEventAggregator eventAggregator,
			IDepartmentDataProtectionService dataProtectionService, IFeatureToggleService featureToggleService, IAdpBillingService adpBillingService)
		{
			_dataProtectionService = dataProtectionService;
			_featureToggleService = featureToggleService;
			_adpBillingService = adpBillingService;
			_departmentsService = departmentsService;
			_usersService = usersService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
			_subscriptionsService = subscriptionsService;
			_personnelRolesService = personnelRolesService;
			_unitsService = unitsService;
			_departmentSettingsService = departmentSettingsService;
			_emailService = emailService;
			_affiliateService = affiliateService;
			_userProfileService = userProfileService;
			_appOptionsAccessor = appOptionsAccessor;
			_eventAggregator = eventAggregator;
		}

		#endregion Private Members and Constructors

		#region Advanced Data Protection addon

		/// <summary>
		/// Every ADP catalog row for this data center. Resolved by TYPE rather than by a hardcoded id like the PTT pages use: there are
		/// two, the yearly checkout (AdpAddonConfig, M0298) and the legacy subscription item (M0126) that departments which bought it
		/// keep, and holding either one is holding the add-on.
		/// </summary>
		private async Task<List<PlanAddon>> GetAdpAddonPlansAsync() =>
			(await _subscriptionsService.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ADP) ?? new List<PlanAddon>()).Where(p => p != null).ToList();

		/// <summary>
		/// Plan 17.1: every ADP billing action is restricted to the department's managing member,
		/// server-side. Not "an administrator" — enrolling commits the department's data to a key it
		/// then depends on, and cancelling starts the migration that undoes it, so both stay with the
		/// single person who owns the account.
		/// </summary>
		private async Task<bool> IsAdpManagingMemberAsync()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			return department != null && !string.IsNullOrWhiteSpace(department.ManagingUserId)
				&& string.Equals(department.ManagingUserId, UserId, StringComparison.OrdinalIgnoreCase);
		}

		private async Task<AdpBillingStatus> GetAdpCheckoutStatusAsync()
		{
			try
			{
				return await _adpBillingService.GetAsync(DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP billing status failed for DepartmentId: {DepartmentId}");
				return null;
			}
		}

		/// <summary>
		/// Loads everything both ADP addon pages render. Billing facts come from the addon rows and
		/// protection facts from the policy, and they are kept apart on purpose — see AdpAddonView.
		/// </summary>
		private async Task<AdpAddonView> BuildAdpAddonViewAsync(string result = null)
		{
			var model = new AdpAddonView();

			var plans = await GetAdpAddonPlansAsync();
			model.PlanAddon = plans.FirstOrDefault(p => p.IsAdpYearlyCheckout) ?? plans.FirstOrDefault();
			if (model.PlanAddon == null)
				return null;

			model.PlanAddonId = model.PlanAddon.PlanAddonId;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.IsManagingMember = model.Department != null
				&& !string.IsNullOrWhiteSpace(model.Department.ManagingUserId)
				&& string.Equals(model.Department.ManagingUserId, UserId, StringComparison.OrdinalIgnoreCase);

			model.Price = AddonCatalog.CurrencySymbol + AddonCatalog.Get(PlanAddonTypes.ADP).Price.ToString("N0", Cultures.UnitedStates);

			var currentPlan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
			model.HasPaidPlan = currentPlan != null && currentPlan.Cost > 0;
			model.Offered = await IsAdpOfferedAsync();

			var addons = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId,
				plans.Select(p => p.PlanAddonId).ToList());

			var addon = addons?.OrderByDescending(x => x.EndingOn).FirstOrDefault();
			if (addon != null)
			{
				model.HasAddon = true;
				model.IsCancelled = addon.IsCancelled;
				model.EndingOn = addon.EndingOn;
				model.IsLegacyItem = !plans.Any(p => p.IsAdpYearlyCheckout && p.PlanAddonId == addon.PlanAddonId);
			}

			// The yearly checkout's own status: whether a checkout can open right now, and whether its subscription can be cancelled.
			var status = await GetAdpCheckoutStatusAsync();
			model.CheckoutProvider = status?.Provider;
			model.CanPurchaseOnline = status?.CheckoutAvailable == true;
			model.CanCancelYearly = status?.CanCancel == true && status.Cancelled != true;
			model.Result = result is AddonBillingResults.Complete or AddonBillingResults.Cancelled ? result : null;

			// Protection state is read fresh: a member who has just enrolled or cancelled is looking
			// at this page precisely to see whether it took effect.
			var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(DepartmentId, bypassCache: true);
			model.ProtectionState = policy == null
				? DepartmentDataProtectionState.Disabled
				: (DepartmentDataProtectionState)policy.State;
			model.PaidThroughOn = policy?.AddonPaidThroughOn;
			model.GraceEndsOn = policy?.AddonGraceEndsOn;
			model.OffboardingEffectiveOn = policy?.OffboardingEffectiveOn;

			return model;
		}

		/// <summary>ADP is offered while its enrollment toggle is on for the department: without enrollment the add-on buys nothing.</summary>
		private async Task<bool> IsAdpOfferedAsync()
		{
			try
			{
				return (await _featureToggleService.EvaluateFreshAsync(FeatureFlagKeys.DepartmentProtectedDataEnrollment, DepartmentId))?.IsEnabled == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unable to evaluate the ADP enrollment toggle for DepartmentId: {DepartmentId}");
				return false;
			}
		}

		private void AuditAdp(string after, bool successful)
		{
			var auditEvent = new AuditEvent();
			auditEvent.Before = null;
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.AddonSubscriptionModified;
			auditEvent.After = after;
			auditEvent.Successful = successful;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);
		}

		/// <summary>Purchase page for the ADP addon (plan 17.1).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> BuyAdpAddon()
		{
			var model = await BuildAdpAddonViewAsync();
			if (model == null)
				return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load the Advanced Data Protection add-on. Please try again.");

			// An active addon belongs on the management page; sending them there beats rendering a
			// buy button that the POST would refuse.
			if (model.HasAddon && !model.IsCancelled)
				return RedirectToAction("ManageAdpAddon", "Subscription", new { Area = "User" });

			// Not offered while the enrollment toggle is off; the Subscription page hides the card for the same reason.
			if (!model.Offered && !model.HasAddon)
				return RedirectToAction("Index", "Subscription", new { Area = "User" });

			return View(model);
		}

		/// <summary>
		/// Starts the ADP yearly checkout: its own Stripe or Paddle subscription through the Billing API, whatever the base plan's
		/// interval. Returns only a validated Stripe Checkout URL or Paddle transaction id. The provider's webhook is what activates
		/// the addon; nothing about protection changes here — the department enrolls afterwards, from the Data Protection page.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> AdpCheckout()
		{
			// Re-checked here rather than trusted from the page: the GET only decides what to draw.
			if (!await IsAdpManagingMemberAsync())
				return Forbid();

			var currentPlan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
			if (currentPlan == null || currentPlan.Cost <= 0 || !await IsAdpOfferedAsync())
				return StatusCode(StatusCodes.Status409Conflict);

			AdpCheckout checkout = null;
			try
			{
				checkout = await _adpBillingService.BeginCheckoutAsync(DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP checkout failed for DepartmentId: {DepartmentId}");
			}

			// Audited AFTER the provider call, with the provider's own answer: recorded first it would claim a purchase the
			// Billing API may then have refused, which is the one thing an addon audit trail must never do.
			var valid = false;
			if (checkout?.Provider == "Stripe" && Uri.TryCreate(checkout.Url, UriKind.Absolute, out var url) &&
				url.Scheme == "https" && url.Host == "checkout.stripe.com" && string.IsNullOrEmpty(url.UserInfo))
				valid = true;
			else if (checkout?.Provider == "Paddle" && System.Text.RegularExpressions.Regex.IsMatch(checkout.TransactionId ?? "", "^txn_[a-z0-9]{26}$"))
				valid = true;
			AuditAdp($"ADP yearly checkout started ({checkout?.Provider ?? "none"})", valid);

			if (!valid)
				return StatusCode(StatusCodes.Status503ServiceUnavailable);
			return checkout.Provider == "Stripe" ? Json(new { url = checkout.Url }) : Json(new { transactionId = checkout.TransactionId });
		}

		/// <summary>Management page for an ADP addon the department already holds (plan 17.1).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ManageAdpAddon(string result = null, int attempt = 0)
		{
			var model = await BuildAdpAddonViewAsync(result);
			if (model == null)
				return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load the Advanced Data Protection add-on. Please try again.");

			model.Attempt = Math.Clamp(attempt, 0, 100);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> CancelAdpAddon(CancellationToken cancellationToken)
		{
			try
			{
				if (!await IsAdpManagingMemberAsync())
					return Unauthorized();

				// Cancels the BILLING subscription only. Protection keeps running until the provider's
				// cancellation event reaches Core and the offboarding migration it schedules actually
				// runs; nothing here touches a key or a ciphertext.
				//
				// The yearly checkout's subscription stops renewing at the end of the paid year; a legacy subscription item is removed
				// from the base subscription as before.
				var status = await GetAdpCheckoutStatusAsync();
				var cancelled = status?.CanCancel == true
					? await _adpBillingService.CancelRenewalAsync(DepartmentId)
					: await _subscriptionsService.CancelPlanAddonByTypeFromStripeAsync(DepartmentId, (int)PlanAddonTypes.ADP);

				// Audited AFTER the call, with the provider's own answer: recorded first it claimed a
				// cancellation the billing API may then have refused.
				AuditAdp(status?.CanCancel == true ? "ADP yearly renewal cancelled" : "ADP addon cancelled", cancelled);

				return RedirectToAction("ManageAdpAddon", "Subscription", new { Area = "User" });
			}
			catch (Exception ex)
			{
				Logging.SendExceptionEmail(ex, "CancelAdpAddon", DepartmentId, UserName);

				return RedirectToAction("PaymentFailed", "Subscription",
					new { Area = "User", chargeId = "", errorMessage = ex.Message });
			}
		}

		#endregion Advanced Data Protection addon

		/// <summary>
		/// What the add-on cards show. An add-on is offered only while its operator toggle is on for the department; one the department
		/// already holds stays listed after its toggle goes off, so it can still be managed or cancelled. A single Billing API read
		/// covers every held add-on except Push-To-Talk, whose per-pack subscription item is read from Stripe by the caller.
		/// </summary>
		private async Task<List<AddonAvailability>> GetAddonAvailabilityAsync(PlanAddon pttAddon)
		{
			var plans = new List<PlanAddon>();
			var held = new List<PaymentAddon>();
			try
			{
				plans = await _subscriptionsService.GetAllAddonPlansAsync() ?? new List<PlanAddon>();
				var planIds = plans.Where(x => x.AddonType != (int)PlanAddonTypes.PTT).Select(x => x.PlanAddonId).ToList();
				held = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, planIds) ?? new List<PaymentAddon>();
			}
			catch (Exception ex)
			{
				// The plan section of the page must still render; held add-ons are then simply not listed.
				Logging.LogException(ex, $"Unable to load add-on payments for DepartmentId: {DepartmentId}");
			}

			var result = new List<AddonAvailability>();
			foreach (var definition in AddonCatalog.All)
			{
				var offered = true;
				if (definition.FeatureFlag != null)
				{
					try
					{
						offered = (await _featureToggleService.EvaluateFreshAsync(definition.FeatureFlag, DepartmentId))?.IsEnabled == true;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Unable to evaluate {definition.FeatureFlag} for DepartmentId: {DepartmentId}");
						offered = false;
					}
				}

				if (definition.Type == PlanAddonTypes.PTT)
				{
					result.Add(new AddonAvailability
					{
						Definition = definition, Offered = offered, Owned = pttAddon != null, Cancelled = pttAddon?.IsCancelled == true,
						EndsOn = pttAddon?.IsCancelled == true ? pttAddon.EndingOn : null
					});
					continue;
				}

				var typePlanIds = plans.Where(x => x.AddonType == (int)definition.Type).Select(x => x.PlanAddonId).ToHashSet();
				var rows = held.Where(x => x != null && typePlanIds.Contains(x.PlanAddonId)).ToList();
				var cancelled = rows.Any() && rows.All(x => x.IsCancelled);
				var endsOn = rows.Any() ? rows.Max(x => x.EndingOn) : (DateTime?)null;
				result.Add(new AddonAvailability
				{
					Definition = definition, Offered = offered, Owned = rows.Any(), Cancelled = cancelled,
					// A manual or invoiced grant runs on the DateTime.MaxValue sentinel; it has no end date to show.
					EndsOn = cancelled && endsOn.HasValue && endsOn.Value.Date != DateTime.MaxValue.Date ? endsOn : null
				});
			}

			return result;
		}

		private static bool ShouldUsePaddleForSubscriptionFlow(Payment currentPayment, string paddleCustomerId)
		{
			if (!string.IsNullOrWhiteSpace(paddleCustomerId))
				return true;

			if (currentPayment != null && !currentPayment.IsFreePlan())
				return currentPayment.Method == (int)PaymentMethods.Paddle;

			return Config.PaymentProviderConfig.IsPaddleActive();
		}

		private static (string PaddleEnvironment, string PaddleClientToken, bool CanInitializePaddleCheckout, string PaddleConfigurationError) GetPaddleCheckoutConfiguration(bool isPaddleDepartment)
		{
			if (!isPaddleDepartment)
				return (string.Empty, string.Empty, false, null);

			var paddleEnvironment = Config.PaymentProviderConfig.GetPaddleEnvironment();
			var paddleClientToken = Config.PaymentProviderConfig.GetPaddleClientToken();
			var canInitializePaddleCheckout =
				Config.PaymentProviderConfig.IsValidPaddleEnvironment(paddleEnvironment)
				&& Config.PaymentProviderConfig.IsValidPaddleClientToken(paddleClientToken);

			return (
				paddleEnvironment,
				paddleClientToken,
				canInitializePaddleCheckout,
				isPaddleDepartment && !canInitializePaddleCheckout
					? GetPaddleConfigurationError(paddleEnvironment, paddleClientToken)
					: null);
		}

		private static string GetPaddleConfigurationError(string paddleEnvironment, string paddleClientToken)
		{
			if (string.IsNullOrWhiteSpace(paddleClientToken))
				return "Paddle checkout is not configured. A valid client-side token is required.";

			if (!Config.PaymentProviderConfig.IsValidPaddleClientToken(paddleClientToken))
				return "Paddle checkout is misconfigured. The configured client-side token must use Paddle's documented live_... or test_... format.";

			if (!Config.PaymentProviderConfig.IsValidPaddleEnvironment(paddleEnvironment))
				return "Paddle checkout is misconfigured. The configured environment must be sandbox or production.";

			return null;
		}

		private static string GetPaddleCheckoutProductId(Resgrid.Model.Plan plan)
		{
			return plan?.GetExternalKey() ?? string.Empty;
		}

		[HttpGet]
		[Authorize]
		public async Task<IActionResult> SelectRegistrationPlan(string discountCode = null)
		{
			var currentPayment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);
			if (currentPayment != null && !currentPayment.IsFreePlan())
				return RedirectToAction("Dashboard", "Home", new { Area = "User" });

			var model = new SelectRegistrationPlanView();
			model.DepartmentId = DepartmentId;
			model.StripeKey = Config.PaymentProviderConfig.GetStripeClientKey();
			model.DiscountCode = discountCode;

			var paddleCustomerId = await _departmentSettingsService.GetPaddleCustomerIdForDepartmentAsync(DepartmentId);
			bool isPaddleDepartment = ShouldUsePaddleForSubscriptionFlow(currentPayment, paddleCustomerId);
			model.IsPaddleDepartment = isPaddleDepartment;
			var paddleCheckoutConfiguration = GetPaddleCheckoutConfiguration(isPaddleDepartment);
			model.PaddleEnvironment = paddleCheckoutConfiguration.PaddleEnvironment;
			model.PaddleClientToken = paddleCheckoutConfiguration.PaddleClientToken;
			model.CanInitializePaddleCheckout = paddleCheckoutConfiguration.CanInitializePaddleCheckout;
			model.PaddleConfigurationError = paddleCheckoutConfiguration.PaddleConfigurationError;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index()
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			var model = new SubscriptionView();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(DepartmentId);
			model.Payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);
			model.IsTestingDepartment = await _departmentSettingsService.IsTestingEnabledForDepartmentAsync(DepartmentId);
			model.Department = department;
			model.StripeKey = Config.PaymentProviderConfig.GetStripeClientKey();
			model.StripeCustomer = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);

			if (model.Plan != null && model.Plan.PlanId != 1 && model.Plan.Cost == 0)
			{
				if (model.Payment != null)
				{
					model.Plan.Cost = model.Payment.Amount;
					model.Plan.Quantity = model.Payment.Quantity;
				}
			}

			var allPayments = await _subscriptionsService.GetAllPaymentsForDepartmentAsync(DepartmentId);
			if (allPayments != null)
				model.HadStripePaymentIn30Days = allPayments.Any(x => x.EndingOn >= DateTime.UtcNow.AddYears(-2) && x.Method == (int)PaymentMethods.Stripe);
			else
				model.HadStripePaymentIn30Days = false;

			if (model.Payment != null)
			{
				// DateTime.MaxValue loses sub-millisecond precision when it crosses the billing API's
				// JSON boundary. Treat any value on its sentinel date as non-expiring so it is not
				// shifted into year 10000 when the department has a positive UTC offset.
				if (model.Payment.EndingOn.Date == DateTime.MaxValue.Date)
					model.Expires = "Never";
				else
					model.Expires = TimeConverterHelper.TimeConverter(model.Payment.EndingOn, department).ToString("D");
			}
			else
			{
				model.Expires = "Never";
			}

			// The Subscription/Index view dereferences Model.Payment (e.g. Payment.Cancelled) without a
			// null check. Departments with no current payment (free/unpaid, or when the billing API returns
			// no payment) would otherwise throw a NullReferenceException while rendering the view. Mirror the
			// Plan fallback below so the page renders as an active, never-expiring plan instead of erroring.
			if (model.Payment == null)
				model.Payment = new Resgrid.Model.Payment { Cancelled = false, Amount = 0, Quantity = 1, EndingOn = DateTime.MaxValue };

			if (model.Plan != null)
			{
				model.PossibleUpgrades = _subscriptionsService.GetPossibleUpgradesForPlan(model.Plan.PlanId);
				model.PossibleDowngrades = _subscriptionsService.GetPossibleDowngradesForPlan(model.Plan.PlanId);
			}
			else
			{
				model.PossibleUpgrades = _subscriptionsService.GetPossibleUpgradesForPlan(1);
				model.PossibleDowngrades = _subscriptionsService.GetPossibleUpgradesForPlan(1);

				model.Plan = new Resgrid.Model.Plan() { PlanId = 1, Cost = 0, Name = "Forever Free" };
			}

			var personnelCount = (await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId)).Count;
			var unitsCount = (await _unitsService.GetUnitsForDepartmentUnlimitedAsync(DepartmentId)).Count;

			if (model.Plan.PlanId >= 36)
			{
				model.PersonnelCount = personnelCount + unitsCount;
				model.PersonnelLimit = model.Plan.GetLimitForType(PlanLimitTypes.Entities);
				float personnelLimit;
				if (float.TryParse(model.Plan.GetLimitForType(PlanLimitTypes.Entities), out personnelLimit))
				{
					float personLimit = (model.PersonnelCount / personnelLimit) * 100f;
					model.PersonnelBarPrecent = personLimit.ToString();

					if (personLimit >= 100)
					{
						ViewBag.PersonnelBarStyle = "progress-bar-danger";
						SetSubscriptionErrorMessage();
					}
					else if (personLimit >= 75)
						ViewBag.PersonnelBarStyle = "progress-bar-warning";
					else
						ViewBag.PersonnelBarStyle = "progress-bar-info";
				}
				else
				{
					model.PersonnelBarPrecent = "0.0";
				}
			}
			else
			{
				model.PersonnelCount = personnelCount;
				model.PersonnelLimit = model.Plan.GetLimitForType(PlanLimitTypes.Personnel);
				float personnelLimit;
				if (float.TryParse(model.Plan.GetLimitForType(PlanLimitTypes.Personnel), out personnelLimit))
				{
					float personLimit = (model.PersonnelCount / personnelLimit) * 100f;
					model.PersonnelBarPrecent = personLimit.ToString();

					if (personLimit >= 100)
					{
						ViewBag.PersonnelBarStyle = "progress-bar-danger";
						SetSubscriptionErrorMessage();
					}
					else if (personLimit >= 75)
						ViewBag.PersonnelBarStyle = "progress-bar-warning";
					else
						ViewBag.PersonnelBarStyle = "progress-bar-info";
				}
				else
				{
					model.PersonnelBarPrecent = "0.0";
				}
			}


			var addon = await _subscriptionsService.GetPTTAddonPlanForDepartmentFromStripeAsync(DepartmentId);

			model.HasActiveSubscription = await _subscriptionsService.HasActiveSubForDepartmentFromStripeAsync(DepartmentId);
			model.HasActiveAddon = addon != null;

			model.AddonFrequencyString = "month";
			if (model.Plan != null)
			{
				if (model.Plan.Frequency == (int)PlanFrequency.Yearly)
					model.AddonFrequencyString = "year";
				else if (model.Plan.Frequency == (int)PlanFrequency.Monthly)
					model.AddonFrequencyString = "month";
			}

			if (addon != null && addon.IsCancelled)
			{
				model.IsAddonCanceled = addon.IsCancelled;
				model.AddonEndingOn = addon.EndingOn;
			}

			var addonPlan = await _subscriptionsService.GetPTTAddonForCurrentSubAsync(DepartmentId);

			if (addonPlan != null)
			{
				model.AddonCost = addonPlan.Cost.ToString("C0", Cultures.UnitedStates);
				model.AddonCost2 = (addonPlan.Cost / 2).ToString("C0", Cultures.UnitedStates);
				model.AddonPlanIdToBuy = addonPlan.PlanAddonId;
			}
			else
				model.AddonCost = "0";

			model.Addons = await GetAddonAvailabilityAsync(addon);

			// Discount / affiliate code panel: the outcome of the last post, and a valid code still waiting for a checkout. Peek keeps the
			// pending code for the checkout request that follows; it lives only in this browser's TempData.
			model.AffiliateCode = department?.AffiliateCode;
			model.CodesMessage = TempData[CodesMessageKey] as string;
			model.PendingDiscountCode = TempData.Peek(PendingDiscountCodeKey) as string;

			var paddleCustomerId = await _departmentSettingsService.GetPaddleCustomerIdForDepartmentAsync(DepartmentId);
			bool isPaddleDepartment = ShouldUsePaddleForSubscriptionFlow(model.Payment, paddleCustomerId);
			model.IsPaddleDepartment = isPaddleDepartment;

			if (isPaddleDepartment)
			{
				model.PaddleCustomer = paddleCustomerId;
				var paddleCheckoutConfiguration = GetPaddleCheckoutConfiguration(isPaddleDepartment);
				model.PaddleEnvironment = paddleCheckoutConfiguration.PaddleEnvironment;
				model.PaddleClientToken = paddleCheckoutConfiguration.PaddleClientToken;
				model.CanInitializePaddleCheckout = paddleCheckoutConfiguration.CanInitializePaddleCheckout;
				model.PaddleConfigurationError = paddleCheckoutConfiguration.PaddleConfigurationError;
			}
			else
			{
				var user = _usersService.GetUserById(UserId);

				try
				{
					var session = await _subscriptionsService.CreateStripeSessionForCustomerPortal(DepartmentId, model.StripeCustomer, "", user.Email, department.Name);

					if (session != null)
						model.StripeCustomerPortalUrl = session.Url;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> UpdateBillingInfo()
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			var model = new BuyNowView();

			if (Config.PaymentProviderConfig.IsTestMode)
				model.StripeKey = Config.PaymentProviderConfig.TestClientKey;
			else
				model.StripeKey = Config.PaymentProviderConfig.ProductionClientKey;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> UpdateBillingInfo(IFormCollection form, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			try
			{
				var user = _usersService.GetUserById(UserId);
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				var stripeCustomerId = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);

				var cardToken = form["stripeToken"];

				var cardService = new CardService();
				var customerService = new CustomerService();

				var updateCardOptions = new CardCreateOptions();
				updateCardOptions.Source = new AnyOf<string, CardCreateNestedOptions>(cardToken);

				Card stripeCard = await cardService.CreateAsync(stripeCustomerId, updateCardOptions, cancellationToken: cancellationToken);

				var customerOptions = new CustomerUpdateOptions
				{
					Email = user.Email,
					Description = department.Name,
					DefaultSource = stripeCard.Id
				};

				Customer stripeCustomer = await customerService.UpdateAsync(stripeCustomerId, customerOptions, cancellationToken: cancellationToken);

				var auditEvent = new AuditEvent();
				auditEvent.Before = updateCardOptions.CloneJsonToString();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.SubscriptionBillingInfoUpdated;
				auditEvent.After = stripeCustomer.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("BillingInfoUpdateSuccess", "Subscription", new { Area = "User" });
			}
			catch (Exception ex)
			{
				Logging.SendExceptionEmail(ex, "UpdateBillingInfo", DepartmentId, UserName);

				return RedirectToAction("PaymentFailed", "Subscription",
						new { Area = "User", chargeId = "", errorMessage = ex.Message });
			}
		}

		// Called by the Update Billing Info page's Stripe card-token callback, so it takes the same rule as that page: a signed-in
		// department admin. Bounded so a caller cannot write an arbitrarily large provider-event row.
		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> LogStripeResponse(StripeResponseInput input, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			const int maxLoggedLength = 2000;
			string Bounded(string value) => value == null ? null : value.Length > maxLoggedLength ? value.Substring(0, maxLoggedLength) : value;

			var providerEvent = new PaymentProviderEvent();
			providerEvent.ProviderType = (int)PaymentMethods.Stripe;
			providerEvent.RecievedOn = DateTime.UtcNow;
			providerEvent.Data = $"Card Token Result: UserId:{UserId} DepartmentId:{DepartmentId} Status:{Bounded(input?.Status)} Response:{Bounded(input?.Response)}";
			providerEvent.Processed = false;
			providerEvent.CustomerId = "SYSTEM";

			await _subscriptionsService.SavePaymentEventAsync(providerEvent, cancellationToken);

			return new EmptyResult();
		}

		/// <summary>
		/// A discount code entered after signup. Applied to the active subscription right away; with no active subscription yet it is
		/// held for this browser session and carried into checkout (the Billing API pre-applies it to the Stripe session or Paddle
		/// transaction). The Billing API resolves Stripe promotion codes and Paddle discount codes, so no provider key is needed here.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ApplyDiscountCode(string discountCode)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			var code = discountCode?.Trim();
			if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
			{
				TempData[CodesMessageKey] = "DiscountInvalid";
				return RedirectToAction("Index", "Subscription", new { Area = "User" });
			}

			var result = await _subscriptionsService.ApplyDiscountCodeToDepartmentAsync(DepartmentId, code);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.SubscriptionUpdated;
			auditEvent.After = $"Discount code entered: {result.Outcome}";
			auditEvent.Successful = result.Outcome == DiscountCodeOutcomes.Applied || result.Outcome == DiscountCodeOutcomes.NoActiveSubscription;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			switch (result.Outcome)
			{
				case DiscountCodeOutcomes.Applied:
					TempData[CodesMessageKey] = "DiscountApplied";
					break;
				case DiscountCodeOutcomes.NoActiveSubscription:
					TempData[CodesMessageKey] = "DiscountPending";
					TempData[PendingDiscountCodeKey] = code;
					break;
				case DiscountCodeOutcomes.InvalidCode:
					TempData[CodesMessageKey] = "DiscountInvalid";
					break;
				default:
					TempData[CodesMessageKey] = "DiscountUnavailable";
					break;
			}

			return RedirectToAction("Index", "Subscription", new { Area = "User" });
		}

		/// <summary>
		/// Records the affiliate (referral) code for a department that did not enter one at signup. Set once: an attribution already on
		/// the department is never replaced from here, so a referral cannot be moved to another affiliate after the fact.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ApplyAffiliateCode(string affiliateCode, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, true);
			if (department == null)
				return RedirectToAction("Index", "Subscription", new { Area = "User" });

			if (!string.IsNullOrWhiteSpace(department.AffiliateCode))
			{
				TempData[CodesMessageKey] = "AffiliateAlreadySet";
				return RedirectToAction("Index", "Subscription", new { Area = "User" });
			}

			var code = affiliateCode?.Trim();
			var affiliate = string.IsNullOrWhiteSpace(code) || code.Length > 64 ? null : await _affiliateService.GetActiveAffiliateByCodeAsync(code);
			if (affiliate == null)
			{
				TempData[CodesMessageKey] = "AffiliateInvalid";
				return RedirectToAction("Index", "Subscription", new { Area = "User" });
			}

			department.AffiliateCode = affiliate.AffiliateCode;
			await _departmentsService.SaveDepartmentAsync(department, cancellationToken);
			await _departmentsService.InvalidateAllDepartmentsCache(DepartmentId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.SubscriptionUpdated;
			auditEvent.Before = null;
			auditEvent.After = $"Affiliate code set: {affiliate.AffiliateCode}";
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			TempData[CodesMessageKey] = "AffiliateApplied";
			return RedirectToAction("Index", "Subscription", new { Area = "User" });
		}

		private const string CodesMessageKey = "SubscriptionCodesMessage";
		private const string PendingDiscountCodeKey = "SubscriptionPendingDiscountCode";

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ValidateCoupon(string couponCode)
		{
			var service = new CouponService();
			Coupon coupon = null;

			try
			{
				if (!String.IsNullOrWhiteSpace(couponCode))
					coupon = await service.GetAsync(couponCode.Trim().ToUpper());
			}
			catch
			{
			}

			if (coupon == null || (coupon.RedeemBy.HasValue && coupon.RedeemBy.Value < DateTime.UtcNow))
				return Content("Invalid");

			return Content("Valid");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Cancel()
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			CancelView model = new CancelView();
			model.Payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync((await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId);
			// GetCurrentPaymentForDepartmentAsync returns null when the Billing API is unavailable; without a
			// current payment there is nothing to cancel and model.Payment.PlanId would NRE.
			if (model.Payment == null)
				return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });

			model.Plan = await _subscriptionsService.GetPlanByIdAsync(model.Payment.PlanId);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> BillingInfoUpdateSuccess()
		{
			return View();
		}


		[HttpGet]
		public async Task<IActionResult> StripeBillingInfoUpdateSuccess(string sessionId)
		{
			var model = new PaymentCompleteView();
			model.SessionId = sessionId;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[RequiresRecentTwoFactor]
		public async Task<IActionResult> Cancel(CancelView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			if (!model.Confirm)
				ModelState.AddModelError("Confirm", "You must check the confirm box to cancel the subscription.");

			if (ModelState.IsValid)
			{
				var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);
				if (payment == null)
					return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });

				if (payment.Method == (int)PaymentMethods.Paddle)
				{
					var paddleCustomerId = await _departmentSettingsService.GetPaddleCustomerIdForDepartmentAsync(DepartmentId);

					if (!String.IsNullOrWhiteSpace(paddleCustomerId))
					{
						var result = await _subscriptionsService.CancelPaddleSubscriptionAsync(paddleCustomerId);

						var auditEvent = new AuditEvent();
						auditEvent.Before = paddleCustomerId;
						auditEvent.DepartmentId = DepartmentId;
						auditEvent.UserId = UserId;
						auditEvent.Type = AuditLogTypes.SubscriptionCancelled;
						auditEvent.After = result.ToString();
						auditEvent.Successful = result;
						auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
						auditEvent.ServerName = Environment.MachineName;
						auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
						_eventAggregator.SendMessage<AuditEvent>(auditEvent);

						if (result)
							return RedirectToAction("CancelSuccess", "Subscription", new { Area = "User" });
						else
							return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });
					}
					else
					{
						return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });
					}
				}
				else if (payment.Method == (int)PaymentMethods.Stripe)
				{
					var stripeCustomerId = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);

					if (String.IsNullOrWhiteSpace(stripeCustomerId))
					{
						var user = _usersService.GetUserById(UserId);
						var cusService = new CustomerService();
						var options = new CustomerListOptions
						{
							Email = user.Email
						};

						var customerList = await cusService.ListAsync(options, cancellationToken: cancellationToken);

						if (customerList != null && customerList.Any())
							stripeCustomerId = customerList.First().Id;
					}

					if (!String.IsNullOrWhiteSpace(stripeCustomerId))
					{
						var subscriptionService = new SubscriptionService();
						var subs = await subscriptionService.ListAsync(new SubscriptionListOptions { Customer = stripeCustomerId }, cancellationToken: cancellationToken);
						Subscription subscription = subs.First(sub => !sub.EndedAt.HasValue);

						var cancelledSub = await subscriptionService.CancelAsync(subscription.Id, new SubscriptionCancelOptions { }, cancellationToken: cancellationToken);

						var auditEvent = new AuditEvent();
						auditEvent.Before = JsonConvert.SerializeObject(subscription);
						auditEvent.DepartmentId = DepartmentId;
						auditEvent.UserId = UserId;
						auditEvent.Type = AuditLogTypes.SubscriptionCancelled;
						auditEvent.After = JsonConvert.SerializeObject(cancelledSub);
						auditEvent.Successful = true;
						auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
						auditEvent.ServerName = Environment.MachineName;
						auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
						_eventAggregator.SendMessage<AuditEvent>(auditEvent);

						if (cancelledSub != null && cancelledSub.Status.Equals("canceled", StringComparison.InvariantCultureIgnoreCase))
						{
							return RedirectToAction("CancelSuccess", "Subscription", new { Area = "User" });
						}
						else
						{
							return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });
						}
					}
					else
					{
						return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });
					}

				}
			}

			model.Payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync((await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId);
			// GetCurrentPaymentForDepartmentAsync returns null when the Billing API is unavailable; without a
			// current payment there is nothing to cancel and model.Payment.PlanId would NRE.
			if (model.Payment == null)
				return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });

			model.Plan = await _subscriptionsService.GetPlanByIdAsync(model.Payment.PlanId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> BuyAddon(string planAddonId)
		{
			var model = new BuyAddonView();
			model.PlanAddon = await _subscriptionsService.GetPlanAddonByIdAsync(planAddonId);
			// GetPlanAddonByIdAsync returns null when the add-on id isn't found (or the Billing API is
			// unavailable). Bail before dereferencing model.PlanAddon below (PlanAddonId / AddonType / PlanId).
			if (model.PlanAddon == null)
				return NotFound();

			// Readiness Pro, Business Operations and Enhanced AI use a dedicated monthly checkout, not the legacy PTT quantity flow.
			if (PlanAddon.IsDedicatedAddon(model.PlanAddon))
				return NotFound();

			model.PlanAddonId = model.PlanAddon.PlanAddonId;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var addonTypes = await _subscriptionsService.GetAllAddonPlansAsync();

			var addons = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId,
				addonTypes.Where(x => x.AddonType == model.PlanAddon.AddonType).Select(y => y.PlanAddonId).ToList());

			if (addons != null && addons.Count > 0)
				model.CurrentPaymentAddon = addons.FirstOrDefault();

			if (model.PlanAddon.PlanId.HasValue)
			{
				var plan = await _subscriptionsService.GetPlanByIdAsync(model.PlanAddon.PlanId.Value);
				// GetPlanByIdAsync returns null when the Billing API is unavailable / the plan isn't found.
				// Guard before dereferencing so a billing outage can't NRE this page.
				if (plan != null)
					model.Frequency = ((PlanFrequency)plan.Frequency).ToString();
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ManagePTTAddon()
		{
			var model = new BuyAddonView();
			model.PlanAddon = await _subscriptionsService.GetPlanAddonByIdAsync("6f4c5f8b-584d-4291-8a7d-29bf97ae6aa9");
			// GetPlanAddonByIdAsync returns null when the Billing API is unavailable / the add-on isn't found.
			// The id here is hardcoded (a known product), so a null means a server/billing problem, not a bad
			// request — surface a 500 instead of NRE'ing on model.PlanAddon below.
			if (model.PlanAddon == null)
				return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load the PTT add-on. Please try again.");

			model.PlanAddonId = model.PlanAddon.PlanAddonId;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			//var addons = await _subscriptionsService.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId,
			//	new List<string>(){SubscriptionsService.PTT10UserAddonPackage});

			var stripeCustomer = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);

			var addon = await _subscriptionsService.GetActivePTTStripeSubscriptionAsync(stripeCustomer);

			if (addon != null)
			{
				model.Quantity = addon.TotalQuantity;
			}

			/*
						if (addons != null && addons.Count > 0)
							model.CurrentPaymentAddon = addons.FirstOrDefault();

						var planAddons = await _subscriptionsService.GetCurrentPlanAddonsForDepartmentFromStripeAsync(DepartmentId);

						if (planAddons != null && planAddons.Any())
						{
							foreach (var addon in planAddons)
							{
								if (!addon.IsCancelled)
									model.Quantity += addon.Quantity;
							}
						}

						if (model.PlanAddon.PlanId.HasValue)
						{
							var plan = await _subscriptionsService.GetPlanByIdAsync(model.PlanAddon.PlanId.Value);
							model.Frequency = ((PlanFrequency)plan.Frequency).ToString();
						}

						*/

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ManagePTTAddon(BuyAddonView model)
		{
			try
			{
				var user = _usersService.GetUserById(UserId);

				var addonPlan = await _subscriptionsService.GetPlanAddonByIdAsync(model.PlanAddonId);
				var plan = await _subscriptionsService.GetPlanByIdAsync(addonPlan.PlanId.Value);



				var result = await _subscriptionsService.AddAddonAddedToExistingSub(DepartmentId, plan, addonPlan);

				return RedirectToAction("PaymentComplete", "Subscription", new { Area = "User", planId = plan.PlanId });
			}
			catch (Exception ex)
			{
				Logging.SendExceptionEmail(ex, "BuyNow", DepartmentId, UserName);

				return RedirectToAction("PaymentFailed", "Subscription",
						new { Area = "User", chargeId = "", errorMessage = ex.Message });
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> BuyAddon(BuyAddonView model, CancellationToken cancellationToken)
		{
			try
			{
				var addonPlan = await _subscriptionsService.GetPlanAddonByIdAsync(model.PlanAddonId);
				if (addonPlan != null && PlanAddon.IsDedicatedAddon(addonPlan))
					return NotFound();

				var user = _usersService.GetUserById(UserId);
				var currentAddonPayments = await _subscriptionsService.GetCurrentPlanAddonsForDepartmentFromStripeAsync(DepartmentId);

				if (addonPlan != null)
				{
					var stripeCustomerId = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);

					var auditEvent = new AuditEvent();
					auditEvent.Before = null;
					auditEvent.DepartmentId = DepartmentId;
					auditEvent.UserId = UserId;
					auditEvent.Type = AuditLogTypes.AddonSubscriptionModified;
					auditEvent.After = model.Quantity.ToString();
					auditEvent.Successful = true;
					auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
					auditEvent.ServerName = Environment.MachineName;
					auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
					_eventAggregator.SendMessage<AuditEvent>(auditEvent);

					var result = await _subscriptionsService.ModifyPTTAddonSubscriptionAsync(stripeCustomerId, model.Quantity, addonPlan);

					if (result)
						return RedirectToAction("PaymentComplete", "Subscription", new { Area = "User", planId = 0 });
					else
						return RedirectToAction("PaymentFailed", "Subscription", new { Area = "User", chargeId = "", errorMessage = "Unknown Error" });
				}
				else
				{
					return RedirectToAction("PaymentFailed", "Subscription", new { Area = "User", chargeId = "", errorMessage = "Unknown Addon Plan" });
				}
			}
			catch (Exception ex)
			{
				Logging.SendExceptionEmail(ex, "BuyNow", DepartmentId, UserName);

				return RedirectToAction("PaymentFailed", "Subscription",
						new { Area = "User", chargeId = "", errorMessage = ex.Message });
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> CancelAddon(int addonTypeId)
		{

			switch ((PlanAddonTypes)addonTypeId)
			{
				case PlanAddonTypes.PTT:
					var addonPttPlan = await _subscriptionsService.GetPTTAddonPlanForDepartmentFromStripeAsync(DepartmentId);

					if (addonPttPlan != null)
					{
						var result = await _subscriptionsService.CancelPlanAddonByTypeFromStripeAsync(DepartmentId, addonTypeId);
					}
					break;
				default:
					break;
			}

			return RedirectToAction("Index", "Subscription");
		}


		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> GetStripeSession(int id, int count, string discountCode = null, CancellationToken cancellationToken = default)
		{
			if (count < 1 || count > 200)
				return BadRequest("Invalid entity pack count.");

			var plan = await _subscriptionsService.GetPlanByIdAsync(id);
			// GetPlanByIdAsync returns null when the Billing API is unavailable / the plan isn't found.
			// Fail with a clear error instead of NRE'ing on plan.GetExternalKey()/plan.PlanId below.
			if (plan == null)
				return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load the selected plan. Please try again.");

			var stripeCustomerId = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var user = _usersService.GetUserById(UserId);
			var session = await _subscriptionsService.CreateStripeSessionForSub(DepartmentId, stripeCustomerId, plan.GetExternalKey(), plan.PlanId, user.Email, department.Name, count, discountCode);
			// CreateStripeSessionForSub returns null when the Billing API is unavailable / Stripe session
			// creation fails. Fail with a clear error instead of NRE'ing on session.CustomerId/SessionId below.
			if (session == null)
				return StatusCode(StatusCodes.Status500InternalServerError, "Unable to start the checkout session. Please try again.");

			var subscription = await _subscriptionsService.GetActiveStripeSubscriptionAsync(session.CustomerId);

			bool hasActiveSub = false;
			if (subscription != null)
				hasActiveSub = true;

			return Json(new
			{
				SessionId = session.SessionId,
				HasActiveSub = hasActiveSub
			});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> GetStripeUpdate()
		{
			//var plan = await _subscriptionsService.GetPlanById(id);
			var stripeCustomerId = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var user = _usersService.GetUserById(UserId);
			var session = await _subscriptionsService.CreateStripeSessionForUpdate(DepartmentId, stripeCustomerId, user.Email, department.Name);

			return Json(new
			{
				SessionId = session.SessionId
			});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> GetPaddleCheckout(int id, int count, string discountCode = null, CancellationToken cancellationToken = default)
		{
			if (count < 1 || count > 200)
				return BadRequest("Invalid entity pack count.");

			var plan = await _subscriptionsService.GetPlanByIdAsync(id);
			var paddleProductId = GetPaddleCheckoutProductId(plan);

			if (string.IsNullOrWhiteSpace(paddleProductId))
				return StatusCode(StatusCodes.Status500InternalServerError, "Paddle checkout is not configured for this plan.");

			var paddleCustomerId = await _departmentSettingsService.GetPaddleCustomerIdForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var user = _usersService.GetUserById(UserId);
			var checkout = await _subscriptionsService.CreatePaddleCheckoutForSub(DepartmentId, paddleCustomerId, paddleProductId, plan.PlanId, user.Email, department.Name, count, discountCode);

			if (checkout == null || (string.IsNullOrWhiteSpace(checkout.TransactionId) && string.IsNullOrWhiteSpace(checkout.PriceId)))
			{
				if (plan.PlanId == 36 || plan.PlanId == 37)
					return StatusCode(StatusCodes.Status502BadGateway, "Paddle checkout could not be created because the billing service did not return a transaction or price for this product-based entity plan.");

				return StatusCode(StatusCodes.Status502BadGateway, "Paddle checkout could not be created because the billing service did not return checkout data.");
			}

			bool hasActiveSub = false;
			if (!string.IsNullOrWhiteSpace(paddleCustomerId))
			{
				var subscription = await _subscriptionsService.GetActivePaddleSubscriptionAsync(paddleCustomerId);
				if (subscription != null)
					hasActiveSub = true;
			}

			return Json(new
			{
				TransactionId = checkout?.TransactionId,
				PriceId = checkout?.PriceId,
				CustomerId = checkout?.CustomerId,
				Environment = checkout?.Environment,
				HasActiveSub = hasActiveSub
			});
		}

		public async Task<IActionResult> PaddleProcessing(int planId)
		{
			ProcessingView model = new ProcessingView();
			model.PlanId = planId;

			return View("Processing", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ManagePaddlePTTAddon()
		{
			var model = new BuyAddonView();
			var paddleAddonId = Config.PaymentProviderConfig.GetPaddlePTT10UserAddonPackageId();
			model.PlanAddon = string.IsNullOrWhiteSpace(paddleAddonId) ? null : await _subscriptionsService.GetPlanAddonByIdAsync(paddleAddonId);
			// An unconfigured Paddle PTT package (or an unreachable Billing API) used to NRE here.
			if (model.PlanAddon == null)
				return StatusCode(StatusCodes.Status500InternalServerError, "Unable to load the PTT add-on. Please try again.");

			model.PlanAddonId = model.PlanAddon.PlanAddonId;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var paddleCustomer = await _departmentSettingsService.GetPaddleCustomerIdForDepartmentAsync(DepartmentId);

			var addon = await _subscriptionsService.GetActivePTTPaddleSubscriptionAsync(paddleCustomer);

			if (addon != null)
			{
				model.Quantity = addon.TotalQuantity;
			}

			// The shared view posts to the Stripe BuyAddon action by default; Paddle pack changes go to ManagePaddlePTTAddon.
			ViewBag.PttPostAction = nameof(ManagePaddlePTTAddon);
			return View("ManagePTTAddon", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> ManagePaddlePTTAddon(BuyAddonView model)
		{
			try
			{
				var addonPlan = await _subscriptionsService.GetPlanAddonByIdAsync(model.PlanAddonId);
				var paddleCustomer = await _departmentSettingsService.GetPaddleCustomerIdForDepartmentAsync(DepartmentId);

				var auditEvent = new AuditEvent();
				auditEvent.Before = null;
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.AddonSubscriptionModified;
				auditEvent.After = model.Quantity.ToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				var result = await _subscriptionsService.ModifyPaddlePTTAddonSubscriptionAsync(paddleCustomer, model.Quantity, addonPlan);

				if (result)
					return RedirectToAction("PaymentComplete", "Subscription", new { Area = "User", planId = 0 });
				else
					return RedirectToAction("PaymentFailed", "Subscription", new { Area = "User", chargeId = "", errorMessage = "Unknown Error" });
			}
			catch (Exception ex)
			{
				Logging.SendExceptionEmail(ex, "ManagePaddlePTTAddon", DepartmentId, UserName);

				return RedirectToAction("PaymentFailed", "Subscription",
						new { Area = "User", chargeId = "", errorMessage = ex.Message });
			}
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> CancelSuccess()
		{
			return View();
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> CancelFailure()
		{
			return View();
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> PaymentComplete(int paymentId)
		{
			PaymentCompleteView model = new PaymentCompleteView();
			model.PaymentId = paymentId;

			return View(model);
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> UnableToPurchase()
		{
			UnableToPurchaseView model = new UnableToPurchaseView();

			model.CurrentPayment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);
			model.NextPayment = await _subscriptionsService.GetUpcomingPaymentForDepartmentAsync(DepartmentId);

			return View(model);
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> PaymentFailed(string chargeId, string errorMessage)
		{
			PaymentFailedView model = new PaymentFailedView();
			model.ChargeId = chargeId;
			model.ErrorMessage = errorMessage;

			return View(model);
		}

		public async Task<IActionResult> PaymentPending()
		{
			PaymentFailedView model = new PaymentFailedView();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> PaymentHistory()
		{
			// The same rule as opening one of these invoices (CanUserViewPaymentAsync): department admins only.
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				return Unauthorized();

			PaymentHistoryView model = new PaymentHistoryView();
			model.Payments = await _subscriptionsService.GetAllPaymentsForDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			return View(model);
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> ViewInvoice(int paymentId)
		{
			if (!await _authorizationService.CanUserViewPaymentAsync(UserId, paymentId))
				return Unauthorized();

			ViewInvoiceView model = new ViewInvoiceView();
			model.Payment = await _subscriptionsService.GetPaymentByIdAsync(paymentId);

			if (!String.IsNullOrWhiteSpace(model.Payment.Data))
			{
				try
				{
					model.Charge = JsonConvert.DeserializeObject<Charge>(model.Payment.Data);
				}
				catch { }
			}

			return View(model);
		}

		public async Task<IActionResult> Processing(int planId)
		{
			ProcessingView model = new ProcessingView();
			model.PlanId = planId;

			return View(model);
		}

		public async Task<IActionResult> StripeProcessing(int planId, string sessionId)
		{
			ProcessingView model = new ProcessingView();
			model.PlanId = planId;
			model.SessionId = sessionId;

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> CheckProcessingStatus(int planId)
		{
			var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);

			if (payment != null && payment.PlanId == planId && payment.PurchaseOn.ToShortDateString() == DateTime.UtcNow.ToShortDateString())
				return Json("1");

			return Json("0");
		}

		private void SetSubscriptionErrorMessage()
		{
			ViewBag.SubscriptionErrorMessage =
				"It appears that you have more entities then your current plan allows. Don't worry they have not been deleted, but to re-enable access to them you need to purchase a higher plan. Note that users, groups or units that are the the ones past the limit (by date added) may not be visible or able to use the system.";
		}
	}
}
