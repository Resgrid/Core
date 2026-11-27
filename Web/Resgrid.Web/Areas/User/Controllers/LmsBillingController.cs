using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Billing;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// LMS add-on purchase, pack quantity and renewal page (protocols-training-knowledge-plan §17.3, §17.4): the first pack add-on on the
	/// shared <see cref="AddonBillingControllerBase"/> page, with a quantity of 25-member packs on a graduated schedule. The price of any
	/// change is shown before it is made (<see cref="PreviewPacks"/>); adding packs is charged at once, prorated, by the Billing API;
	/// removing packs takes effect at the next billing date and never below the packs that seat the LMS-enabled members. Monthly only,
	/// no trial; sold only with a paid base plan (§25.4a item 9). Only the managing member may buy, change or cancel. Checkout and the
	/// quantity live in the Billing API (int-CommonApis LmsBilling, owed); Core only relays.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public sealed class LmsBillingController : AddonBillingControllerBase
	{
		private readonly ILmsBillingService _billing;
		private readonly ILmsSeatService _seats;
		private readonly IEventAggregator _events;
		private LmsBillingStatus _status;
		private bool _statusRead;

		public LmsBillingController(ILmsBillingService billing, ILmsSeatService seats, IEventAggregator events, IDepartmentsService departments,
			Model.Services.IAuthorizationService authorization, IFeatureToggleService flags, IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> addonStrings)
			: base(departments, authorization, flags, addonStrings)
		{
			_billing = billing;
			_seats = seats;
			_events = events;
		}

		protected override PlanAddonTypes AddonType => PlanAddonTypes.Lms;

		private static PackPriceSchedule ScheduleFor(LmsBillingStatus status) =>
			status?.Currency != null ? LmsPackPricing.ForCurrency(status.Currency) : AddonCatalog.Get(PlanAddonTypes.Lms).Schedule;

		private async Task<LmsBillingStatus> StatusAsync()
		{
			if (!_statusRead)
			{
				_status = await _billing.GetAsync(DepartmentId);
				_statusRead = true;
			}

			return _status;
		}

		protected override async Task<AddonBillingStatusView> GetStatusAsync() => AddonBillingStatusView.From(await StatusAsync());

		/// <summary>
		/// The packs field of the checkout form. A first purchase (or a purchase after a lapse) may hold fewer seats than members still
		/// LMS-enabled from before; the licensing order then decides who is licensed until packs are added.
		/// </summary>
		protected override async Task<AddonCheckoutStart> BeginCheckoutAsync()
		{
			var status = await StatusAsync();
			if (status == null || !status.HasEntityPlan || status.Active)
				return null;

			var schedule = ScheduleFor(status);
			if (!Request.HasFormContentType || !int.TryParse(Request.Form["packs"], NumberStyles.None, CultureInfo.InvariantCulture, out var packs) ||
				!schedule.IsValidQuantity(packs))
				return null;

			var checkout = await _billing.BeginCheckoutAsync(DepartmentId, packs);
			return checkout == null ? null : new AddonCheckoutStart(checkout.Provider, checkout.Url, checkout.TransactionId);
		}

		protected override Task<bool> CancelRenewalAsync() => _billing.CancelRenewalAsync(DepartmentId);

		protected override async Task DecorateAsync(AddonBillingView view)
		{
			var status = await StatusAsync();
			if (status != null && !status.HasEntityPlan && !status.Active)
				view.Notices.Add(AddonStrings["LmsRequiresPaidPlan"]);

			view.Notes.Add(AddonStrings["LmsBillingPacksNote"]);
			view.Notes.Add(AddonStrings["LmsBillingSeatsNote"]);
			view.Notes.Add(AddonStrings["LmsBillingFreeNote"]);

			if (view.CanViewBilling)
			{
				LmsSeatSummary seats = null;
				try
				{
					seats = await _seats.GetSummaryAsync(DepartmentId);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"LMS seats could not be read for DepartmentId: {DepartmentId}");
				}

				var schedule = ScheduleFor(status);
				var packs = status?.Active == true ? Math.Max(schedule.MinimumPacks, status.Packs) : 0;
				var minimum = Math.Max(schedule.MinimumPacks, seats?.MinimumPacks ?? schedule.MinimumPacks);
				view.Packs = new AddonPackView
				{
					Schedule = schedule,
					CurrencySymbol = string.Equals(schedule.Currency, "EUR", StringComparison.OrdinalIgnoreCase) ? "€" : "$",
					Packs = packs,
					ScheduledPacks = status?.Active == true ? status.ScheduledPacks : null,
					EnabledSeats = seats?.EnabledSeats ?? 0,
					MinimumPacks = minimum,
					ProposedPacks = packs > 0 ? packs : minimum,
					NextBillingUtc = status?.Active == true ? status.PaidThroughUtc : null
				};
			}

			view.Links.Add(new AddonLink(AddonStrings["LmsManageSeats"], Url.Action("Index", "Personnel", new { Area = "User" })));
		}

		/// <summary>The price of <paramref name="packs"/> packs against the packs in force, before anything is changed.</summary>
		[HttpGet]
		public async Task<IActionResult> PreviewPacks(int packs)
		{
			if (!await OwnerAsync() && !await CanViewBillingAsync())
				return Forbid();

			var status = await StatusAsync();
			var schedule = ScheduleFor(status);
			var current = status?.Active == true ? Math.Max(schedule.MinimumPacks, status.Packs) : 0;
			var minimum = await _seats.GetMinimumPacksAsync(DepartmentId);
			var quote = PackQuotes.Build(schedule, current, packs, minimum, status?.PeriodStartUtc, status?.PaidThroughUtc, DateTime.UtcNow);

			return Json(new
			{
				packs = quote.Packs,
				currentPacks = quote.CurrentPacks,
				seats = quote.Seats,
				currency = quote.Currency,
				nextMonthly = quote.NextMonthly,
				monthlyChange = quote.MonthlyChange,
				proratedNow = quote.ProratedNow,
				effectiveOn = quote.EffectiveOnUtc?.ToString("D", CultureInfo.CurrentCulture),
				increase = quote.IsIncrease,
				decrease = quote.IsDecrease,
				refusal = quote.Refusal,
				minimumPacks = Math.Max(schedule.MinimumPacks, minimum)
			});
		}

		/// <summary>
		/// Sets the pack quantity of a running subscription. An increase is applied and invoiced at once, prorated; a decrease is
		/// scheduled for the next billing date and never goes below the packs that seat the LMS-enabled members.
		/// </summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ChangePacks(int packs, int expectedPacks)
		{
			if (!await OwnerAsync())
				return Forbid();
			if (!await OfferedAsync())
				return RedirectToAction(nameof(Index), new { result = AddonBillingResults.PacksFailed });

			var outcome = AddonBillingResults.PacksFailed;
			try
			{
				var status = await StatusAsync();
				if (status?.Active == true && !status.Cancelled)
				{
					var schedule = ScheduleFor(status);
					var current = Math.Max(schedule.MinimumPacks, status.Packs);
					var minimum = await _seats.GetMinimumPacksAsync(DepartmentId);
					var quote = PackQuotes.Build(schedule, current, packs, minimum, status.PeriodStartUtc, status.PaidThroughUtc, DateTime.UtcNow);
					if (quote.Refusal == PackQuotes.BelowEnabledSeats)
						outcome = AddonBillingResults.PacksBelowSeats;
					else if (quote.Refusal == null && expectedPacks == current && packs != current)
					{
						var result = await _billing.ChangePacksAsync(DepartmentId, packs, expectedPacks);
						if (result?.Succeeded == true)
						{
							outcome = packs > current ? AddonBillingResults.PacksAdded : AddonBillingResults.PacksScheduled;
							_events.SendMessage(new AuditEvent
							{
								DepartmentId = DepartmentId,
								UserId = UserId,
								Type = AuditLogTypes.LmsPacksChanged,
								Before = JsonConvert.SerializeObject(new { packs = current, monthly = quote.CurrentMonthly }),
								After = JsonConvert.SerializeObject(new { packs, monthly = quote.NextMonthly, scheduledFor = packs > current ? null : status.PaidThroughUtc, chargedNow = result.AmountChargedNow }),
								Successful = true
							});
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"LMS pack change failed for DepartmentId: {DepartmentId}");
				outcome = AddonBillingResults.PacksFailed;
			}

			return RedirectToAction(nameof(Index), new { result = outcome });
		}
	}
}
