using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.LmsPrograms;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// Round 9A: the member's progress on the certifications backed by requirement sets ("3 of 4 done", the state, the expiry, each item).
	/// Rendered by name from baseline pages (My Training, the certification record page), so they never depend on LMS types (§17.3).
	/// Nothing while the LMS flag is off or the department has no active set (a cheap read before any gate call). Another member's progress
	/// shows only to someone who may view certifications. An optional panel never takes the page down.
	/// </summary>
	public sealed class LmsCertificationProgressViewComponent(ILmsCertificationRequirementService requirements, ILmsAccessService access, ILmsGateService gate,
		IDepartmentsService departments) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(string userId = null, int certificationTypeId = 0)
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var viewerId = ClaimsAuthorizationHelper.GetUserId();
				if (departmentId <= 0 || string.IsNullOrWhiteSpace(viewerId) || !await access.IsFlagEnabledAsync(departmentId))
					return Content(string.Empty);

				var memberId = string.IsNullOrWhiteSpace(userId) ? viewerId : userId;
				var isSelf = string.Equals(memberId, viewerId, StringComparison.OrdinalIgnoreCase);
				var mayViewOthers = ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanViewCertifications();
				if (!isSelf && !mayViewOthers)
					return Content(string.Empty);

				var items = await requirements.GetMemberProgressAsync(departmentId, memberId, DateTime.UtcNow) ?? new List<LmsCertificationProgress>();
				if (certificationTypeId > 0)
					items = items.Where(p => p.CertificationTypeId == certificationTypeId).ToList();
				if (items.Count == 0)
					return Content(string.Empty);

				var actor = await gate.GetActorAsync(departmentId, viewerId);
				return View("~/Areas/User/Views/LmsPrograms/_CertificationProgress.cshtml", new LmsCertificationProgressPanel
				{
					Items = items,
					IsSelf = isSelf,
					IsLapsed = actor?.Gate?.IsLapsed == true,
					CanSeeOfficerList = mayViewOthers,
					Department = await departments.GetDepartmentByIdAsync(departmentId, false)
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The certification progress panel could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
