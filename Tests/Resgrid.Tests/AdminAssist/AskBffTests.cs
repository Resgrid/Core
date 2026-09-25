using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Security;
using Resgrid.Web.Controllers;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class AskBffTests
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

		[TestCase("api/v4/AdminAssist/Ask", 100, true)]
		[TestCase("api/v4/AdminAssist/Conversation", 100, true)]
		[TestCase("api/v4/AdminAssist/ConversationExport", 100, true)]
		[TestCase("api/v4/AdminAssist/Diagnose", 100, true)]
		[TestCase("api/v4/AdminAssist/Diagnostic", 100, true)]
		[TestCase("api/v4/AdminAssist/DiagnosticSupportPreview", 100, true)]
		[TestCase("api/v4/AdminAssist/DiagnosticSupportExport", 100, true)]
		[TestCase("api/v4/AdminAssist/Diagnostics", 30, true)]
		[TestCase("api/v4/AdminAssist/AskStatus", 30, true)]
		[TestCase("api/v4/Chat/Conversations", 30, false)]
		public async Task Attended_grant_and_extended_deadline_are_limited_to_expected_routes(string path, int seconds, bool grantExpected)
		{
			using var fixture = new Fixture();
			fixture.Context.Request.Headers["X-Resgrid-Protected-Grant"] = "attended-grant";
			fixture.Context.Request.Headers.Authorization = "Bearer browser-supplied-token";
			await fixture.Controller.Proxy(path, CancellationToken.None);
			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(200));
			Assert.That(fixture.Sent, Has.Count.EqualTo(2));
			Assert.That(fixture.Sent[0].Path, Is.EqualTo("/api/v4/connect/token"));
			Assert.That(fixture.Sent[0].Grant, Is.Null);
			Assert.That(fixture.Sent[1].Grant, Is.EqualTo(grantExpected ? "attended-grant" : null));
			Assert.That(fixture.Sent[1].Bearer, Is.EqualTo("Bearer server-token"));
			Assert.That(fixture.Sent[1].Timeout, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
			Assert.That(fixture.Context.Response.Headers.CacheControl.ToString(), Does.Contain("no-store"));
		}

		[TestCase("multiple")]
		[TestCase("oversized")]
		[TestCase("newline")]
		public async Task Malformed_grant_is_rejected_before_protected_request(string kind)
		{
			using var fixture = new Fixture();
			fixture.Context.Request.Headers["X-Resgrid-Protected-Grant"] = kind switch
			{
				"multiple" => new StringValues(new[] { "one", "two" }),
				"oversized" => new StringValues(new string('x', 8193)),
				_ => new StringValues("one\r\nInjected: value")
			};
			await fixture.Controller.Proxy("api/v4/AdminAssist/Ask", CancellationToken.None);
			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(400));
			Assert.That(fixture.Sent, Has.Count.EqualTo(1));
			Assert.That(fixture.Sent[0].Grant, Is.Null);
		}

		[Test]
		public async Task Ask_requires_antiforgery_before_any_upstream_request()
		{
			using var fixture = new Fixture();
			fixture.Antiforgery.Setup(a => a.ValidateRequestAsync(fixture.Context)).ThrowsAsync(new AntiforgeryValidationException("invalid"));
			await fixture.Controller.Proxy("api/v4/AdminAssist/Ask", CancellationToken.None);
			Assert.That(fixture.Context.Response.StatusCode, Is.EqualTo(400));
			Assert.That(fixture.Sent, Is.Empty);
		}

		[Test]
		public void Client_disconnect_cancels_the_upstream_turn()
		{
			using var fixture = new Fixture();
			using var cancellation = new CancellationTokenSource();
			fixture.OnApiSend = async token => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); };
			Assert.CatchAsync<OperationCanceledException>(() => fixture.Controller.Proxy("api/v4/AdminAssist/Ask", cancellation.Token));
			Assert.That(fixture.Sent, Has.Count.EqualTo(2));
		}

		private sealed class Fixture : IDisposable
		{
			public readonly DefaultHttpContext Context = new();
			public readonly Mock<IAntiforgery> Antiforgery = new();
			public readonly List<(string Path, string Grant, string Bearer, TimeSpan Timeout)> Sent = new();
			public readonly WebApiBffController Controller;
			public Func<CancellationToken, Task> OnApiSend;
			private readonly MemoryCache _cache = new(new MemoryCacheOptions());
			private readonly ServiceProvider _services;
			private readonly List<HttpClient> _clients = new();

			public Fixture()
			{
				Context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
					new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(ClaimTypes.PrimaryGroupSid, "7"),
					new Claim(SessionClaimTypes.SessionId, "session"), new Claim(SessionClaimTypes.AuthenticationGeneration, "1")
				}, "test"));
				var authentication = new Mock<IAuthenticationService>();
				authentication.Setup(a => a.AuthenticateAsync(Context, null)).ReturnsAsync(AuthenticateResult.Success(
					new AuthenticationTicket(Context.User, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow }, "test")));
				_services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
				Context.RequestServices = _services;
				Context.Request.Method = "POST";
				Context.Request.ContentType = "application/json";
				Context.Request.Body = new MemoryStream("{}"u8.ToArray());
				Context.Response.Body = new MemoryStream();
				Antiforgery.Setup(a => a.ValidateRequestAsync(Context)).Returns(Task.CompletedTask);
				var factory = new Mock<IHttpClientFactory>();
				factory.Setup(f => f.CreateClient("ResgridWebBff")).Returns(() => {
					HttpClient client = null;
					client = new HttpClient(new Handler(async (request, token) => {
						var isExchange = request.RequestUri.AbsolutePath == "/api/v4/connect/token";
						Sent.Add((request.RequestUri.AbsolutePath,
							request.Headers.TryGetValues("X-Resgrid-Protected-Grant", out var values) ? string.Join(",", values) : null,
							request.Headers.Authorization?.ToString(), client.Timeout));
						if (!isExchange && OnApiSend != null) await OnApiSend(token);
						return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(isExchange
							? "{\"access_token\":\"server-token\",\"expires_in\":300}" : "{}") };
					})) { Timeout = TimeSpan.FromSeconds(30) };
					_clients.Add(client);
					return client;
				});
				Controller = new WebApiBffController(factory.Object, _cache, Antiforgery.Object) { ControllerContext = new ControllerContext { HttpContext = Context } };
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
