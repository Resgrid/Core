using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Subscription;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Workforce Pro seats (shifts-scheduling-modernization-plan.md §7.2 "per-member switch"): the department's active members with their
	/// Workforce Pro switch and the seat counter (used / purchased / available). Department administrators only; NotFound while the
	/// Workforce.Pro flag is off. Switching a member on beyond the purchased seats is refused with an offer to add a pack; switching one off
	/// never deletes a record (their wellness records become read-only). Every rule lives in IWorkforceProSeatService.
	/// </summary>
	[Area("User"), Authorize]
	public class WorkforceProSeatsController : SecureBaseController
	{
		public const string MessageKey = "WorkforceProSeatMessage";
		public const string MessageKindKey = "WorkforceProSeatMessageKind";

		private readonly IWorkforceProSeatService _seats;
		private readonly IWorkforceProAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> _strings;

		public WorkforceProSeatsController(IWorkforceProSeatService seats, IWorkforceProAccessService access, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.Addons.Addons> strings)
		{
			_seats = seats;
			_access = access;
			_departments = departments;
			_strings = strings;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Forbid();
			if (!await WorkforceProRequestGate.IsOfferedAsync(HttpContext, _access, DepartmentId))
				return NotFound();

			var summary = await _seats.GetSummaryAsync(DepartmentId);
			var active = await _departments.GetActiveMemberUserIdsAsync(DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			// PersonName upper-cases its user id; the member ids keep their own spelling.
			var canonical = active.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var names = (await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId) && active.Contains(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.First())
				.ToList();

			var model = new WorkforceProSeatsView
			{
				Summary = summary,
				Members = names
					.Select(n => new WorkforceProSeatRow
					{
						UserId = canonical.TryGetValue(n.UserId, out var id) ? id : n.UserId,
						Name = string.IsNullOrWhiteSpace(n.Name) ? n.UserId : n.Name.Trim(),
						Enabled = summary.EnabledUserIds.Contains(n.UserId),
						Licensed = summary.LicensedUserIds.Contains(n.UserId)
					})
					.OrderByDescending(r => r.Enabled)
					.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
					.ToList(),
				Message = TempData?[MessageKey] as string,
				MessageKind = TempData?[MessageKindKey] as string
			};
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetSeat(string userId, bool enabled, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Forbid();
			if (string.IsNullOrWhiteSpace(userId) || !await WorkforceProRequestGate.IsOfferedAsync(HttpContext, _access, DepartmentId))
				return NotFound();

			WorkforceProSeatChangeResult result;
			try
			{
				result = await _seats.SetSeatAsync(DepartmentId, userId, enabled, UserId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Workforce Pro seat change failed for DepartmentId: {DepartmentId}");
				result = null;
			}

			if (result?.Outcome == WorkforceProSeatOutcomes.NotAllowed)
				return Forbid();
			Report(result);
			return RedirectToAction(nameof(Index));
		}

		private void Report(WorkforceProSeatChangeResult result)
		{
			string message;
			var kind = "warning";
			switch (result?.Outcome)
			{
				case WorkforceProSeatOutcomes.Enabled:
				case WorkforceProSeatOutcomes.Disabled:
				case WorkforceProSeatOutcomes.Unchanged:
					message = _strings["WorkforceProSeatSaved"];
					kind = "success";
					break;
				case WorkforceProSeatOutcomes.SeatsFull:
					message = string.Format(CultureInfo.CurrentCulture, _strings["WorkforceProSeatsFull"].Value, result.Summary?.PurchasedSeats ?? 0, Math.Max(1, result.PacksNeeded));
					break;
				case WorkforceProSeatOutcomes.NotActiveMember:
					message = _strings["WorkforceProSeatNotActive"];
					break;
				case WorkforceProSeatOutcomes.AddonInactive:
					message = _strings["WorkforceProSeatAddonInactive"];
					break;
				default:
					message = _strings["WorkforceProSeatFailed"];
					break;
			}

			if (TempData == null)
				return;
			TempData[MessageKey] = message;
			TempData[MessageKindKey] = kind;
		}
	}
}
