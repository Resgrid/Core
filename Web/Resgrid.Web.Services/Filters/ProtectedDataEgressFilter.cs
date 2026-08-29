using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// API twin of the MVC response-boundary net (plan section 7.5). For a department whose
	/// protection is enforced, the outgoing result object is walked and any value still carrying an
	/// rgdp/rgdpb envelope is replaced with the REDACTED placeholder before serialization.
	///
	/// The v4 surface is where this matters most: a result DTO is mapped field by field from an
	/// entity, so forgetting the resolve call ships ciphertext to every mobile client. Two of the
	/// four leaks found by hand were exactly this shape.
	///
	/// Redacts AND logs — a silent redaction would hide the missing resolve call, and the log line
	/// with the member path is what makes the missed surface findable.
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
				// Fails OPEN: this sits behind the real resolve calls, and a protection-state fault
				// must not take the API down.
				Logging.LogException(ex, $"ProtectedDataEgressFilter (api) could not read protection state for department {departmentId}; skipping the scan");
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

				// A file endpoint that never resolved its payload would hand the caller ciphertext
				// bytes as their document. Nothing to redact in a file — refuse it.
				if (context.Result is FileContentResult file &&
					ProtectedEgressScanner.HasBinaryEnvelopePrefix(file.FileContents))
				{
					Logging.LogError($"ADP egress net: {action} tried to serve an ENCRYPTED file to department {departmentId}; refused. The endpoint is missing its protected read (includeData).");
					context.Result = new NotFoundResult();
					return;
				}

				var payload = context.Result switch
				{
					ObjectResult objectResult => objectResult.Value,
					JsonResult json => json.Value,
					_ => null
				};

				var result = ProtectedEgressScanner.Sanitize(payload, maxNodes: DataProtectionConfig.EgressScanMaxNodes);
				if (!result.FoundAnything)
					return;

				Logging.LogError($"ADP egress net caught unresolved protected data in {action} for department {departmentId}: " +
					$"{result.Redacted} redacted, {result.Unfixable} unfixable{(result.Truncated ? ", scan truncated" : "")}. " +
					$"Paths: {string.Join(", ", result.Paths.Take(25))}");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "ProtectedDataEgressFilter (api) failed while scanning the result");
			}
		}
	}
}
