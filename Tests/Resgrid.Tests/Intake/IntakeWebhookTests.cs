using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Signed webhook intake: X-Resgrid-Signature t=&lt;unix&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;, a timestamp tolerance, a body cap,
	/// per-token rate limiting, and the JSON body becoming an intake envelope.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class IntakeWebhookTests
	{
		private const string Secret = "whsec_test_secret_value";
		private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz012345";
		private static readonly DateTime Now = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);

		private Mock<IIntakeChannelsService> _channels;
		private Mock<IIntakeService> _intake;
		private IntakeEnvelope _received;
		private (bool Enabled, int Max, int Tolerance, int PerMinute) _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = (IntakeConfig.Enabled, IntakeConfig.MaxWebhookBodyBytes, IntakeConfig.WebhookTimestampToleranceSeconds, IntakeConfig.WebhookRequestsPerMinute);
			IntakeConfig.Enabled = true;
			IntakeConfig.MaxWebhookBodyBytes = 65536;
			IntakeConfig.WebhookTimestampToleranceSeconds = 300;
			IntakeConfig.WebhookRequestsPerMinute = 60;
			IntakeWebhookRateLimiter.Reset();
			_received = null;

			var channel = new IntakeChannel { IntakeChannelId = "wh-1", DepartmentId = 55, ChannelType = (int)IntakeChannelTypes.Webhook, Address = Token, IsEnabled = true };
			_channels = new Mock<IIntakeChannelsService>();
			_channels.Setup(c => c.ResolveChannelAsync(IntakeChannelTypes.Webhook, Token)).ReturnsAsync(channel);
			_channels.Setup(c => c.GetWebhookSecret(channel)).Returns(Secret);
			_intake = new Mock<IIntakeService>();
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeEnvelope e, CancellationToken _) => _received = e)
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.CallCreated, CallId = 900 });
		}

		[TearDown]
		public void TearDown()
		{
			(IntakeConfig.Enabled, IntakeConfig.MaxWebhookBodyBytes, IntakeConfig.WebhookTimestampToleranceSeconds, IntakeConfig.WebhookRequestsPerMinute) = _saved;
			IntakeWebhookRateLimiter.Reset();
		}

		private static long Unix(DateTime value) => new DateTimeOffset(value).ToUnixTimeSeconds();

		[Test]
		public void a_correct_signature_verifies()
		{
			var body = "{\"message\":\"test\"}";
			var header = IntakeWebhookSignature.Header(Secret, Unix(Now), body);

			IntakeWebhookSignature.Verify(header, Secret, body, Now, 300).Should().Be(IntakeWebhookSignatureResults.Valid);
		}

		[Test]
		public void the_signature_is_hex_hmac_sha256_of_timestamp_dot_body()
		{
			using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(Secret));
			var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("1763564400.{}"))).ToLowerInvariant();

			IntakeWebhookSignature.Compute(Secret, 1763564400, "{}").Should().Be(expected);
		}

		[Test]
		public void a_changed_body_or_wrong_secret_is_invalid()
		{
			var header = IntakeWebhookSignature.Header(Secret, Unix(Now), "{\"message\":\"a\"}");

			IntakeWebhookSignature.Verify(header, Secret, "{\"message\":\"b\"}", Now, 300).Should().Be(IntakeWebhookSignatureResults.Invalid);
			IntakeWebhookSignature.Verify(header, "other", "{\"message\":\"a\"}", Now, 300).Should().Be(IntakeWebhookSignatureResults.Invalid);
			IntakeWebhookSignature.Verify(header, null, "{\"message\":\"a\"}", Now, 300).Should().Be(IntakeWebhookSignatureResults.Invalid);
		}

		[Test]
		public void an_old_or_future_timestamp_is_stale()
		{
			var body = "{}";
			IntakeWebhookSignature.Verify(IntakeWebhookSignature.Header(Secret, Unix(Now.AddSeconds(-301)), body), Secret, body, Now, 300)
				.Should().Be(IntakeWebhookSignatureResults.Stale);
			IntakeWebhookSignature.Verify(IntakeWebhookSignature.Header(Secret, Unix(Now.AddSeconds(301)), body), Secret, body, Now, 300)
				.Should().Be(IntakeWebhookSignatureResults.Stale);
			IntakeWebhookSignature.Verify(IntakeWebhookSignature.Header(Secret, Unix(Now.AddSeconds(-299)), body), Secret, body, Now, 300)
				.Should().Be(IntakeWebhookSignatureResults.Valid);
		}

		[TestCase(null, IntakeWebhookSignatureResults.Missing)]
		[TestCase("", IntakeWebhookSignatureResults.Missing)]
		[TestCase("v1=abc", IntakeWebhookSignatureResults.Malformed)]
		[TestCase("t=123", IntakeWebhookSignatureResults.Malformed)]
		[TestCase("t=abc,v1=0000000000000000000000000000000000000000000000000000000000000000", IntakeWebhookSignatureResults.Malformed)]
		[TestCase("garbage", IntakeWebhookSignatureResults.Malformed)]
		public void malformed_headers_are_refused(string header, IntakeWebhookSignatureResults expected)
		{
			IntakeWebhookSignature.Verify(header, Secret, "{}", Now, 300).Should().Be(expected);
		}

		[Test]
		public void a_second_signature_for_rotation_is_accepted()
		{
			var body = "{}";
			var t = Unix(Now);
			var header = "t=" + t + ",v1=" + IntakeWebhookSignature.Compute("old-secret", t, body) + ",v1=" + IntakeWebhookSignature.Compute(Secret, t, body);

			IntakeWebhookSignature.Verify(header, Secret, body, Now, 300).Should().Be(IntakeWebhookSignatureResults.Valid);
		}

		private IntakeController Controller(string body, string signature)
		{
			var context = new DefaultHttpContext();
			var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
			context.Request.Body = new MemoryStream(bytes);
			context.Request.ContentLength = bytes.Length;
			context.Request.ContentType = "application/json";
			if (signature != null)
				context.Request.Headers[IntakeWebhookSignature.HeaderName] = signature;
			return new IntakeController(_channels.Object, _intake.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
		}

		private static int Status(IActionResult result) => result is ObjectResult o ? o.StatusCode ?? 200 : result is StatusCodeResult s ? s.StatusCode : 0;

		private IntakeController Signed(string body) => Controller(body, IntakeWebhookSignature.Header(Secret, Unix(DateTime.UtcNow), body));

		[Test]
		public async Task a_signed_request_creates_a_pending_call_through_intake()
		{
			var body = "{\"subject\":\"Lift assist\",\"message\":\"Resident fell\",\"sender\":\"CAD\",\"externalId\":\"CAD-1042\",\"latitude\":39.78,\"longitude\":\"-89.65\",\"address\":\"123 Main St\"}";

			var result = await Signed(body).Webhook(Token, CancellationToken.None);

			Status(result.Result).Should().Be(200);
			((IntakeWebhookResult)((ObjectResult)result.Result).Value).CallId.Should().Be(900);
			_received.ChannelType.Should().Be(IntakeChannelTypes.Webhook);
			_received.IntakeChannelId.Should().Be("wh-1");
			_received.ProviderMessageId.Should().Be("ext:CAD-1042");
			_received.Subject.Should().Be("Lift assist");
			_received.Body.Should().Be("Resident fell");
			_received.Sender.Should().Be("CAD");
			_received.Latitude.Should().Be(39.78);
			_received.Longitude.Should().Be(-89.65);
			_received.Address.Should().Be("123 Main St");
		}

		[Test]
		public async Task without_an_external_id_the_signature_identifies_the_request()
		{
			var body = "{\"message\":\"Alarm zone 4\"}";
			var signature = IntakeWebhookSignature.Header(Secret, Unix(DateTime.UtcNow), body);

			await Controller(body, signature).Webhook(Token, CancellationToken.None);

			_received.ProviderMessageId.Should().Be("sig:" + signature.Substring(signature.IndexOf("v1=", StringComparison.Ordinal) + 3));
		}

		[Test]
		public async Task an_unsigned_or_wrongly_signed_request_is_unauthorized_and_reaches_nothing()
		{
			var body = "{\"message\":\"x\"}";

			Status((await Controller(body, null).Webhook(Token, CancellationToken.None)).Result).Should().Be(401);
			Status((await Controller(body, IntakeWebhookSignature.Header("wrong", Unix(DateTime.UtcNow), body)).Webhook(Token, CancellationToken.None)).Result).Should().Be(401);
			Status((await Controller(body, IntakeWebhookSignature.Header(Secret, Unix(DateTime.UtcNow.AddHours(-1)), body)).Webhook(Token, CancellationToken.None)).Result).Should().Be(401);
			_intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task an_oversize_body_is_refused_before_anything_else()
		{
			IntakeConfig.MaxWebhookBodyBytes = 300;
			var body = "{\"message\":\"" + new string('x', 400) + "\"}";

			Status((await Signed(body).Webhook(Token, CancellationToken.None)).Result).Should().Be(413);
			_channels.Verify(c => c.ResolveChannelAsync(It.IsAny<IntakeChannelTypes>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task an_oversize_body_without_a_content_length_is_refused_too()
		{
			IntakeConfig.MaxWebhookBodyBytes = 300;
			var body = "{\"message\":\"" + new string('x', 400) + "\"}";
			var controller = Signed(body);
			controller.HttpContext.Request.ContentLength = null;

			Status((await controller.Webhook(Token, CancellationToken.None)).Result).Should().Be(413);
		}

		[Test]
		public async Task an_unknown_token_is_not_found()
		{
			Status((await Signed("{\"message\":\"x\"}").Webhook("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ", CancellationToken.None)).Result).Should().Be(404);
			Status((await Signed("{\"message\":\"x\"}").Webhook("bad token!", CancellationToken.None)).Result).Should().Be(404);
		}

		[TestCase("not json")]
		[TestCase("[1,2]")]
		[TestCase("{\"subject\":\"no message\"}")]
		[TestCase("{\"message\":{\"nested\":true}}")]
		public async Task a_body_without_a_message_is_a_bad_request(string body)
		{
			Status((await Signed(body).Webhook(Token, CancellationToken.None)).Result).Should().Be(400);
		}

		[Test]
		public async Task a_refusal_is_forbidden_and_a_failure_asks_for_a_retry()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Refused, Reason = "FeatureOff" });
			Status((await Signed("{\"message\":\"x\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(403);

			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Failed });
			Status((await Signed("{\"message\":\"y\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(503);

			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Duplicate, CallId = 900 });
			Status((await Signed("{\"message\":\"z\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(200);
		}

		[Test]
		public async Task a_token_sending_too_fast_is_rate_limited()
		{
			IntakeConfig.WebhookRequestsPerMinute = 2;

			Status((await Signed("{\"message\":\"1\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(200);
			Status((await Signed("{\"message\":\"2\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(200);
			Status((await Signed("{\"message\":\"3\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(429);
		}

		[Test]
		public async Task made_up_tokens_are_never_counted_and_never_use_up_a_real_channels_allowance()
		{
			IntakeConfig.WebhookRequestsPerMinute = 1;

			// A flood of unknown tokens: each is a 404 after its lookup, and none of them takes a place in the limiter.
			for (var i = 0; i < 5; i++)
				Status((await Signed("{\"message\":\"x\"}").Webhook("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ" + i, CancellationToken.None)).Result).Should().Be(404);

			IntakeWebhookRateLimiter.TrackedCount.Should().Be(0);
			Status((await Signed("{\"message\":\"real\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(200);
			IntakeWebhookRateLimiter.TrackedCount.Should().Be(1, "only the real channel is counted");
		}

		[Test]
		public async Task an_external_id_over_200_characters_is_refused_not_cut()
		{
			var longId = new string('A', 200) + "-second";
			var body = "{\"message\":\"x\",\"externalId\":\"" + longId + "\"}";

			Status((await Signed(body).Webhook(Token, CancellationToken.None)).Result).Should().Be(400);
			_intake.Verify(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()), Times.Never);

			var exact = "{\"message\":\"x\",\"externalId\":\"" + new string('A', 200) + "\"}";
			Status((await Signed(exact).Webhook(Token, CancellationToken.None)).Result).Should().Be(200);
			_received.ProviderMessageId.Should().Be("ext:" + new string('A', 200));
		}

		[Test]
		public async Task a_repeat_while_the_first_request_is_still_being_processed_is_conflict()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Duplicate, Reason = IntakeResultReasons.DuplicateInProgress });

			var result = await Signed("{\"message\":\"x\",\"externalId\":\"CAD-1\"}").Webhook(Token, CancellationToken.None);

			Status(result.Result).Should().Be(409);
			((IntakeWebhookResult)((ObjectResult)result.Result).Value).Status.Should().Be("InProgress");
		}

		[Test]
		public void the_rate_limiter_window_resets_each_minute()
		{
			IntakeWebhookRateLimiter.TryAcquire("t", Now, 1).Should().BeTrue();
			IntakeWebhookRateLimiter.TryAcquire("t", Now.AddSeconds(10), 1).Should().BeFalse();
			IntakeWebhookRateLimiter.TryAcquire("t", Now.AddMinutes(1), 1).Should().BeTrue();
		}

		[Test]
		public async Task the_host_switch_hides_the_endpoint()
		{
			IntakeConfig.Enabled = false;

			Status((await Signed("{\"message\":\"x\"}").Webhook(Token, CancellationToken.None)).Result).Should().Be(404);
		}
	}
}
