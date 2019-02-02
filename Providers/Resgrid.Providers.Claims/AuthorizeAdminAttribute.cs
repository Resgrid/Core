using System;
using System.IdentityModel.Services;
using System.Security.Claims;
using System.Web.Mvc;

namespace Resgrid.Providers.Claims
{
	[AttributeUsage(AttributeTargets.Method)]
	public class AuthorizeAdminAttribute: AuthorizeAttribute
	{
		public System.Security.Claims.AuthorizationContext Context { get; set; }

		public AuthorizeAdminAttribute()
		{
			Context = ResgridAuthorizationContext.BuildAdminRoleContext();
		}

		protected override bool AuthorizeCore(System.Web.HttpContextBase httpContext)
		{
			if (Context == null)
				return false;

			ClaimsAuthorizationManager authorizationManager = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthorizationManager;

			return authorizationManager.CheckAccess(Context);
		}
	}
}
