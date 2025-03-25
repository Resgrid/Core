using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Services.Middleware
{
	public class AuthenticationFailedContext : ResultContext<AuthenticationSchemeOptions>
	{
		public AuthenticationFailedContext(
			HttpContext context,
			AuthenticationScheme scheme,
			AuthenticationSchemeOptions options)
			: base(context, scheme, options)
		{
		}

		public Exception Exception { get; set; }
	}
}
