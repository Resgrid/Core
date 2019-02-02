using System;
using System.IdentityModel.Services;
using System.Security.Claims;
using System.Web.Mvc;
using Resgrid.Framework;

namespace Resgrid.Providers.Claims
{
	[AttributeUsage(AttributeTargets.Method)]
	public class AuthorizeActionAttributeBase: AuthorizeAttribute
	{
		public string Resrouce { get; set; }
		public string Action { get; set; }

		public AuthorizeActionAttributeBase(string resource, string action)
		{
			Resrouce = resource;
			Action = action;
		}

		public AuthorizeActionAttributeBase(string action)
		{
			Action = action;
		}

		public override void OnAuthorization(System.Web.Mvc.AuthorizationContext filterContext)
		{
			if (String.IsNullOrWhiteSpace(Resrouce))
			{
				var resourceAttribute = filterContext.Controller.GetType().GetAttribute<ClaimsResourceAttribute>();
				if (resourceAttribute != null)
				{
					Resrouce = resourceAttribute.Resrouce;
				}
			}

			base.OnAuthorization(filterContext);
		}

		protected override bool AuthorizeCore(System.Web.HttpContextBase httpContext)
		{
			if (String.IsNullOrWhiteSpace(Resrouce))
				return false;

			var context = new System.Security.Claims.AuthorizationContext(ClaimsPrincipal.Current, Resrouce, Action);
			ClaimsAuthorizationManager authorizationManager = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthorizationManager;

			return authorizationManager.CheckAccess(context);
		}
	}

	public class AuthorizeViewAttribute : AuthorizeActionAttributeBase
	{
		public AuthorizeViewAttribute(string resource)
			: base(resource, ResgridClaimTypes.Actions.View)
		{ }

		public AuthorizeViewAttribute()
			: base(null, ResgridClaimTypes.Actions.View)
		{ }
	}

	public class AuthorizeCreateAttribute : AuthorizeActionAttributeBase
	{
		public AuthorizeCreateAttribute(string resource)
			: base(resource, ResgridClaimTypes.Actions.Create)
		{ }

		public AuthorizeCreateAttribute()
			: base(null, ResgridClaimTypes.Actions.Create)
		{ }
	}

	public class AuthorizeUpdateAttribute : AuthorizeActionAttributeBase
	{
		public AuthorizeUpdateAttribute(string resource)
			: base(resource, ResgridClaimTypes.Actions.Update)
		{ }

		public AuthorizeUpdateAttribute()
			: base(null, ResgridClaimTypes.Actions.Update)
		{ }
	}

	public class AuthorizeDeleteAttribute : AuthorizeActionAttributeBase
	{
		public AuthorizeDeleteAttribute(string resource)
			: base(resource, ResgridClaimTypes.Actions.Delete)
		{ }

		public AuthorizeDeleteAttribute()
			: base(null, ResgridClaimTypes.Actions.Delete)
		{ }
	}
}