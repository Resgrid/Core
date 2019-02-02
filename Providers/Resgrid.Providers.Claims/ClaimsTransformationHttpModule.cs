using System;
using System.IdentityModel.Services;
using System.Security.Claims;
using System.Threading;
using System.Web;

namespace Resgrid.Providers.Claims
{
	public class ClaimsTransformationHttpModule : IHttpModule
	{
		public void Init(HttpApplication context)
		{
			context.PostAuthenticateRequest += context_PostAuthenticateRequest;
		}

		private void context_PostAuthenticateRequest(object sender, EventArgs e)
		{
			var context = ((HttpApplication)sender).Context;

			if (FederatedAuthentication.SessionAuthenticationModule != null &&
				FederatedAuthentication.SessionAuthenticationModule.ContainsSessionTokenCookie(context.Request.Cookies))
			{
				return;
			}

			var transformer = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthenticationManager;

			if (transformer != null)
			{
				if (context.User.Identity.IsAuthenticated)
				{
					var transformedPrincipal = transformer.Authenticate(context.Request.RawUrl, context.User as ClaimsPrincipal);

					context.User = transformedPrincipal;

					Thread.CurrentPrincipal = transformedPrincipal;
					HttpContext.Current.User = transformedPrincipal;
				}
			}
		}

		public void Dispose() { }
	}
}
