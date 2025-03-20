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

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[ClaimsResource(ResgridClaimTypes.Resources.Department)]
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

		public SubscriptionController(IDepartmentsService departmentsService, IUsersService usersService, IDepartmentGroupsService departmentGroupsService,
			Model.Services.IAuthorizationService authorizationService, ISubscriptionsService subscriptionsService, IPersonnelRolesService personnelRolesService, IUnitsService unitsService,
			IDepartmentSettingsService departmentSettingsService, IEmailService emailService, IAffiliateService affiliateService,
			IUserProfileService userProfileService, IOptions<AppOptions> appOptionsAccessor, IEventAggregator eventAggregator)
		{
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

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index()
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				Unauthorized();

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
				if (model.Payment.EndingOn == DateTime.MaxValue)
					model.Expires = "Never";
				else
					model.Expires = TimeConverterHelper.TimeConverter(model.Payment.EndingOn, department).ToString("D");
			}
			else
			{
				model.Expires = "Never";
			}

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

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> UpdateBillingInfo()
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				Unauthorized();

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
		public async Task<IActionResult> UpdateBillingInfo(IFormCollection form, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				Unauthorized();

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

		[HttpPost]

		public async Task<IActionResult> LogStripeResponse(StripeResponseInput input, CancellationToken cancellationToken)
		{
			var providerEvent = new PaymentProviderEvent();
			providerEvent.ProviderType = (int)PaymentMethods.Stripe;
			providerEvent.RecievedOn = DateTime.UtcNow;
			providerEvent.Data = $"Card Token Result: UserId:{UserId} DepartmentId:{DepartmentId} Status:{input.Status} Response:{input.Response}";
			providerEvent.Processed = false;
			providerEvent.CustomerId = "SYSTEM";

			await _subscriptionsService.SavePaymentEventAsync(providerEvent, cancellationToken);

			return new EmptyResult();
		}

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
				Unauthorized();

			CancelView model = new CancelView();
			model.Payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync((await _departmentsService.GetDepartmentByUserIdAsync(UserId)).DepartmentId);
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
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Cancel(CancelView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserManageSubscriptionAsync(UserId, DepartmentId))
				Unauthorized();

			if (!model.Confirm)
				ModelState.AddModelError("Confirm", "You must check the confirm box to cancel the subscription.");

			if (ModelState.IsValid)
			{
				var payment = await _subscriptionsService.GetCurrentPaymentForDepartmentAsync(DepartmentId);
				if (payment == null)
					return RedirectToAction("CancelFailure", "Subscription", new { Area = "User" });

				if (payment.Method == (int)PaymentMethods.Stripe)
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
			model.Plan = await _subscriptionsService.GetPlanByIdAsync(model.Payment.PlanId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> BuyAddon(string planAddonId)
		{
			var model = new BuyAddonView();
			model.PlanAddon = await _subscriptionsService.GetPlanAddonByIdAsync(planAddonId);
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
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> BuyAddon(BuyAddonView model, CancellationToken cancellationToken)
		{
			try
			{
				var user = _usersService.GetUserById(UserId);

				var addonPlan = await _subscriptionsService.GetPlanAddonByIdAsync(model.PlanAddonId);
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

		[HttpGet]
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
		public async Task<IActionResult> GetStripeSession(int id, int count, CancellationToken cancellationToken)
		{
			var plan = await _subscriptionsService.GetPlanByIdAsync(id);
			var stripeCustomerId = await _departmentSettingsService.GetStripeCustomerIdForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var user = _usersService.GetUserById(UserId);
			var session = await _subscriptionsService.CreateStripeSessionForSub(DepartmentId, stripeCustomerId, plan.GetExternalKey(), plan.PlanId, user.Email, department.Name, count);
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

		//[AuthorizeUpdate]
		public async Task<IActionResult> PaymentHistory()
		{
			PaymentHistoryView model = new PaymentHistoryView();
			model.Payments = await _subscriptionsService.GetAllPaymentsForDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			return View(model);
		}

		//[AuthorizeUpdate]
		public async Task<IActionResult> ViewInvoice(int paymentId)
		{
			if (!await _authorizationService.CanUserViewPaymentAsync(UserId, paymentId))
				Unauthorized();

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
