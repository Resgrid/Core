using Stripe;
using System;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;

namespace Resgrid.Web.Services
{
	// Note: For instructions on enabling IIS6 or IIS7 classic mode, 
	// visit http://go.microsoft.com/?LinkId=9394801

	public class WebApiApplication : System.Web.HttpApplication
	{
		protected void Application_Error()
		{
			Exception unhandledException = Server.GetLastError();
			HttpException httpException = unhandledException as HttpException;
			if (httpException == null)
			{
				Exception innerException = unhandledException.InnerException;
				httpException = innerException as HttpException;
			}

			// We don't care about operation canceled: http://stackoverflow.com/questions/22157596/asp-net-web-api-operationcanceledexception-when-browser-cancels-the-request
			var cancelledException = unhandledException as OperationCanceledException;
			if (cancelledException != null)
			{
				var token = cancelledException.CancellationToken;
				if (token.IsCancellationRequested)
				{
					Server.ClearError();
					Request.Abort();
					return;
				}
			}

			if (httpException != null)
			{
				int httpCode = httpException.GetHttpCode();
				switch (httpCode)
				{
					default:
						Response.Redirect("~/Home/Error", false);
						break;
				}
			}
			else
			{
				var invalidOperationException = unhandledException as InvalidOperationException;
				if (invalidOperationException != null)
				{
					Server.ClearError();
					Request.Abort();
					return;
				}

				Framework.Logging.LogException(unhandledException);
			}
		}

		protected void Application_BeginRequest(object sender, EventArgs e)
		{
			Context.Response.AppendHeader("Access-Control-Allow-Credentials", "true");
			var referrer = Request.UrlReferrer;
			if (Context.Request.Path.Contains("signalr/") && referrer != null)
			{
				Context.Response.AppendHeader("Access-Control-Allow-Origin", referrer.Scheme + "://" + referrer.Authority);
			}
		}

		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();
		}
	}
}
