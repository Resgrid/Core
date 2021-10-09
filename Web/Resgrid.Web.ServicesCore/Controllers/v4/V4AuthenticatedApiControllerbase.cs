using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
#if (!DEBUG && !DOCKER)
	//[RequireHttps]
#endif
	[ApiController]
	[Produces("application/json")]
	[Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
	public class V4AuthenticatedApiControllerbase : ControllerBase
	{
		protected string UserId => ClaimsAuthorizationHelper.GetUserId();

		protected int DepartmentId => ClaimsAuthorizationHelper.GetDepartmentId();

		protected string UserName => ClaimsAuthorizationHelper.GetUsername();

		protected string TimeZone => ClaimsAuthorizationHelper.GetTimeZone();
	}
}
