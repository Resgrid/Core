using System;
using System.Linq;
using System.Net.Http;
using System.Web.Http.Controllers;

namespace Resgrid.Web.Services.Attributes
{
	public class TokenAuthenticationAttribute : System.Web.Http.Filters.ActionFilterAttribute
	{
		public override void OnActionExecuting(HttpActionContext actionContext)
		{
			string token = null;

			try
			{
				token = actionContext.Request.Headers.GetValues("RGAuth-Token").First();
			}
			catch (Exception)
			{
				actionContext.Response = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
				{ Content = new StringContent("Missing Authorization Token") };

				return;
			}

			if (String.IsNullOrEmpty(token))

			try
			{
				//Get Department
				base.OnActionExecuting(actionContext);
			}
			catch (Exception)
			{
				actionContext.Response = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
				{ Content = new StringContent("Unauthorized") };

				return;
			}
		}
	}
}