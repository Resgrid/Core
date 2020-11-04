using System.Security.Principal;
using System.Web;
using Moq;

namespace Resgrid.Tests.Helpers
{
	public static class MoqHelpers
	{
		//public static HttpContextBase GetHttpContext(IPrincipal principal)
		//{
		//	var httpContext = new Mock<HttpContextBase>();
		//	var request = new Mock<HttpRequestBase>();
		//	var response = new Mock<HttpResponseBase>();
		//	var session = new Mock<HttpSessionStateBase>();
		//	var server = new Mock<HttpServerUtilityBase>();
		//	var user = principal;


		//	httpContext.Setup(ctx => ctx.Request).Returns(request.Object);
		//	httpContext.Setup(ctx => ctx.Response).Returns(response.Object);
		//	httpContext.Setup(ctx => ctx.Session).Returns(session.Object);
		//	httpContext.Setup(ctx => ctx.Server).Returns(server.Object);
		//	httpContext.Setup(ctx => ctx.User).Returns(user);

		//	return httpContext.Object;
		//}
	}
}
