using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Security;
using Resgrid.Web.Controllers;

namespace Resgrid.Tests.Web
{
	// A pooled keep-alive connection reset as the request goes out (RESGRID-WEB GET WebApiBff.Proxy,
	// "Connection reset by peer" on api/v4/Chat/GetChannels) surfaced to the browser as a 503.
	[TestFixture, NonParallelizable]
	public class WebApiBffProxyRetryTests
	{
		private string _apiBase;
		private string _internalKey;

		[SetUp]
		public void SetUp()
		{
			_apiBase = SystemBehaviorConfig.ResgridApiBaseUrl;
			_internalKey = ApiConfig.BackendInternalApikey;
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.invalid/";
			ApiConfig.BackendInternalApikey = "test-internal-key";
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ResgridApiBaseUrl = _apiBase;
			ApiConfig.BackendInternalApikey = _internalKey;
		}

		[Test]
		public async Task Get_is_replayed_once_after_a_connection_reset()
		{
			using var fixture = new Fixture("GET");
			fixture.Failures.Enqueue(ConnectionReset());

			await fixture.Controller.Proxy("api/v4/Chat/GetChannels", CancellationToken.None);

			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(200));
			Assert.That(fixture.ApiSends, Is.EqualTo(2));
			Assert.That(fixture.LastBearer, Is.EqualTo("Bearer server-token"));
		}

		[Test]
		public async Task Get_that_fails_again_on_the_replay_returns_503()
		{
			using var fixture = new Fixture("GET");
			fixture.Failures.Enqueue(ConnectionReset());
			fixture.Failures.Enqueue(ConnectionReset());

			await fixture.Controller.Proxy("api/v4/Chat/GetChannels", CancellationToken.None);

			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(503));
			Assert.That(fixture.ApiSends, Is.EqualTo(2));
		}

		[Test]
		public async Task Post_is_never_replayed()
		{
			using var fixture = new Fixture("POST");
			fixture.Failures.Enqueue(ConnectionReset());

			await fixture.Controller.Proxy("api/v4/Chat/SendMessage", CancellationToken.None);

			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(503));
			Assert.That(fixture.ApiSends, Is.EqualTo(1));
		}

		[Test]
		public async Task Get_failure_that_is_not_a_dropped_connection_is_not_replayed()
		{
			using var fixture = new Fixture("GET");
			fixture.Failures.Enqueue(new HttpRequestException("No such host is known."));

			await fixture.Controller.Proxy("api/v4/Chat/GetChannels", CancellationToken.None);

			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(503));
			Assert.That(fixture.ApiSends, Is.EqualTo(1));
		}

		[Test]
		public async Task Token_exchange_is_replayed_once_after_a_connection_reset()
		{
			using var fixture = new Fixture("GET");
			fixture.ExchangeFailures.Enqueue(ConnectionReset());

			await fixture.Controller.Proxy("api/v4/Chat/GetChannels", CancellationToken.None);

			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(200));
			Assert.That(fixture.ExchangeBodies, Has.Count.EqualTo(2));
			Assert.That(fixture.ExchangeBodies[1], Is.EqualTo(fixture.ExchangeBodies[0]));
			Assert.That(fixture.ExchangeBodies[1], Does.Contain("grant_type=web_session"));
			Assert.That(fixture.ApiSends, Is.EqualTo(1));
			Assert.That(fixture.LastBearer, Is.EqualTo("Bearer server-token"));
		}

		[Test]
		public async Task Token_exchange_that_fails_again_on_the_replay_returns_503()
		{
			using var fixture = new Fixture("GET");
			fixture.ExchangeFailures.Enqueue(ConnectionReset());
			fixture.ExchangeFailures.Enqueue(ConnectionReset());

			await fixture.Controller.Proxy("api/v4/Chat/GetChannels", CancellationToken.None);

			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(503));
			Assert.That(fixture.ExchangeBodies, Has.Count.EqualTo(2));
			Assert.That(fixture.ApiSends, Is.EqualTo(0));
		}

		private static HttpRequestException ConnectionReset() =>
			new("An error occurred while sending the request.",
				new IOException("Unable to read data from the transport connection: Connection reset by peer.",
					new SocketException((int)SocketError.ConnectionReset)));

		private sealed class Fixture : IDisposable
		{
			public readonly DefaultHttpContext Context = new();
			public readonly Queue<Exception> Failures = new();
			public readonly Queue<Exception> ExchangeFailures = new();
			public readonly List<string> ExchangeBodies = new();
			public readonly WebApiBffController Controller;
			public int ApiSends;
			public string LastBearer;
			private readonly MemoryCache _cache = new(new MemoryCacheOptions());
			private readonly ServiceProvider _services;
			private readonly List<HttpClient> _clients = new();

			public Fixture(string method)
			{
				Context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
					new Claim(ClaimTypes.NameIdentifier, "user"), new Claim(ClaimTypes.PrimaryGroupSid, "7"),
					new Claim(SessionClaimTypes.SessionId, "session"), new Claim(SessionClaimTypes.AuthenticationGeneration, "1")
				}, "test"));
				var authentication = new Mock<IAuthenticationService>();
				authentication.Setup(a => a.AuthenticateAsync(Context, null)).ReturnsAsync(AuthenticateResult.Success(
					new AuthenticationTicket(Context.User, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow }, "test")));
				_services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
				Context.RequestServices = _services;
				Context.Request.Method = method;
				if (method == "POST")
				{
					Context.Request.ContentType = "application/json";
					Context.Request.Body = new MemoryStream("{}"u8.ToArray());
				}
				Context.Response.Body = new MemoryStream();
				var antiforgery = new Mock<IAntiforgery>();
				antiforgery.Setup(a => a.ValidateRequestAsync(Context)).Returns(Task.CompletedTask);
				var factory = new Mock<IHttpClientFactory>();
				factory.Setup(f => f.CreateClient("ResgridWebBff")).Returns(() => {
					var client = new HttpClient(new Handler(async (request, token) => {
						if (request.RequestUri.AbsolutePath == "/api/v4/connect/token")
						{
							ExchangeBodies.Add(await request.Content.ReadAsStringAsync(token));
							if (ExchangeFailures.Count > 0)
								throw ExchangeFailures.Dequeue();
							return new HttpResponseMessage(HttpStatusCode.OK)
								{ Content = new StringContent("{\"access_token\":\"server-token\",\"expires_in\":300}") };
						}

						ApiSends++;
						LastBearer = request.Headers.Authorization?.ToString();
						if (Failures.Count > 0)
							throw Failures.Dequeue();
						return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
					})) { Timeout = TimeSpan.FromSeconds(30) };
					_clients.Add(client);
					return client;
				});
				Controller = new WebApiBffController(factory.Object, _cache, antiforgery.Object) { ControllerContext = new ControllerContext { HttpContext = Context } };
			}

			public void Dispose()
			{
				foreach (var client in _clients) client.Dispose();
				_cache.Dispose(); _services.Dispose(); Context.Request.Body.Dispose(); Context.Response.Body.Dispose();
			}
		}

		private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
		{
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
		}
	}
}
