using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// GetAsync resolves a recovery token and must always hand back a result: a missing, unreadable or expired
	/// token is a normal outcome of the reset flow, and every one of them has to be indistinguishable to the
	/// caller so nothing leaks which tokens ever existed.
	/// </summary>
	[TestFixture]
	public class PasswordRecoveryServiceTests
	{
		private const string Token = "a-recovery-token";

		private Mock<ICacheProvider> _cacheProvider;
		private PasswordRecoveryService _service;

		[SetUp]
		public void SetUp()
		{
			_cacheProvider = new Mock<ICacheProvider>();
			_service = new PasswordRecoveryService(_cacheProvider.Object);
		}

		private void StoredPayloadIs(string json) =>
			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync(json);

		[TestCase(null)]
		[TestCase("")]
		[TestCase("   ")]
		public async Task GetAsync_BlankToken_ReportsNotFoundWithoutTouchingTheCache(string token)
		{
			var result = await _service.GetAsync(token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
			result.Request.Should().BeNull();
			_cacheProvider.Verify(x => x.GetStringAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task GetAsync_UnknownToken_ReportsNotFound()
		{
			StoredPayloadIs(null);

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
		}

		[Test]
		public async Task GetAsync_UnreadablePayload_ReportsNotFound()
		{
			StoredPayloadIs("{ this is not json");

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
		}

		[Test]
		public async Task GetAsync_ExpiredRequest_ReportsNotFound()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				Email = "someone@example.com",
				CreatedOn = DateTime.UtcNow.AddHours(-2),
				ExpiresOn = DateTime.UtcNow.AddMinutes(-1)
			}));

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
			result.Request.Should().BeNull();
		}

		[Test]
		public async Task GetAsync_LiveRequest_ReturnsTheRequest()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				Email = "someone@example.com",
				AuthenticationGeneration = 7,
				CreatedOn = DateTime.UtcNow,
				ExpiresOn = DateTime.UtcNow.AddMinutes(10)
			}));

			var result = await _service.GetAsync(Token);

			result.Found.Should().BeTrue();
			result.Request.Should().NotBeNull();
			result.Request?.UserId.Should().Be("user-1");
			result.Request?.AuthenticationGeneration.Should().Be(7);
		}

		[Test]
		public async Task GetAsync_NullPayload_ReportsNotFound()
		{
			StoredPayloadIs("null");

			var result = await _service.GetAsync(Token);

			result.Should().NotBeNull();
			result.Found.Should().BeFalse();
			result.Request.Should().BeNull();
		}

		[Test]
		public async Task TryConsumeAsync_ExpiredRequest_DoesNotConsume()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				ExpiresOn = DateTime.UtcNow.AddMinutes(-1)
			}));

			(await _service.TryConsumeAsync(Token)).Should().BeFalse();
			_cacheProvider.Verify(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
		}

		[Test]
		public async Task TryConsumeAsync_LiveRequest_ConsumesOnlyOnce()
		{
			StoredPayloadIs(JsonConvert.SerializeObject(new PasswordRecoveryRequest
			{
				UserId = "user-1",
				ExpiresOn = DateTime.UtcNow.AddMinutes(10)
			}));
			_cacheProvider.SetupSequence(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(1)
				.ReturnsAsync(2);

			(await _service.TryConsumeAsync(Token)).Should().BeTrue();
			(await _service.TryConsumeAsync(Token)).Should().BeFalse();
		}
	}
}
