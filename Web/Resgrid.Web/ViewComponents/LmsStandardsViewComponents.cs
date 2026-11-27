using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.LmsStandards;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// Training T5: the course builder's fields of a Scorm, Cmi5 or LtiLink lesson (the department's ready packages of the right standard, its
	/// active LTI tools), rendered inside the lesson editor's form. The course service checks the choice again when it saves.
	/// </summary>
	public sealed class LmsStandardsLessonFieldsViewComponent(ILmsContentPackageService packages, ILmsLtiService lti) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(LmsLesson lesson, int kind)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var model = new LmsStandardsLessonFieldsView { Lesson = lesson ?? new LmsLesson { Kind = kind }, Kind = kind };
			if (kind == (int)LmsLessonKinds.LtiLink)
				model.Tools = (await lti.GetToolsAsync(departmentId)).Where(t => t.IsActive || t.LmsLtiToolId == lesson?.LmsLtiToolId).ToList();
			else
			{
				var cmi5 = kind == (int)LmsLessonKinds.Cmi5;
				model.Packages = (await packages.GetPackagesAsync(departmentId))
					.Where(p => (p.Standard == (int)LmsContentStandards.Cmi5) == cmi5 && (p.Status != (int)LmsContentPackageStatuses.Failed || p.LmsContentPackageId == lesson?.LmsContentPackageId))
					.ToList();
			}
			return View("~/Areas/User/Views/LmsStandards/_LessonFields.cshtml", model);
		}
	}

	/// <summary>
	/// Training T5: the player of a Scorm or Cmi5 lesson (the sandboxed frame of a fresh launch, with a SCO / AU menu for multi-item packages)
	/// or of an LtiLink lesson (a launch button opening the tool in a new window). The launch runs the learner service's checks (gate, lapse
	/// rules, lesson order) and fails closed with the reason.
	/// </summary>
	public sealed class LmsStandardsPlayerViewComponent(ILmsStandardsRuntimeService runtime, ILmsLtiService lti) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(int enrollmentId, LmsLesson lesson, bool completed, string item)
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
			var model = new LmsStandardsPlayerView { EnrollmentId = enrollmentId, Lesson = lesson, Completed = completed };
			try
			{
				if (lesson?.Kind == (int)LmsLessonKinds.LtiLink)
				{
					model.Tool = lesson.LmsLtiToolId.HasValue ? await lti.GetToolAsync(departmentId, lesson.LmsLtiToolId.Value) : null;
					model.Registration = await runtime.GetRegistrationAsync(departmentId, enrollmentId, lesson.LmsLessonId);
					if (model.Tool == null || model.Tool.IsDeleted || !model.Tool.IsActive)
						model.Error = LmsErrors.MediaUnavailable;
				}
				else if (lesson != null)
				{
					var request = HttpContext.Request;
					model.Launch = await runtime.LaunchAsync(departmentId, ClaimsAuthorizationHelper.GetUserId(), enrollmentId, lesson.LmsLessonId, item,
						request.Scheme + "://" + request.Host + request.PathBase);
					model.Registration = model.Launch?.Registration;
					model.Error = model.Launch?.Error ?? LmsErrors.NotFound;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A standards lesson could not be launched.");
				model.Error = LmsErrors.MediaUnavailable;
			}
			return View("~/Areas/User/Views/LmsStandards/_Player.cshtml", model);
		}
	}
}
