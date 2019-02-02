using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Services.ApplicationCore
{
	public static class AuthExtensions
	{
		public static Resgrid.Web.Services.App_Start.AuthToken AuthToken(this System.Security.Principal.IPrincipal user)
		{
			return ((Resgrid.Web.Services.App_Start.ResgridPrinciple)user).AuthToken;
		}

		public static Resgrid.Web.Services.App_Start.V3AuthToken V3AuthToken(this System.Security.Principal.IPrincipal user)
		{
			return ((Resgrid.Web.Services.App_Start.ResgridPrincipleV3)user).AuthToken;
		}
	}
}