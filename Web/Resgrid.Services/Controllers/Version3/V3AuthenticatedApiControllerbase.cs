using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Mvc;
using Newtonsoft.Json;
using Resgrid.Web.Services.ApplicationCore;
using Resgrid.Web.Services.Controllers.Version2;

namespace Resgrid.Web.Services.Controllers.Version3
{
	[System.Web.Http.AuthorizeAttribute]
	[JsonNetFormatterConfig]
	[RequireHttps]
	[EnableCors(origins: Config.ApiConfig.CorsAllowedHostnames, headers: "*", methods: Config.ApiConfig.CorsAllowedMethods, SupportsCredentials = true)]
	public class V3AuthenticatedApiControllerbase : ApiController
	{
		protected string UserId
		{
			get
			{
				return this.User.V3AuthToken().UserId.ToUpper();
			}
		}

		protected string UserName
		{
			get
			{
				return this.User.V3AuthToken().UserName;
			}
		}

		protected int DepartmentId
		{
			get
			{
				return this.User.V3AuthToken().DepartmentId;
			}
		}

		protected bool IsSystem
		{
			get
			{
				return ((App_Start.ResgridPrincipleV3)this.User).IsSystem;
			}
		}
	}
}
