using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Web.Mcp;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Resgrid.Web.Mcp.Tools;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// A tool called with an expired access token must say so, with a code the client can act on, rather than the
	/// generic "try again later" that would have it retry the same dead token.
	/// </summary>
	[TestFixture]
	public sealed class McpToolErrorTests
	{
		// OpenIddict validation's challenge for an expired token, and SessionValidationMiddleware's for a revoked session.
		private const string ExpiredTokenChallenge =
			@"Bearer error=""invalid_token"", error_description=""The specified token is no longer valid."", error_uri=""https://documentation.openiddict.com/errors/ID2019""";
		private const string RevokedSessionChallenge = @"Bearer error=""invalid_token""";

		[TestCase(ExpiredTokenChallenge)]
		[TestCase(RevokedSessionChallenge)]
		public void ApiClient_ShouldReportARejectedTokenAsExpired(string challenge)
		{
			var apiClient = CreateApiClient(_ => Unauthorized(challenge));

			var error = Assert.ThrowsAsync<McpToolErrorException>(() => apiClient.GetAsync<object>(V4Routes.Get.ActiveCalls, "access-1"));

			Assert.That(error.ErrorCode, Is.EqualTo(McpToolErrorException.AccessTokenExpired));
			Assert.That(error.Message, Does.Contain("refresh_access_token"));
		}

		[Test]
		public void ApiClient_ShouldReportARejectedTokenOnDeleteAsExpired()
		{
			var apiClient = CreateApiClient(_ => Unauthorized(ExpiredTokenChallenge));

			var error = Assert.ThrowsAsync<McpToolErrorException>(() => apiClient.DeleteAsync($"{V4Routes.Delete.Message}?messageId=1", "access-1"));

			Assert.That(error.ErrorCode, Is.EqualTo(McpToolErrorException.AccessTokenExpired));
		}

		[Test]
		public void ApiClient_ShouldReportAnActionLevelUnauthorizedAsForbidden()
		{
			// v4 actions return a bare Unauthorized() (no challenge) when the user may not touch the record.
			var apiClient = CreateApiClient(_ => Unauthorized(null));

			var error = Assert.ThrowsAsync<McpToolErrorException>(() => apiClient.GetAsync<object>(V4Routes.Get.ActiveCalls, "access-1"));

			Assert.That(error.ErrorCode, Is.EqualTo(McpToolErrorException.Forbidden));
		}

		[Test]
		public void ApiClient_ShouldReportAPolicyRefusalAsForbidden()
		{
			var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

			var error = Assert.ThrowsAsync<McpToolErrorException>(() => apiClient.GetAsync<object>(V4Routes.Get.ActiveCalls, "access-1"));

			Assert.That(error.ErrorCode, Is.EqualTo(McpToolErrorException.Forbidden));
		}

		[Test]
		public void ApiClient_ShouldLeaveOtherFailuresAsHttpErrors()
		{
			var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

			Assert.ThrowsAsync<HttpRequestException>(() => apiClient.GetAsync<object>(V4Routes.Get.ActiveCalls, "access-1"));
		}

		[Test]
		public async Task Tool_ShouldReturnAccessTokenExpiredWhenTheApiRejectsTheToken()
		{
			var server = new McpServer("test", "1.0.0");
			new CallsToolProvider(CreateApiClient(_ => Unauthorized(ExpiredTokenChallenge)), NullLogger<CallsToolProvider>.Instance)
				.RegisterTools(server);

			var result = await CallTool(server, "get_active_calls", new JObject { ["accessToken"] = "access-1" });

			Assert.That(result.Value<bool>("success"), Is.False);
			Assert.That(result.Value<string>("errorCode"), Is.EqualTo(McpToolErrorException.AccessTokenExpired));
			Assert.That(result.Value<string>("error"), Does.Contain("refresh_access_token"));
		}

		[Test]
		public async Task Tool_ShouldReturnForbiddenWhenTheUserIsNotPermitted()
		{
			var server = new McpServer("test", "1.0.0");
			new PersonnelToolProvider(CreateApiClient(_ => Unauthorized(null)), NullLogger<PersonnelToolProvider>.Instance)
				.RegisterTools(server);

			var result = await CallTool(server, "set_personnel_status",
				new JObject { ["accessToken"] = "access-1", ["userId"] = "someone-else", ["statusType"] = 2 });

			Assert.That(result.Value<bool>("success"), Is.False);
			Assert.That(result.Value<string>("errorCode"), Is.EqualTo(McpToolErrorException.Forbidden));
		}

		[Test]
		public void ToolCatchAlls_ShouldLetMcpToolErrorsReachTheServer()
		{
			var root = RepositoryRoot();
			if (root == null)
				Assert.Ignore("Resgrid.sln not found above the test directory; the MCP source is not available to scan.");

			var toolsDirectory = Path.Combine(root, "Web", "Resgrid.Web.Mcp", "Tools");
			var offenders = Directory.EnumerateFiles(toolsDirectory, "*.cs")
				.SelectMany(file => File.ReadLines(file)
					.Select((line, index) => (line, index))
					.Where(x => x.line.Contains("catch (Exception") && !x.line.Contains("when (ex is not McpToolErrorException)"))
					.Select(x => $"{Path.GetRelativePath(root, file)}:{x.index + 1}: {x.line.Trim()}"))
				.ToList();

			Assert.That(offenders, Is.Empty,
				"A tool's catch-all must not swallow McpToolErrorException: add 'when (ex is not McpToolErrorException)'");
		}

		private static async Task<JObject> CallTool(McpServer server, string toolName, JObject arguments)
		{
			var request = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = 1,
				["method"] = "tools/call",
				["params"] = new JObject { ["name"] = toolName, ["arguments"] = arguments }
			};

			var response = JObject.Parse(await server.HandleRequestAsync(request.ToString(), CancellationToken.None));
			Assert.That(response["error"]?.Type ?? JTokenType.Null, Is.EqualTo(JTokenType.Null), "Tool errors must come back as a result, not a JSON-RPC error");
			Assert.That(response["result"].Value<bool>("isError"), Is.True, "A failed tool call must be flagged with isError");

			return JObject.Parse(response["result"]["content"][0].Value<string>("text"));
		}

		private static HttpResponseMessage Unauthorized(string challenge)
		{
			var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
			if (challenge != null)
				response.Headers.TryAddWithoutValidation("WWW-Authenticate", challenge);
			return response;
		}

		private static ApiClient CreateApiClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			var factory = new Mock<IHttpClientFactory>();
			factory.Setup(x => x.CreateClient("ResgridApi"))
				.Returns(() => new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://api.example.test/") });

			return new ApiClient(factory.Object, NullLogger<ApiClient>.Instance);
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory?.FullName;
		}

		private sealed class StubHandler : HttpMessageHandler
		{
			private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
			public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { _respond = respond; }
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
				=> Task.FromResult(_respond(request));
		}
	}
}
