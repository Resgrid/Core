using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Request-bound <see cref="IProtectedGrantContext"/> (ADP plan 3.3 / 7.2): the caller's Protected Data
	/// Grant is the <c>X-Resgrid-Protected-Grant</c> header of the current request, and a request without an
	/// authenticated user is a workload (relay or system principal). Registered after ServicesModule so it
	/// replaces the workload default the module ships for hosts without requests.
	/// </summary>
	public sealed class HttpProtectedGrantContext : IProtectedGrantContext
	{
		public const string HeaderName = "X-Resgrid-Protected-Grant";
		public const string FormFieldName = "__ResgridProtectedGrant";

		private readonly IHttpContextAccessor _accessor;

		public HttpProtectedGrantContext(IHttpContextAccessor accessor)
		{
			_accessor = accessor;
		}

		public string GrantToken
		{
			get
			{
				var context = _accessor?.HttpContext;
				if (context == null)
					return null;
				string value = context.Request.Headers[HeaderName];
				if (string.IsNullOrWhiteSpace(value) && context.Request.HasFormContentType)
				{
					// A full-page form post cannot carry a header; the reveal module writes the grant into this
					// hidden field on the edit pages that need it (RMS plan section 5.9.3).
					value = context.Request.Form[FormFieldName];
				}
				return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			}
		}

		public string UserId
		{
			get
			{
				var user = _accessor?.HttpContext?.User;
				if (user?.Identity == null || !user.Identity.IsAuthenticated)
					return null;
				// Resgrid identities carry the user id as PrimarySid (ClaimsAuthorizationHelper.GetUserId); NameIdentifier is the API fallback.
				return user.FindFirst(ClaimTypes.PrimarySid)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
			}
		}

		public bool IsWorkloadCaller
		{
			get
			{
				var context = _accessor?.HttpContext;
				return context?.User?.Identity == null || !context.User.Identity.IsAuthenticated;
			}
		}
	}
}
