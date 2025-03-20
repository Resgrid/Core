using Microsoft.AspNetCore.Builder;

namespace Resgrid.Web.ServicesCore.Middleware
{
	public static class MiddlewareExtensions
	{
		public static IApplicationBuilder UseV3AuthTokenMiddleware(this IApplicationBuilder builder)
		{
			return builder.UseMiddleware<AuthTokenMiddleware>();
		}
	}
}
