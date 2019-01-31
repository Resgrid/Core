using System;
using System.IO;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Stripe;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services
{
	public class StripeHandler : IHttpHandler
	{
		/// <summary>
		/// You will need to configure this handler in the Web.config file of your 
		/// web and register it with IIS before being able to use it. For more information
		/// see the following link: http://go.microsoft.com/?linkid=8101007
		/// </summary>
		#region IHttpHandler Members

		public bool IsReusable
		{
			// Return false in case your Managed Handler cannot be reused for another request.
			// Usually this would be false in case you have some state information preserved per request.
			get { return false; }
		}

		public StripeHandler()
		{

		}

		public void ProcessRequest(HttpContext context)
		{
			
		}

		#endregion
	}
}
