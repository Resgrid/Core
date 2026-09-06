using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Model;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Maps <see cref="RecordProtectedContentException"/> (RMS plan section 5.9.3) onto the step-up flow: an
	/// AJAX or file request gets 403 with the machine-readable reason the reveal module understands; a page
	/// request goes back where it came from with a message, so the banner on that page can prompt for the grant.
	/// </summary>
	public sealed class RecordProtectedContentExceptionFilter : IExceptionFilter
	{
		public void OnException(ExceptionContext context)
		{
			if (!(context.Exception is RecordProtectedContentException exception))
				return;

			var request = context.HttpContext.Request;
			var wantsJson = string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
				|| request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
				|| request.Path.StartsWithSegments("/api");

			if (wantsJson)
			{
				context.Result = new JsonResult(new { success = false, error = exception.Reason, message = exception.Message }) { StatusCode = 403 };
				context.ExceptionHandled = true;
				return;
			}

			try
			{
				var tempDataFactory = context.HttpContext.RequestServices.GetService<ITempDataDictionaryFactory>();
				var tempData = tempDataFactory?.GetTempData(context.HttpContext);
				if (tempData != null)
				{
					tempData["RecordsError"] = exception.Message;
					tempData["RecordsProtectedReason"] = exception.Reason;
				}
			}
			catch (Exception)
			{
				// TempData is a courtesy; the redirect below still stops the ciphertext from rendering.
			}

			var referer = request.Headers["Referer"].ToString();
			context.Result = !string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri) && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
				? new RedirectResult(uri.PathAndQuery)
				: new RedirectToActionResult("Index", "Records", new { area = "User" });
			context.ExceptionHandled = true;
		}
	}
}
