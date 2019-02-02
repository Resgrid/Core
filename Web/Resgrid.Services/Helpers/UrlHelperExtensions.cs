using System;
using System.Web.Mvc;

namespace Resgrid.Web.Helpers
{
	public static class UrlHelperExtensions
	{
		public static string ActionAbsolute(this UrlHelper url, string action, object routeValues)
		{
			var request = url.RequestContext.HttpContext.Request;
			var baseUri = new Uri(string.Format("{0}://{1}", request.Url.Scheme, request.Headers["Host"]));
			var actionUri = new Uri(baseUri, url.Action(action, routeValues));

			return actionUri.AbsoluteUri;
		}
	}
}