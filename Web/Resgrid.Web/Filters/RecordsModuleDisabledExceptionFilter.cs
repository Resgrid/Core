using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// A Records module that is not enabled for the department (<see cref="RecordsModuleDisabledException"/>) is something to
	/// tell the user, not a server fault. The prevention pages check the Records flag before calling their service, but the
	/// service gate also needs Records activated and cut over, so a department with the flag on and Records not yet active got
	/// a 500 (and a Sentry issue per module). An AJAX or file request now gets 404 with the message; a page request lands on
	/// the Records queue with the message above the notice that says whether Records itself still needs activating.
	/// </summary>
	public sealed class RecordsModuleDisabledExceptionFilter : IExceptionFilter
	{
		public void OnException(ExceptionContext context)
		{
			if (!(context.Exception is RecordsModuleDisabledException exception))
				return;

			var message = Message(context, exception);
			var request = context.HttpContext.Request;
			var wantsJson = string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
				|| request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
				|| request.Path.StartsWithSegments("/api");

			if (wantsJson)
			{
				context.Result = new JsonResult(new { success = false, error = "records_module_disabled", message }) { StatusCode = 404 };
				context.ExceptionHandled = true;
				return;
			}

			// The queue is where the message is shown; if the queue itself is what failed, redirecting there would loop.
			if (string.Equals(context.RouteData.Values["controller"] as string, "Records", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(context.RouteData.Values["action"] as string, "Index", StringComparison.OrdinalIgnoreCase))
			{
				context.Result = new NotFoundResult();
				context.ExceptionHandled = true;
				return;
			}

			try
			{
				var tempDataFactory = context.HttpContext.RequestServices.GetService<ITempDataDictionaryFactory>();
				var tempData = tempDataFactory?.GetTempData(context.HttpContext);
				if (tempData != null)
					tempData["RecordsError"] = message;
			}
			catch (Exception)
			{
				// TempData is a courtesy; the redirect still replaces the error page.
			}

			context.Result = new RedirectToActionResult("Index", "Records", new { area = "User" });
			context.ExceptionHandled = true;
		}

		/// <summary>The message in the user's language, naming the module the way its page heading does.</summary>
		private static string Message(ExceptionContext context, RecordsModuleDisabledException exception)
		{
			var localizer = context.HttpContext.RequestServices?.GetService<IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records>>();
			var nameKey = ModuleNameKey(exception.Module);
			if (localizer == null || nameKey == null)
				return exception.Message;

			return string.Format(localizer["RecordsModuleNotEnabled"].Value, localizer[nameKey].Value);
		}

		private static string ModuleNameKey(RecordsPreventionModule module)
		{
			switch (module)
			{
				case RecordsPreventionModule.Occupancy: return "Occupancies";
				case RecordsPreventionModule.Inspections: return "Inspections";
				case RecordsPreventionModule.Hydrants: return "Hydrants";
				case RecordsPreventionModule.Permits: return "Permits";
				case RecordsPreventionModule.Crr: return "CrrActivities";
				case RecordsPreventionModule.Investigations: return "Investigations";
				case RecordsPreventionModule.QualityReview: return "QualityReview";
				case RecordsPreventionModule.Analytics: return "AnalyticsHeader";
				default: return null;
			}
		}
	}
}
