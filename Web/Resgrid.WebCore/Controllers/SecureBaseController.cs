using System;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.Helpers;

namespace Resgrid.Web
{
	public abstract class SecureBaseController : Controller
	{
		protected string UserId => ClaimsAuthorizationHelper.GetUserId();

		protected int DepartmentId => ClaimsAuthorizationHelper.GetDepartmentId();

		protected string UserName => ClaimsAuthorizationHelper.GetUsername();

		public string GetBaseUrl()
		{
			return $"{this.Request.Scheme}://{this.Request.Host}{this.Request.PathBase}";
		}

		public new void Unauthorized()
		{
			Response.Redirect("/Public/Unauthorized");
		}

		//public new void NotFound()
		//{
		//	Response.Redirect("/Public/NotFound");
		//}
	}
}
