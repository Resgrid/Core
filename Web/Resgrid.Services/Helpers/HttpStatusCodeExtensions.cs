using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace Resgrid.Web.Services.Helpers
{
	public static class HttpStatusCodeExtensions
	{
		public static HttpResponseException AsException(this HttpStatusCode code)
		{
			return new HttpResponseException(new HttpResponseMessage(code));
		}
	}
}
