using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Billing;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>What the LMS seat panels draw: the seat summary, the outcome of the last seat change, and the pack schedule.</summary>
	public sealed class LmsSeatPanelView
	{
		public LmsSeatSummary Summary { get; init; }
		public PackPriceSchedule Schedule { get; init; }
		public string CurrencySymbol { get; init; }

		/// <summary>The member the switch is for (the personnel page), or null for the department panels.</summary>
		public string UserId { get; init; }
		public bool MemberEnabled { get; init; }
		public bool MemberLicensed { get; init; }

		public IReadOnlyList<DepartmentGroup> Groups { get; init; } = Array.Empty<DepartmentGroup>();

		/// <summary>Packs from the next billing date when a decrease is scheduled (the LMS settings section).</summary>
		public int? ScheduledPacks { get; init; }

		public string Message { get; init; }
		public string MessageKind { get; init; }
		public bool OfferPack { get; init; }

		public bool Entitled => Summary != null && (Summary.State == LmsSubscriptionStates.Active || Summary.State == LmsSubscriptionStates.DunningGrace);
		public int BillingPacks => ScheduledPacks ?? Summary?.PurchasedPacks ?? 0;
		public decimal NextMonthly => Entitled && Schedule != null ? Schedule.MonthlyTotal(BillingPacks) : 0m;
	}

	internal static class LmsSeatPanels
	{
		public static PackPriceSchedule Schedule => AddonCatalog.Get(PlanAddonTypes.Lms).Schedule;
		public static string Symbol => AddonCatalog.CurrencySymbol;
	}

	/// <summary>
	/// The per-member LMS switch on the personnel page (§17.4: "where the switch lives"), beside where the Shifts plan puts the
	/// Workforce Pro switch. Department admins only, and only while the LMS is on for the department; one gate read per request.
	/// </summary>
	public sealed class LmsSeatSwitchViewComponent(ILmsAccessService access, ILmsSeatService seats) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(string userId)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(userId) || !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					return Content(string.Empty);

				var summary = await LmsRequestGate.GetSummaryAsync(HttpContext, access, seats, ClaimsAuthorizationHelper.GetDepartmentId());
				if (summary == null)
					return Content(string.Empty);

				return View("~/Areas/User/Views/Shared/_LmsSeatSwitch.cshtml", new LmsSeatPanelView
				{
					Summary = summary,
					Schedule = LmsSeatPanels.Schedule,
					CurrencySymbol = LmsSeatPanels.Symbol,
					UserId = userId,
					MemberEnabled = summary.EnabledUserIds.Contains(userId),
					MemberLicensed = summary.LicensedUserIds.Contains(userId),
					Message = TempData[LmsSeatsController.MessageKey] as string,
					MessageKind = TempData[LmsSeatsController.MessageKindKey] as string,
					OfferPack = TempData[LmsSeatsController.OfferPackKey] as bool? == true
				});
			}
			catch (Exception ex)
			{
				// An optional panel must not take down the personnel page.
				Logging.LogException(ex);
				return Content(string.Empty);
			}
		}
	}

	/// <summary>The seat counter and the bulk switch by group on the personnel list (§17.4). Department admins only, LMS on.</summary>
	public sealed class LmsSeatSummaryViewComponent(ILmsAccessService access, ILmsSeatService seats, IDepartmentGroupsService groups) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					return Content(string.Empty);

				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var summary = await LmsRequestGate.GetSummaryAsync(HttpContext, access, seats, departmentId);
				if (summary == null)
					return Content(string.Empty);

				var list = (await groups.GetAllGroupsForDepartmentUnlimitedThinAsync(departmentId) ?? new List<DepartmentGroup>())
					.Where(g => g != null && g.DepartmentId == departmentId)
					.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
					.ToList();

				return View("~/Areas/User/Views/Shared/_LmsSeatSummary.cshtml", new LmsSeatPanelView
				{
					Summary = summary,
					Schedule = LmsSeatPanels.Schedule,
					CurrencySymbol = LmsSeatPanels.Symbol,
					Groups = list,
					Message = TempData[LmsSeatsController.MessageKey] as string,
					MessageKind = TempData[LmsSeatsController.MessageKindKey] as string,
					OfferPack = TempData[LmsSeatsController.OfferPackKey] as bool? == true
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return Content(string.Empty);
			}
		}
	}

	/// <summary>
	/// The LMS section of the training records settings page (§17.4 implementation notes): seat count, packs and next month's price,
	/// with the scheduled decrease when there is one. Shown while the LMS is on for the department.
	/// </summary>
	public sealed class LmsSettingsSummaryViewComponent(ILmsAccessService access, ILmsSeatService seats, ILmsBillingService billing) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var summary = await LmsRequestGate.GetSummaryAsync(HttpContext, access, seats, departmentId);
				if (summary == null)
					return Content(string.Empty);

				int? scheduled = null;
				var schedule = LmsSeatPanels.Schedule;
				var symbol = LmsSeatPanels.Symbol;
				if (summary.State == LmsSubscriptionStates.Active)
				{
					// The scheduled decrease is the Billing API's; without it the preview is the packs in force.
					var status = await billing.GetAsync(departmentId);
					if (status?.Active == true)
					{
						scheduled = status.ScheduledPacks;
						schedule = LmsPackPricing.ForCurrency(status.Currency ?? schedule.Currency);
						symbol = string.Equals(schedule.Currency, "EUR", StringComparison.OrdinalIgnoreCase) ? "€" : "$";
					}
				}

				return View("~/Areas/User/Views/Shared/_LmsSettingsSummary.cshtml", new LmsSeatPanelView
				{
					Summary = summary,
					Schedule = schedule,
					CurrencySymbol = symbol,
					ScheduledPacks = scheduled
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return Content(string.Empty);
			}
		}
	}
}
