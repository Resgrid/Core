using System;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Filters
{
	/// <summary>
	/// Per-endpoint, incident-scoped capability gate (§3.11). Layered ON TOP of the broad
	/// <c>[Authorize(Policy = Command_*)]</c> claims that already protect every IC endpoint, this filter
	/// additionally enforces that the calling user's effective <see cref="IncidentCapabilities"/> for the
	/// target incident include <see cref="Required"/>. The Incident Commander / Deputy / Unified-Command roles
	/// (and the user who established command) get <see cref="IncidentCapabilities.All"/>, so they pass every
	/// gate — see <see cref="IncidentRoleCapabilityMap"/> and
	/// <see cref="IIncidentCommandService.GetCapabilitiesForUserAsync"/>.
	///
	/// It runs as an action filter (after model binding) so it can read the target Call from the bound request —
	/// either an explicit <c>callId</c> route value, an <c>IncidentCommandId</c> on the body/route (resolved to
	/// its Call, department-ownership checked), or a <c>CallId</c> on the request body. If the Call cannot be
	/// determined the filter does NOT block: the broad Command_* claim and the service-layer department-ownership
	/// guards still apply, so this filter only ever ADDS protection, never removes it.
	///
	/// Note: the entity-id "second action" verbs (delete/release/complete/acknowledge an existing row) are left on
	/// the broad Command_* claim because the target Call isn't on the request — adding capability gating there
	/// needs an entity-id → Call lookup and is a deliberate follow-up.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
	public sealed class RequiresIncidentCapabilityAttribute : Attribute, IAsyncActionFilter
	{
		/// <summary>The capability the caller must hold for the target incident to proceed.</summary>
		public IncidentCapabilities Required { get; }

		public RequiresIncidentCapabilityAttribute(IncidentCapabilities required)
		{
			Required = required;
		}

		public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			var service = context.HttpContext.RequestServices?.GetService(typeof(IIncidentCommandService)) as IIncidentCommandService;

			// Identity from the same claims the controllers use (PrimarySid = userId, PrimaryGroupSid = departmentId).
			var user = context.HttpContext.User;
			var userId = user?.FindFirst(ClaimTypes.PrimarySid)?.Value;
			int.TryParse(user?.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var departmentId);

			// Without identity or the service we can't evaluate — defer to the broad [Authorize] gate.
			if (service == null || departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
			{
				await next();
				return;
			}

			var callId = await ResolveCallIdAsync(context, service, departmentId);
			if (callId == null || callId.Value <= 0)
			{
				// Couldn't classify the target incident — don't block; the broad Command_* claim still governs.
				await next();
				return;
			}

			var capabilities = await service.GetCapabilitiesForUserAsync(departmentId, callId.Value, userId);
			if ((capabilities & Required) != Required)
			{
				context.Result = new ObjectResult("Insufficient incident-command capability for this action.")
				{
					StatusCode = StatusCodes.Status403Forbidden
				};
				return;
			}

			await next();
		}

		/// <summary>
		/// Determines the target Call for the request, preferring the most authoritative source:
		/// (1) an explicit <c>callId</c> route/argument, (2) an <c>IncidentCommandId</c> (route/arg/body) resolved
		/// to its Call and department-ownership checked, then (3) a <c>CallId</c> on a bound request body object.
		/// </summary>
		private static async Task<int?> ResolveCallIdAsync(ActionExecutingContext context, IIncidentCommandService service, int departmentId)
		{
			// (1) Explicit callId (e.g. EvaluateAccountability/{callId}, CloseIncidentChannels/{callId}).
			var directCallId = GetInt(context, "callId");
			if (directCallId.HasValue && directCallId.Value > 0)
				return directCallId;

			// (2) IncidentCommandId — authoritative; resolve to its Call and confirm department ownership so a
			// forged id from another department can't be used to classify the request.
			var incidentCommandId = GetString(context, "incidentCommandId") ?? GetStringProperty(context, "IncidentCommandId");
			if (!string.IsNullOrWhiteSpace(incidentCommandId))
			{
				var command = await service.GetCommandByIdAsync(incidentCommandId);
				if (command != null && command.DepartmentId == departmentId && command.CallId > 0)
					return command.CallId;
			}

			// (3) CallId on a bound body object (e.g. EstablishCommandInput, CreateIncidentChannelInput, ad-hoc creates, FormUnitInput).
			var bodyCallId = GetIntProperty(context, "CallId");
			if (bodyCallId.HasValue && bodyCallId.Value > 0)
				return bodyCallId;

			return null;
		}

		private static int? GetInt(ActionExecutingContext context, string key)
		{
			if (context.ActionArguments.TryGetValue(key, out var arg) && arg is int i)
				return i;

			if (context.RouteData.Values.TryGetValue(key, out var routeVal) && int.TryParse(routeVal?.ToString(), out var parsed))
				return parsed;

			return null;
		}

		private static string GetString(ActionExecutingContext context, string key)
		{
			if (context.ActionArguments.TryGetValue(key, out var arg) && arg is string s && !string.IsNullOrWhiteSpace(s))
				return s;

			if (context.RouteData.Values.TryGetValue(key, out var routeVal))
			{
				var rv = routeVal?.ToString();
				if (!string.IsNullOrWhiteSpace(rv))
					return rv;
			}

			return null;
		}

		private static string GetStringProperty(ActionExecutingContext context, string propertyName)
		{
			foreach (var arg in context.ActionArguments.Values)
			{
				if (arg is null or string)
					continue;

				var prop = arg.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
				if (prop != null && prop.PropertyType == typeof(string))
				{
					if (prop.GetValue(arg) is string value && !string.IsNullOrWhiteSpace(value))
						return value;
				}
			}

			return null;
		}

		private static int? GetIntProperty(ActionExecutingContext context, string propertyName)
		{
			foreach (var arg in context.ActionArguments.Values)
			{
				if (arg is null or string)
					continue;

				var prop = arg.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
				if (prop != null && prop.PropertyType == typeof(int))
				{
					if (prop.GetValue(arg) is int value && value > 0)
						return value;
				}
			}

			return null;
		}
	}
}
