using Resgrid.Web.Services.ApplicationCore;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Controllers.Version2;

namespace Resgrid.Web.Services.Controllers
{
	[System.Web.Http.AuthorizeAttribute]
	[JsonNetFormatterConfig]
	[EnableCors(origins: Config.ApiConfig.CorsAllowedHostnames, headers: "*", methods: Config.ApiConfig.CorsAllowedMethods, SupportsCredentials = true)]
	public class AuthenticatedApiControllerbase : ApiController
	{
		protected string UserName
		{
			get
			{
				return this.User.AuthToken().UserName;
			}
		}

		protected int DepartmentId
		{
			get
			{
				return this.User.AuthToken().DepartmentId;
			}
		}

		protected string DepartmentCode
		{
			get
			{
				return this.User.AuthToken().DepartmentCode;
			}
		}

	}
}
