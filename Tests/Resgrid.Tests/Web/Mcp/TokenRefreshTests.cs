using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Web.Mcp;
using Resgrid.Web.Mcp.Infrastructure;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Resgrid.Web.Mcp.Tools;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// The MCP client signs in once, then keeps its session alive by exchanging single-use refresh tokens through the
	/// refresh_access_token tool.
	/// </summary>
	[TestFixture]
	public sealed class TokenRefreshTests
	{
		private const string TokenPairJson =
			@"{""access_token"":""access-2"",""token_type"":""Bearer"",""expires_in"":86400,""refresh_token"":""refresh-2""}";

		[Test]
		public async Task Authenticate_ShouldRequestOfflineAccessAndReturnTheRefreshToken()
		{
			string body = null;
			var apiClient = CreateApiClient(request =>
			{
				body = request.Content.ReadAsStringAsync().Result;
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TokenPairJson) };
			});

			var result = await apiClient.AuthenticateAsync("jane", "secret");

			Assert.That(FormValue(body, "grant_type"), Is.EqualTo("password"));
			Assert.That(FormValue(body, "scope").Split(' '), Does.Contain("offline_access"));
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.RefreshToken, Is.EqualTo("refresh-2"));
		}

		[Test]
		public async Task RefreshToken_ShouldPostTheRefreshGrantToTheTokenEndpoint()
		{
			HttpRequestMessage sent = null;
			string body = null;
			var apiClient = CreateApiClient(request =>
			{
				sent = request;
				body = request.Content.ReadAsStringAsync().Result;
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TokenPairJson) };
			});

			var result = await apiClient.RefreshTokenAsync("refresh-1");

			Assert.That(sent.Method, Is.EqualTo(HttpMethod.Post));
			Assert.That(sent.RequestUri.AbsolutePath, Is.EqualTo(V4Routes.Post.Token));
			Assert.That(FormValue(body, "grant_type"), Is.EqualTo("refresh_token"));
			Assert.That(FormValue(body, "refresh_token"), Is.EqualTo("refresh-1"));
			Assert.That(result.IsSuccess, Is.True);
			Assert.That(result.AccessToken, Is.EqualTo("access-2"));
			Assert.That(result.RefreshToken, Is.EqualTo("refresh-2"));
			Assert.That(result.ExpiresIn, Is.EqualTo(86400));
		}

		[Test]
		public async Task RefreshToken_ShouldReportTheServersReasonWhenTheGrantIsRefused()
		{
			var apiClient = CreateApiClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
			{
				Content = new StringContent(@"{""error"":""invalid_grant"",""error_description"":""The refresh token is no longer valid.""}")
			});

			var result = await apiClient.RefreshTokenAsync("refresh-1");

			Assert.That(result.IsSuccess, Is.False);
			Assert.That(result.ErrorMessage, Is.EqualTo("The refresh token is no longer valid."));
		}

		[Test]
		public async Task Refresh_ShouldShareOneExchangeBetweenCallersPresentingTheSameToken()
		{
			var exchange = new TaskCompletionSource<AuthenticationResult>();
			var apiClient = new Mock<IApiClient>();
			apiClient.Setup(x => x.RefreshTokenAsync("refresh-1", It.IsAny<CancellationToken>())).Returns(exchange.Task);
			var service = new TokenRefreshService(apiClient.Object, NullLogger<TokenRefreshService>.Instance);

			var first = service.RefreshAsync("refresh-1");
			var second = service.RefreshAsync("refresh-1");
			exchange.SetResult(new AuthenticationResult { IsSuccess = true, AccessToken = "access-2", RefreshToken = "refresh-2" });

			Assert.That((await first).RefreshToken, Is.EqualTo("refresh-2"));
			Assert.That((await second).RefreshToken, Is.EqualTo("refresh-2"));
			apiClient.Verify(x => x.RefreshTokenAsync("refresh-1", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Refresh_ShouldNotReuseACompletedExchange()
		{
			var apiClient = new Mock<IApiClient>();
			apiClient.Setup(x => x.RefreshTokenAsync("refresh-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AuthenticationResult { IsSuccess = true, AccessToken = "access-2", RefreshToken = "refresh-2" });
			var service = new TokenRefreshService(apiClient.Object, NullLogger<TokenRefreshService>.Instance);

			await service.RefreshAsync("refresh-1");
			await service.RefreshAsync("refresh-1");

			// A redeemed token goes back to the API, which decides whether it is still inside the reuse window.
			apiClient.Verify(x => x.RefreshTokenAsync("refresh-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task Refresh_ShouldKeepTheSharedExchangeRunningWhenOneCallerCancels()
		{
			var exchange = new TaskCompletionSource<AuthenticationResult>();
			var apiClient = new Mock<IApiClient>();
			apiClient.Setup(x => x.RefreshTokenAsync("refresh-1", It.IsAny<CancellationToken>())).Returns(exchange.Task);
			var service = new TokenRefreshService(apiClient.Object, NullLogger<TokenRefreshService>.Instance);
			using var cancellation = new CancellationTokenSource();

			var abandoned = service.RefreshAsync("refresh-1", cancellation.Token);
			var waiting = service.RefreshAsync("refresh-1");
			cancellation.Cancel();
			exchange.SetResult(new AuthenticationResult { IsSuccess = true, RefreshToken = "refresh-2" });

			Assert.That(async () => await abandoned, Throws.InstanceOf<OperationCanceledException>());
			Assert.That((await waiting).RefreshToken, Is.EqualTo("refresh-2"));
		}

		[Test]
		public async Task Refresh_ShouldRejectABlankTokenWithoutCallingTheApi()
		{
			var apiClient = new Mock<IApiClient>();
			var service = new TokenRefreshService(apiClient.Object, NullLogger<TokenRefreshService>.Instance);

			var result = await service.RefreshAsync(" ");

			Assert.That(result.IsSuccess, Is.False);
			apiClient.Verify(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task RefreshAccessTokenTool_ShouldReturnTheNewPair()
		{
			var refresh = new Mock<ITokenRefreshService>();
			refresh.Setup(x => x.RefreshAsync("refresh-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AuthenticationResult { IsSuccess = true, AccessToken = "access-2", TokenType = "Bearer", ExpiresIn = 86400, RefreshToken = "refresh-2" });

			var result = await CallRefreshTool(refresh.Object, "refresh-1");

			Assert.That(result.Value<bool>("success"), Is.True);
			Assert.That(result.Value<string>("accessToken"), Is.EqualTo("access-2"));
			Assert.That(result.Value<string>("refreshToken"), Is.EqualTo("refresh-2"));
			Assert.That(result.Value<int>("expiresIn"), Is.EqualTo(86400));
		}

		[Test]
		public async Task RefreshAccessTokenTool_ShouldSendTheCallerBackToAuthenticateWhenRefreshFails()
		{
			var refresh = new Mock<ITokenRefreshService>();
			refresh.Setup(x => x.RefreshAsync("refresh-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AuthenticationResult { IsSuccess = false, ErrorMessage = "The refresh token is no longer valid." });

			var result = await CallRefreshTool(refresh.Object, "refresh-1");

			Assert.That(result.Value<bool>("success"), Is.False);
			Assert.That(result.Value<string>("error"), Is.EqualTo("The refresh token is no longer valid. Call authenticate to sign in again."));
		}

		private static async Task<JObject> CallRefreshTool(ITokenRefreshService refreshService, string refreshToken)
		{
			var server = new McpServer("test", "1.0.0");
			new AuthenticationToolProvider(Mock.Of<IApiClient>(), refreshService, NullLogger<AuthenticationToolProvider>.Instance)
				.RegisterTools(server);

			var request = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = 1,
				["method"] = "tools/call",
				["params"] = new JObject
				{
					["name"] = "refresh_access_token",
					["arguments"] = new JObject { ["refreshToken"] = refreshToken }
				}
			};

			var response = await server.HandleRequestAsync(request.ToString(), CancellationToken.None);
			return JObject.Parse(JObject.Parse(response)["result"]["content"][0].Value<string>("text"));
		}

		private static ApiClient CreateApiClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			var httpClient = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://api.example.test/") };
			var factory = new Mock<IHttpClientFactory>();
			factory.Setup(x => x.CreateClient("ResgridApi")).Returns(httpClient);

			return new ApiClient(factory.Object, NullLogger<ApiClient>.Instance);
		}

		private static string FormValue(string formBody, string key)
		{
			foreach (var pair in formBody.Split('&'))
			{
				var parts = pair.Split('=', 2);
				if (WebUtility.UrlDecode(parts[0]) == key)
					return WebUtility.UrlDecode(parts[1]);
			}

			return null;
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
