using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Config;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Tests for per-department chatbot configuration (own LLM override, encrypted-at-rest key) and
	/// the session store's in-memory fallback behavior.
	/// </summary>
	[TestFixture]
	public class ChatbotDeptConfigAndSessionTests
	{
		private static Mock<ICacheProvider> CacheMock()
		{
			// Keep config caching out of the way and make invalidation a no-op.
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ReturnsAsync(true);
			return cache;
		}

		// ---- Per-department LLM override ---------------------------------------------------------

		[Test]
		public async Task GetLlmOverride_WhenEndpointAndKeyConfigured_ReturnsDecryptedOverride()
		{
			var repo = new Mock<IChatbotDepartmentConfigRepository>();
			repo.Setup(r => r.GetByDepartmentIdAsync(5)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				DepartmentId = 5,
				IsEnabled = true,
				LlmApiEndpoint = "https://dept.example/v1/chat/completions",
				LlmApiKey = "ENCRYPTED",
				LlmModelName = "dept-model"
			});

			var enc = new Mock<IEncryptionService>();
			enc.Setup(e => e.Decrypt("ENCRYPTED")).Returns("plaintext-key");

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object);

			var ovr = await service.GetLlmOverrideAsync(5);

			ovr.Should().NotBeNull();
			ovr.Endpoint.Should().Be("https://dept.example/v1/chat/completions");
			ovr.ApiKey.Should().Be("plaintext-key");
			ovr.Model.Should().Be("dept-model");
		}

		[Test]
		public async Task GetLlmOverride_WhenNotConfigured_ReturnsNull()
		{
			var repo = new Mock<IChatbotDepartmentConfigRepository>();
			repo.Setup(r => r.GetByDepartmentIdAsync(5)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				DepartmentId = 5,
				IsEnabled = true
				// no LLM endpoint/key => fall back to system provider
			});

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, Mock.Of<IEncryptionService>());

			var ovr = await service.GetLlmOverrideAsync(5);

			ovr.Should().BeNull();
		}

		[Test]
		public async Task SaveConfig_WithNewKey_EncryptsBeforePersisting()
		{
			var repo = new Mock<IChatbotDepartmentConfigRepository>();
			repo.Setup(r => r.GetByDepartmentIdAsync(7)).ReturnsAsync((ChatbotDepartmentConfig)null);
			repo.Setup(r => r.InsertAsync(It.IsAny<ChatbotDepartmentConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ChatbotDepartmentConfig c, CancellationToken _, bool __) => c);

			var enc = new Mock<IEncryptionService>();
			enc.Setup(e => e.Encrypt("super-secret")).Returns("CIPHER");

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object);

			var config = new ChatbotDepartmentConfig { DepartmentId = 7, IsEnabled = true };
			await service.SaveConfigAsync(config, "super-secret");

			config.LlmApiKey.Should().Be("CIPHER");
			enc.Verify(e => e.Encrypt("super-secret"), Times.Once);
		}

		[Test]
		public async Task SaveConfig_WithNullKey_PreservesExistingKey()
		{
			var repo = new Mock<IChatbotDepartmentConfigRepository>();
			repo.Setup(r => r.GetByDepartmentIdAsync(7)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				Id = "existing", DepartmentId = 7, LlmApiKey = "OLD-CIPHER"
			});
			repo.Setup(r => r.UpdateAsync(It.IsAny<ChatbotDepartmentConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ChatbotDepartmentConfig c, CancellationToken _, bool __) => c);

			var enc = new Mock<IEncryptionService>();

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object);

			var config = new ChatbotDepartmentConfig { DepartmentId = 7, IsEnabled = true };
			await service.SaveConfigAsync(config, null);

			config.LlmApiKey.Should().Be("OLD-CIPHER");
			enc.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
		}

		// ---- Session store in-memory fallback ----------------------------------------------------

		[Test]
		public async Task SessionStore_InMemory_ReusesSessionForSameUserAndDepartment()
		{
			ChatbotConfig.UseRedisSessionStore = false; // force in-memory path
			var store = new RedisSessionStore(Mock.Of<ICacheProvider>());

			// Unique user id to avoid interference with the static in-memory store across tests.
			var userId = "session-test-user-001";
			var first = await store.GetOrCreateAsync(userId, 1, ChatbotPlatform.SmsTwilio, "");
			var second = await store.GetOrCreateAsync(userId, 1, ChatbotPlatform.SmsTwilio, "");

			second.SessionId.Should().Be(first.SessionId);

			var fetched = await store.GetAsync(first.SessionId);
			fetched.Should().NotBeNull();
			fetched.SessionId.Should().Be(first.SessionId);
		}

		// ---- Rate limiting -----------------------------------------------------------------------

		[Test]
		public async Task RateLimiter_BlocksAfterPerUserLimitReached()
		{
			// Mock ICacheProvider.IsConnected() is false by default => exercises the in-memory path.
			var limiter = new ChatbotRateLimiter(Mock.Of<ICacheProvider>());

			(await limiter.TryAcquireAsync("rl-user-a", 1, 2, 100)).Should().BeTrue();
			(await limiter.TryAcquireAsync("rl-user-a", 1, 2, 100)).Should().BeTrue();
			(await limiter.TryAcquireAsync("rl-user-a", 1, 2, 100)).Should().BeFalse(); // third within the window
		}

		[Test]
		public async Task RateLimiter_BlocksAfterPerDepartmentLimitReached()
		{
			var limiter = new ChatbotRateLimiter(Mock.Of<ICacheProvider>());

			(await limiter.TryAcquireAsync("rl-user-b", 42, 100, 2)).Should().BeTrue();
			(await limiter.TryAcquireAsync("rl-user-c", 42, 100, 2)).Should().BeTrue();
			(await limiter.TryAcquireAsync("rl-user-d", 42, 100, 2)).Should().BeFalse(); // department cap reached
		}

		[Test]
		public async Task RateLimiter_TreatsZeroOrNegativeLimitAsUnlimited()
		{
			var limiter = new ChatbotRateLimiter(Mock.Of<ICacheProvider>());

			for (var i = 0; i < 50; i++)
				(await limiter.TryAcquireAsync("rl-user-e", 7, 0, 0)).Should().BeTrue();
		}
	}
}
