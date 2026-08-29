using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Response-boundary net for Advanced Data Protection (plan section 7.5). For a department
	/// whose protection is enforced, the outgoing view model is walked and any value still carrying
	/// an rgdp/rgdpb envelope is replaced with the REDACTED placeholder before the view renders.
	///
	/// Why this exists: the catalog proves a field is protected and the binding-parity test proves
	/// a read accessor exists, but nothing proves a controller CALLS the resolve method. Four real
	/// leaks were found by hand across these surfaces and every one was invisible to the suite.
	/// A per-surface list has to be maintained; envelope detection does not.
	///
	/// It redacts AND logs. Redacting alone would quietly paper over the missed resolve call, so
	/// every hit is logged with the action and the member path — that log line is the bug report.
	/// Runs only when protection is enforced, so an unprotected department pays one cached lookup.
	/// </summary>
	public sealed class ProtectedDataEgressFilter : IAsyncResultFilter
	{
		public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
		{
			if (!DataProtectionConfig.EgressScanEnabled)
			{
				await next();
				return;
			}

			var departmentClaim = context.HttpContext.User?.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value;
			if (!int.TryParse(departmentClaim, out var departmentId) || departmentId <= 0)
			{
				await next();
				return;
			}

			bool enforced;
			try
			{
				var protectionService = context.HttpContext.RequestServices.GetService<IDepartmentDataProtectionService>();
				enforced = protectionService != null && await protectionService.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Fails OPEN deliberately: this is a safety net behind the real resolve calls, and a
				// protection-state fault must not blank every page in the product.
				Logging.LogException(ex, $"ProtectedDataEgressFilter (web) could not read protection state for department {departmentId}; skipping the scan");
				await next();
				return;
			}

			if (!enforced)
			{
				await next();
				return;
			}

			Scan(context, departmentId);
			await next();
		}

		private static void Scan(ResultExecutingContext context, int departmentId)
		{
			try
			{
				var action = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)
					?.DisplayName ?? context.HttpContext.Request.Path.ToString();

				// A file endpoint that never resolved its payload would stream ciphertext bytes as
				// the user's document. There is nothing to redact in a file — refuse it instead.
				if (context.Result is FileContentResult file &&
					ProtectedEgressScanner.HasBinaryEnvelopePrefix(file.FileContents))
				{
					Logging.LogError($"ADP egress net: {action} tried to serve an ENCRYPTED file to department {departmentId}; refused. The endpoint is missing its protected read (includeData).");
					context.Result = new NotFoundResult();
					return;
				}

				var model = context.Result switch
				{
					ViewResult view => view.Model,
					PartialViewResult partial => partial.Model,
					ObjectResult objectResult => objectResult.Value,
					JsonResult json => json.Value,
					_ => null
				};

				var result = ProtectedEgressScanner.Sanitize(model, maxNodes: DataProtectionConfig.EgressScanMaxNodes);

				// ViewData carries entities too on several of these pages, so it is scanned as well.
				if (context.Result is ViewResult viewResult && viewResult.ViewData != null)
				{
					foreach (var key in viewResult.ViewData.Keys.ToList())
					{
						var entry = ProtectedEgressScanner.Sanitize(viewResult.ViewData[key],
							maxNodes: DataProtectionConfig.EgressScanMaxNodes);

						if (ProtectedEgressScanner.IsEnvelopedValue(viewResult.ViewData[key]))
						{
							viewResult.ViewData[key] = ProtectedDataEnvelope.RedactionValue;
							result.Redacted++;
							result.Paths.Add($"ViewData[{key}]");
						}

						result.Redacted += entry.Redacted;
						result.Unfixable += entry.Unfixable;
						result.Paths.AddRange(entry.Paths);
					}
				}

				if (!result.FoundAnything)
					return;

				// This is a bug report, not a routine event: a surface reached the response boundary
				// without resolving. Paths are member names, never values.
				Logging.LogError($"ADP egress net caught unresolved protected data in {action} for department {departmentId}: " +
					$"{result.Redacted} redacted, {result.Unfixable} unfixable{(result.Truncated ? ", scan truncated" : "")}. " +
					$"Paths: {string.Join(", ", result.Paths.Take(25))}");
			}
			catch (Exception ex)
			{
				// Never let the net itself break a response.
				Logging.LogException(ex, "ProtectedDataEgressFilter (web) failed while scanning the result");
			}
		}
	}
}
