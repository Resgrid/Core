using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Services.Middleware
{
	/// <summary>
	/// Class ValidateCredentialsContext.
	/// Implements the <see cref="Microsoft.AspNetCore.Authentication.ResultContext{Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions}" />
	/// </summary>
	/// <seealso cref="Microsoft.AspNetCore.Authentication.ResultContext{Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions}" />
	public class ValidateCredentialsContext : ResultContext<AuthenticationSchemeOptions>
	{
		public ValidateCredentialsContext(
			HttpContext context,
			AuthenticationScheme scheme,
			AuthenticationSchemeOptions options)
			: base(context, scheme, options)
		{
		}

		/// <summary>
		/// Gets or sets the <see cref="T:System.Security.Claims.ClaimsPrincipal" /> containing the user claims.
		/// </summary>
		/// <value>The principal.</value>
		public ClaimsPrincipal Principal { get; set; }
	}
}
