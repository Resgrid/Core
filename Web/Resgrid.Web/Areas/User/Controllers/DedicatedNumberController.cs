using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Departments;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department → Dedicated Number (call-intake-and-ai-destinations-plan.md D1/D5): the department's own Twilio number, bought with the
	/// Dedicated Number add-on. Department admins search and provision it, switch SMS / voice intake, release it, and bring a lapsed
	/// number back. The managing member buys the add-on here or on its billing page and cancels it there. Replaces the hidden legacy
	/// provisioning on Text Messaging Settings, which no longer buys numbers.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class DedicatedNumberController : SecureBaseController
	{
		private const string MessageKey = "DedicatedNumberMessage";
		private const string ErrorKey = "DedicatedNumberError";

		private readonly IDepartmentPhoneNumbersService _numbers;
		private readonly IDedicatedNumberAccessService _access;
		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly Model.Services.IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Department.DedicatedNumber> _localizer;
		private readonly IDedicatedNumberBillingService _billing;

		public DedicatedNumberController(IDepartmentPhoneNumbersService numbers, IDedicatedNumberAccessService access, IEnhancedAiAccessService enhancedAi,
			Model.Services.IAuthorizationService authorization, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.Department.DedicatedNumber> localizer, IDedicatedNumberBillingService billing)
		{
			_numbers = numbers;
			_access = access;
			_enhancedAi = enhancedAi;
			_authorization = authorization;
			_departments = departments;
			_localizer = localizer;
			_billing = billing;
		}

		/// <summary>
		/// The page, and the number search (plan §8 D13) when a country is given: state / province, city, postal code, area code, digits or
		/// near the department's address. A held number whose department stopped paying shows its intake stop and release dates (D10/D12).
		/// A department that may buy the add-on buys it here too (the managing member only, through DedicatedNumberBilling/Checkout);
		/// the provider returns with <paramref name="result"/> and the page refreshes itself until the payment activates the add-on.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Index(string country = null, string areaCode = null, string contains = null, string region = null, string city = null,
			string postalCode = null, bool near = false, int? distance = null, string result = null, int attempt = 0, CancellationToken cancellationToken = default)
		{
			var access = await _access.GetAccessAsync(DepartmentId);
			var checkout = await GetCheckoutAsync(access);
			var rows = await _numbers.GetAllForDepartmentAsync(DepartmentId);
			var active = rows.FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Active);
			var lapsed = rows.FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Lapsed);
			var provisioning = rows.FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Provisioning);
			var failure = rows.FirstOrDefault(n => n.Status == (int)DepartmentPhoneNumberStatuses.Failed);
			if (failure != null && rows.Any(n => n.IsHeld || n.CreatedOn > failure.CreatedOn))
				failure = null;

			var message = TempData[MessageKey] as string;
			var error = TempData[ErrorKey] as string;

			var searched = false;
			var results = Array.Empty<AvailablePhoneNumber>();
			if (!string.IsNullOrWhiteSpace(country) && access.HasActiveAddon && active == null && lapsed == null && provisioning == null)
			{
				searched = true;
				try
				{
					results = (await _numbers.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch
					{
						CountryCode = country, AreaCode = areaCode, Contains = contains, Region = region, Locality = city, PostalCode = postalCode,
						NearDepartment = near, DistanceMiles = distance
					}, cancellationToken)).ToArray();
				}
				catch (DedicatedNumberException ex)
				{
					error = ErrorText(ex.Error);
				}
			}

			var voiceIntake = active != null && await _enhancedAi.CanUseAsync(DepartmentId, FeatureFlagKeys.AiVoiceIntake);

			// Dates on the page are the department's local dates.
			var activeSchedule = DedicatedNumberReleaseSchedule.For(active);
			var lapsedSchedule = DedicatedNumberReleaseSchedule.For(lapsed);
			var department = activeSchedule != null || lapsedSchedule != null || active != null || lapsed != null
				? await _departments.GetDepartmentByIdAsync(DepartmentId, false)
				: null;

			return View(new DedicatedNumberView
			{
				Access = access,
				Department = department?.DepartmentId == DepartmentId ? department : null,
				Active = active,
				Lapsed = lapsed,
				ActiveSchedule = activeSchedule,
				LapsedSchedule = lapsedSchedule,
				Provisioning = provisioning,
				LastFailure = failure,
				VoiceIntakeAvailable = voiceIntake,
				Searched = searched,
				Country = (country ?? access.AllowedCountries?.FirstOrDefault() ?? "").Trim().ToUpperInvariant(),
				AreaCode = areaCode,
				Contains = contains,
				Region = region,
				City = city,
				PostalCode = postalCode,
				Near = near,
				Distance = distance is > 0 ? Math.Min(distance.Value, DedicatedNumberSearch.MaxDistanceMiles) : DedicatedNumberSearch.DefaultDistanceMiles,
				Results = results,
				Message = message,
				Error = error,
				IsOwner = checkout.IsOwner,
				Billing = checkout.Billing,
				CheckoutResult = result is AddonBillingResults.Complete or AddonBillingResults.Cancelled ? result : null,
				CheckoutAttempt = Math.Clamp(attempt, 0, 100)
			});
		}

		/// <summary>
		/// Whether this member may buy the add-on on the page, and the Billing API's offer (provider, price, checkout availability). Only
		/// asked while the department may buy (an active paid entity plan, the add-on offered and not held) and only for the managing
		/// member, who alone may check out; a billing failure leaves the page with a link to the add-on billing page instead.
		/// </summary>
		private async Task<(bool IsOwner, DedicatedNumberBillingStatus Billing)> GetCheckoutAsync(DedicatedNumberAccess access)
		{
			if (access.HasActiveAddon || !access.HasBillingRelationship || !access.IsOffered)
				return (false, null);

			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			if (department?.DepartmentId != DepartmentId || department.ManagingUserId != UserId)
				return (false, null);

			try
			{
				return (true, await _billing.GetAsync(DepartmentId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dedicated Number billing status failed for DepartmentId: {DepartmentId}");
				return (true, null);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Provision(string phoneNumber, CancellationToken cancellationToken)
		{
			if (!await _authorization.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			return await RunAsync(async () =>
			{
				var number = await _numbers.ProvisionAsync(DepartmentId, phoneNumber, UserId, cancellationToken);
				if (number.Status == (int)DepartmentPhoneNumberStatuses.Active)
					TempData[MessageKey] = string.Format(_localizer["Provisioned"].Value, number.PhoneNumber);
				else
					TempData[ErrorKey] = _localizer["ProvisionFailed"].Value;
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> UpdateIntake(bool smsIntakeEnabled, bool voiceIntakeEnabled, CancellationToken cancellationToken)
		{
			if (!await _authorization.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			return await RunAsync(async () =>
			{
				await _numbers.UpdateIntakeOptionsAsync(DepartmentId, smsIntakeEnabled, voiceIntakeEnabled, UserId, cancellationToken);
				TempData[MessageKey] = _localizer["IntakeSaved"].Value;
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Release(string departmentPhoneNumberId, bool confirmRelease, CancellationToken cancellationToken)
		{
			if (!await _authorization.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			if (!confirmRelease)
			{
				TempData[ErrorKey] = _localizer["ReleaseConfirmRequired"].Value;
				return RedirectToAction(nameof(Index));
			}

			// Only a number this department holds; another department's id is simply not found.
			var number = (await _numbers.GetAllForDepartmentAsync(DepartmentId))
				.FirstOrDefault(n => n.DepartmentPhoneNumberId == departmentPhoneNumberId && n.IsHeld && n.Status != (int)DepartmentPhoneNumberStatuses.Provisioning);
			if (number == null)
				return NotFound();

			return await RunAsync(async () =>
			{
				var released = await _numbers.ReleaseAsync(number.DepartmentPhoneNumberId, cancellationToken);
				if (released)
					TempData[MessageKey] = _localizer["Released"].Value;
				else
					TempData[ErrorKey] = _localizer["ReleaseFailed"].Value;
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Reactivate(CancellationToken cancellationToken)
		{
			if (!await _authorization.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			return await RunAsync(async () =>
			{
				var number = await _numbers.ReactivateAsync(DepartmentId, UserId, cancellationToken);
				if (number == null)
					TempData[ErrorKey] = ErrorText(DedicatedNumberErrors.NoActiveNumber);
				else
					TempData[MessageKey] = _localizer["Reactivated"].Value;
			});
		}

		/// <summary>Runs an action and lands back on the page with its outcome; a refusal is shown to the admin, never logged as a fault.</summary>
		private async Task<IActionResult> RunAsync(Func<Task> action)
		{
			try
			{
				await action();
			}
			catch (DedicatedNumberException ex)
			{
				TempData[ErrorKey] = ErrorText(ex.Error);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"Dedicated number request failed for DepartmentId: {DepartmentId}");
				TempData[ErrorKey] = ErrorText(DedicatedNumberErrors.CarrierUnavailable);
			}

			return RedirectToAction(nameof(Index));
		}

		private string ErrorText(DedicatedNumberErrors error) => _localizer["Error." + error].Value;
	}
}
