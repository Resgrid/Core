using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Providers.Claims;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
#if (!DEBUG && !DOCKER)
	//[RequireHttps]
#endif
	/// <summary>
	/// Base controller for v4 API endpoints that accept both standard OAuth/OIDC AND SystemApiKey
	/// authentication (used by the SMTP Relay in hosted multi-department mode).
	///
	/// Controllers that only need standard OAuth should use <see cref="V4AuthenticatedApiControllerbase"/> instead.
	/// </summary>
	[ApiController]
	[Produces("application/json")]
	[Authorize(AuthenticationSchemes = $"{OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme},SystemApiKey")]
	public class V4AuthenticatedApiControllerbaseSystemAuth : ControllerBase
	{
		/// <summary>
		/// Returns the current user ID. In SystemApiKey mode returns a synthetic identifier.
		/// </summary>
		protected string UserId => IsSystemApiKeyRequest
			? "smtp_relay_system"
			: ClaimsAuthorizationHelper.GetUserId();

		/// <summary>
		/// Returns the department ID from the auth token claims. Callers that need to
		/// potentially override this with a request-level DepartmentId (SystemApiKey mode)
		/// should use <see cref="GetEffectiveDepartmentId(int?)"/> instead.
		/// </summary>
		protected int DepartmentId => ClaimsAuthorizationHelper.GetDepartmentId();

		/// <summary>
		/// Returns the current username. In SystemApiKey mode returns "SMTP Relay".
		/// </summary>
		protected string UserName => IsSystemApiKeyRequest
			? "SMTP Relay"
			: ClaimsAuthorizationHelper.GetUsername();

		protected string TimeZone => ClaimsAuthorizationHelper.GetTimeZone();

		/// <summary>
		/// Returns true if the current request was authenticated via the SystemApiKey scheme
		/// or carries a service-account marker claim set by the ConnectController client_credentials flow.
		/// </summary>
		protected bool IsSystemApiKeyRequest =>
			HttpContext.User.Identities.Any(i => i.AuthenticationType == "SystemApiKey") ||
			HttpContext.User.HasClaim(ResgridClaimTypes.Data.ServiceAccount, "true");

		/// <summary>
		/// Returns the effective department ID for the current request.
		/// When a request-level department ID is provided (e.g. from NewCallInput.DepartmentId
		/// or a departmentId query parameter in SystemApiKey mode), that value takes precedence.
		/// Otherwise, falls back to the department ID from the auth token claims.
		/// </summary>
		/// <param name="requestDepartmentId">Optional department ID from the request body or query.</param>
		protected int GetEffectiveDepartmentId(int? requestDepartmentId)
		{
			if (IsSystemApiKeyRequest && requestDepartmentId.HasValue && requestDepartmentId.Value > 0)
				return requestDepartmentId.Value;

			return ClaimsAuthorizationHelper.GetDepartmentId();
		}

		/// <summary>
		/// Returns the effective department ID for the current request, parsing from a string.
		/// </summary>
		/// <param name="requestDepartmentId">Optional department ID string from the request body or query.</param>
		protected int GetEffectiveDepartmentId(string requestDepartmentId)
		{
			if (IsSystemApiKeyRequest && !string.IsNullOrWhiteSpace(requestDepartmentId) && int.TryParse(requestDepartmentId, out var deptId) && deptId > 0)
				return deptId;

			return ClaimsAuthorizationHelper.GetDepartmentId();
		}
	}
}
