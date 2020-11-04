using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
#if (!DEBUG && !DOCKER)
	//[RequireHttps]
#endif
	[ApiVersion("3.0")]
	[ApiController]
	[Authorize(AuthenticationSchemes = "BasicAuthentication")]
	//[EnableCors("_resgridWebsiteAllowSpecificOrigins")]
	public class V3AuthenticatedApiControllerbase : ControllerBase
	{

		protected string UserId => ClaimsAuthorizationHelper.GetUserId();

		protected int DepartmentId => ClaimsAuthorizationHelper.GetDepartmentId();

		protected string UserName => ClaimsAuthorizationHelper.GetUsername();

		protected bool IsSystem
		{
			get
			{
				//return ((App_Start.ResgridPrincipleV3)this.User).IsSystem;
				return false;
			}
		}

		//[HttpOptions("Options")]
		//public HttpResponseMessage Options()
		//{
		//	var response = new HttpResponseMessage();
		//	response.StatusCode = HttpStatusCode.OK;
		//	response.Headers.Add("Access-Control-Allow-Origin", "*");
		//	response.Headers.Add("Access-Control-Request-Headers", "*");
		//	response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

		//	return response;
		//}
	}
}
