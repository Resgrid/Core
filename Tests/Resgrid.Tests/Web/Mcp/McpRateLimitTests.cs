using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Web.Mcp.Infrastructure;
using Resgrid.Web.Mcp.ModelContextProtocol;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// Tool calls are limited per signed-in session (access token), and calls made without a token (authenticate,
	/// refresh_access_token) per client address, more tightly.
	/// </summary>
	[TestFixture]
	public sealed class McpRateLimitTests
	{
		[Test]
		public async Task RateLimiter_ShouldRejectRequestsOverTheLimit()
		{
			using var limiter = new RateLimiter(NullLogger<RateLimiter>.Instance);

			for (var i = 0; i < 3; i++)
				Assert.That(await limiter.IsAllowedAsync("client-a", "tools/call", 3), Is.True);

			Assert.That(await limiter.IsAllowedAsync("client-a", "tools/call", 3), Is.False);
			Assert.That(await limiter.IsAllowedAsync("client-b", "tools/call", 3), Is.True, "Each client has its own window");
		}

		[Test]
		public async Task ToolCalls_ShouldBeLimitedPerAccessToken()
		{
			using var limiter = new RateLimiter(NullLogger<RateLimiter>.Instance);
			var handlerCalls = 0;
			var server = CreateServer(limiter, () => handlerCalls++);

			for (var i = 0; i < McpConfig.ToolCallsPerMinute; i++)
				Assert.That((await CallTool(server, "access-a", "203.0.113.5")).Value<bool>("isError"), Is.False);

			var limited = await CallTool(server, "access-a", "203.0.113.5");

			Assert.That(limited.Value<bool>("isError"), Is.True);
			Assert.That(ToolResult(limited).Value<string>("errorCode"), Is.EqualTo(McpToolErrorException.RateLimited));
			Assert.That(handlerCalls, Is.EqualTo(McpConfig.ToolCallsPerMinute), "A rate limited call must not reach the tool");

			var otherSession = await CallTool(server, "access-b", "203.0.113.5");

			Assert.That(otherSession.Value<bool>("isError"), Is.False, "Sessions sharing an address must not share a limit");
		}

		[Test]
		public async Task CallsWithoutAToken_ShouldBeLimitedPerClientAddress()
		{
			using var limiter = new RateLimiter(NullLogger<RateLimiter>.Instance);
			var server = CreateServer(limiter, () => { });

			for (var i = 0; i < McpConfig.UnauthenticatedCallsPerMinute; i++)
				Assert.That((await CallTool(server, null, "203.0.113.5")).Value<bool>("isError"), Is.False);

			var limited = await CallTool(server, null, "203.0.113.5");
			var otherAddress = await CallTool(server, null, "198.51.100.7");

			Assert.That(ToolResult(limited).Value<string>("errorCode"), Is.EqualTo(McpToolErrorException.RateLimited));
			Assert.That(otherAddress.Value<bool>("isError"), Is.False);
		}

		[Test]
		public async Task RateLimiter_ShouldNeverReceiveTheRawAccessToken()
		{
			var clientIds = new List<string>();
			var limiter = new Mock<IRateLimiter>();
			limiter.Setup(x => x.IsAllowedAsync(Capture.In(clientIds), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
			var server = CreateServer(limiter.Object, () => { });

			await CallTool(server, "secret-access-token", "203.0.113.5");

			Assert.That(clientIds.Single(), Does.StartWith("token:").And.Not.Contain("secret-access-token"));
		}

		private static McpServer CreateServer(IRateLimiter limiter, System.Action onCall)
		{
			var server = new McpServer("test", "1.0.0", rateLimiter: limiter);
			server.AddTool("test_tool", "test tool", new Dictionary<string, object>(), _ =>
			{
				onCall();
				return Task.FromResult<object>(new { success = true });
			});
			return server;
		}

		private static async Task<JObject> CallTool(McpServer server, string accessToken, string clientAddress)
		{
			var arguments = new JObject();
			if (accessToken != null)
				arguments["accessToken"] = accessToken;

			var request = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = 1,
				["method"] = "tools/call",
				["params"] = new JObject { ["name"] = "test_tool", ["arguments"] = arguments }
			};

			var response = await server.HandleRequestAsync(request.ToString(), clientAddress, CancellationToken.None);
			return (JObject)JObject.Parse(response)["result"];
		}

		private static JObject ToolResult(JObject result) => JObject.Parse(result["content"][0].Value<string>("text"));
	}
}
