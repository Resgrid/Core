using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The per-member LMS switch and the bulk switch by group (§17.4 seat rules: "on the personnel page, with bulk actions by group and a
	/// seat counter; not a separate settings page"). The forms live on the personnel pages (LmsSeatSwitch and LmsSeatSummary view
	/// components); these actions only write and return there. Department admins only; NotFound while the LMS is switched off for the
	/// department. Enabling beyond the purchased seats is refused with an offer to add a pack.
	/// </summary>
	[Area("User"), Authorize]
	public class LmsSeatsController : SecureBaseController
	{
		public const string MessageKey = "LmsSeatMessage";
		public const string MessageKindKey = "LmsSeatMessageKind";
		public const string OfferPackKey = "LmsSeatOfferPack";

		private readonly ILmsSeatService _seats;
		private readonly ILmsAccessService _access;
		private readonly IDepartmentGroupsService _groups;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> _strings;

		public LmsSeatsController(ILmsSeatService seats, ILmsAccessService access, IDepartmentGroupsService groups,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> strings)
		{
			_seats = seats;
			_access = access;
			_groups = groups;
			_strings = strings;
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetSeat(string userId, bool enabled, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Forbid();
			if (string.IsNullOrWhiteSpace(userId) || !await LmsRequestGate.IsFlagEnabledAsync(HttpContext, _access, DepartmentId))
				return NotFound();

			LmsSeatChangeResult result;
			try
			{
				result = await _seats.SetSeatAsync(DepartmentId, userId, enabled, UserId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"LMS seat change failed for DepartmentId: {DepartmentId}");
				result = null;
			}

			Report(result?.Outcome, result?.Summary, result?.PacksNeeded ?? 0);
			return RedirectToAction("ViewPerson", "Personnel", new { Area = "User", userId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetGroup(int groupId, bool enabled, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Forbid();
			if (!await LmsRequestGate.IsFlagEnabledAsync(HttpContext, _access, DepartmentId))
				return NotFound();

			var group = groupId > 0 ? await _groups.GetGroupByIdAsync(groupId) : null;
			if (group == null || group.DepartmentId != DepartmentId)
				return NotFound();

			try
			{
				var result = await _seats.SetSeatsForGroupAsync(DepartmentId, groupId, enabled, UserId, cancellationToken);
				if (result.Outcome is LmsSeatOutcomes.NotOffered or LmsSeatOutcomes.AddonInactive)
					Report(result.Outcome, result.Summary, 0);
				else
				{
					TempData[MessageKey] = string.Format(CultureInfo.CurrentCulture, _strings["LmsSeatBulkResult"].Value,
						result.Changed, result.AlreadySet, result.SkippedInactive, result.RefusedForSeats);
					TempData[MessageKindKey] = result.RefusedForSeats > 0 ? "warning" : "success";
					TempData[OfferPackKey] = result.RefusedForSeats > 0;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"LMS group seat change failed for DepartmentId: {DepartmentId}");
				Report(null, null, 0);
			}

			return RedirectToAction("Index", "Personnel", new { Area = "User" });
		}

		private void Report(LmsSeatOutcomes? outcome, LmsSeatSummary summary, int packsNeeded)
		{
			string message;
			var kind = "warning";
			var offerPack = false;
			switch (outcome)
			{
				case LmsSeatOutcomes.Enabled:
				case LmsSeatOutcomes.Disabled:
				case LmsSeatOutcomes.Unchanged:
					message = _strings["LmsSeatSaved"];
					kind = "success";
					break;
				case LmsSeatOutcomes.SeatsFull:
					message = string.Format(CultureInfo.CurrentCulture, _strings["LmsSeatsFull"].Value, summary?.PurchasedSeats ?? 0, Math.Max(1, packsNeeded));
					offerPack = true;
					break;
				case LmsSeatOutcomes.NotActiveMember:
					message = _strings["LmsSeatNotActive"];
					break;
				case LmsSeatOutcomes.AddonInactive:
					message = _strings["LmsSeatAddonInactive"];
					offerPack = true;
					break;
				default:
					message = _strings["LmsSeatFailed"];
					break;
			}

			TempData[MessageKey] = message;
			TempData[MessageKindKey] = kind;
			TempData[OfferPackKey] = offerPack;
		}
	}
}
