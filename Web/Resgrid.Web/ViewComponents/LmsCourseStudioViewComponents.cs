using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Courses;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// Where a course came from (T5 items 4 and 5), on the course page and in the builder: a starter clone's attribution (source, licence,
	/// revision), or an AI draft's source and a reminder that every lesson must be checked against its cited section before it is submitted (the
	/// builder also lists each drafted lesson's citations). Nothing for a course that started blank. An optional panel never takes the page down.
	/// </summary>
	public sealed class LmsCourseOriginViewComponent(ILmsCourseDraftService drafts) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(int courseId, bool showCitations = false)
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				if (departmentId <= 0 || courseId <= 0)
					return Content(string.Empty);
				var origin = await drafts.GetOriginAsync(departmentId, courseId);
				if (origin == null)
					return Content(string.Empty);
				return View("~/Areas/User/Views/Courses/_CourseOrigin.cshtml", new CourseOriginModel
				{
					Origin = origin,
					Citations = showCitations ? LmsLessonCitation.Parse(origin.CitationsJson).Where(c => c.Anchors.Count > 0).ToList() : new System.Collections.Generic.List<LmsLessonCitation>(),
					ShowCitations = showCitations
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The course origin could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
