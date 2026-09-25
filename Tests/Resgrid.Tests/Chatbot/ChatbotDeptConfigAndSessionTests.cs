using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Framework;
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

		private static IEnhancedAiAccessService OwnProvider(bool allowed) =>
			OwnProvider(allowed ? OwnLlmProviderStatus.Allowed : OwnLlmProviderStatus.AddonRequired);

		private static IEnhancedAiAccessService OwnProvider(OwnLlmProviderStatus status)
		{
			var access = new Mock<IEnhancedAiAccessService>();
			access.Setup(a => a.GetOwnLlmProviderStatusAsync(It.IsAny<int>())).ReturnsAsync(status);
			return access.Object;
		}

		// ---- Per-department LLM override ---------------------------------------------------------

		[TestCase(OwnLlmProviderStatus.AddonRequired)]
		[TestCase(OwnLlmProviderStatus.DataProtectionEnabled)]
		[TestCase(OwnLlmProviderStatus.Unknown)]
		public async Task GetLlmOverride_WithoutEnhancedAiOrUnderDataProtection_IsInertAndTheSystemProviderAnswers(OwnLlmProviderStatus status)
		{
			var repo = new Mock<IChatbotDepartmentConfigRepository>();
			repo.Setup(r => r.GetByDepartmentIdAsync(5)).ReturnsAsync(new ChatbotDepartmentConfig
			{
				DepartmentId = 5, IsEnabled = true, LlmApiEndpoint = "https://api.openai.com/v1/chat/completions", LlmApiKey = "ENCRYPTED"
			});
			var enc = new Mock<IEncryptionService>();
			enc.Setup(e => e.Decrypt("ENCRYPTED")).Returns("plaintext-key");

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object, OwnProvider(status));

			(await service.GetLlmOverrideAsync(5)).Should().BeNull();
			(await service.GetLlmOverrideStatusAsync(5)).Should().Be(status);
			enc.Verify(e => e.Decrypt(It.IsAny<string>()), Times.Never, "the key is not decrypted when it cannot be used");
		}

		[Test]
		public async Task OwnProviderEntitlement_IsCachedBrieflyAndClearedOnSave()
		{
			var saved = SystemBehaviorConfig.CacheEnabled;
			try
			{
				SystemBehaviorConfig.CacheEnabled = true;
				var store = new System.Collections.Generic.Dictionary<string, string>();
				var cache = new Mock<ICacheProvider>();
				cache.Setup(c => c.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string k) => store.TryGetValue(k, out var v) ? v : null);
				cache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), TimeSpan.FromMinutes(5)))
					.Callback((string k, string v, TimeSpan _) => store[k] = v).ReturnsAsync(true);
				cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).Callback((string k) => store.Remove(k)).ReturnsAsync(true);
				var access = new Mock<IEnhancedAiAccessService>();
				access.Setup(a => a.GetOwnLlmProviderStatusAsync(9)).ReturnsAsync(OwnLlmProviderStatus.Allowed);
				var repo = new Mock<IChatbotDepartmentConfigRepository>();
				repo.Setup(r => r.UpdateAsync(It.IsAny<ChatbotDepartmentConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatbotDepartmentConfig c, CancellationToken _, bool __) => c);
				repo.Setup(r => r.GetByDepartmentIdAsync(9)).ReturnsAsync(new ChatbotDepartmentConfig { Id = "x", DepartmentId = 9 });
				var service = new ChatbotDepartmentConfigService(repo.Object, cache.Object, Mock.Of<IEncryptionService>(), access.Object);

				(await service.GetLlmOverrideStatusAsync(9)).Should().Be(OwnLlmProviderStatus.Allowed);
				access.Setup(a => a.GetOwnLlmProviderStatusAsync(9)).ReturnsAsync(OwnLlmProviderStatus.DataProtectionEnabled);
				(await service.GetLlmOverrideStatusAsync(9)).Should().Be(OwnLlmProviderStatus.Allowed, "the Billing API is not asked on every chatbot message");
				(await service.GetLlmOverrideStatusAsync(9, bypassCache: true)).Should().Be(OwnLlmProviderStatus.DataProtectionEnabled, "settings pages read it fresh");
				(await service.GetLlmOverrideStatusAsync(9)).Should().Be(OwnLlmProviderStatus.DataProtectionEnabled, "a fresh read refreshes the cache");

				access.Setup(a => a.GetOwnLlmProviderStatusAsync(9)).ReturnsAsync(OwnLlmProviderStatus.Allowed);
				await service.SaveConfigAsync(new ChatbotDepartmentConfig { DepartmentId = 9 });
				(await service.GetLlmOverrideStatusAsync(9)).Should().Be(OwnLlmProviderStatus.Allowed);
			}
			finally { SystemBehaviorConfig.CacheEnabled = saved; }
		}

		[Test]
		public void DepartmentConfig_SerializesForDistributedCache()
		{
			var createdAt = DateTime.UtcNow.AddDays(-1);
			var updatedAt = DateTime.UtcNow;
			var config = new ChatbotDepartmentConfig
			{
				Id = "config-id",
				DepartmentId = 5,
				IsEnabled = true,
				AllowedPlatforms = "Discord,Telegram",
				MaxSessionsPerUser = 4,
				SessionTtlMinutes = 45,
				AllowDispatchViaChatbot = true,
				RequireConfirmationForStatusChange = true,
				LlmApiEndpoint = "https://dept.example/v1/chat/completions",
				LlmApiKey = "ENCRYPTED",
				LlmModelName = "dept-model",
				MessagesPerUserPerMinute = 12,
				MessagesPerDepartmentPerMinute = 120,
				RequireLinkingConfirmation = false,
				ProactiveNotificationsEnabled = true,
				CreatedAt = createdAt,
				UpdatedAt = updatedAt
			};

			var serialized = ObjectSerialization.Serialize(config);
			var deserialized = ObjectSerialization.Deserialize<ChatbotDepartmentConfig>(serialized);

			deserialized.Should().BeEquivalentTo(config, options => options
				.Excluding(value => value.IdValue)
				.Excluding(value => value.TableName)
				.Excluding(value => value.IdName)
				.Excluding(value => value.IdType)
				.Excluding(value => value.IgnoredProperties));
		}

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

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object, OwnProvider(true));

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

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, Mock.Of<IEncryptionService>(), OwnProvider(true));

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

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object, OwnProvider(true));

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

			var service = new ChatbotDepartmentConfigService(repo.Object, CacheMock().Object, enc.Object, OwnProvider(true));

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

		[Test]
		public async Task SessionStore_InMemory_AppliesRequestedTtlBeforeExpiryCheck()
		{
			// Arrange
			ChatbotConfig.UseRedisSessionStore = false;
			var store = new RedisSessionStore(Mock.Of<ICacheProvider>());
			var userId = "session-ttl-expiry-user-001";
			var first = await store.GetOrCreateAsync(userId, 1, ChatbotPlatform.SmsTwilio, "", 30);
			first.State = ChatbotDialogState.AwaitingConfirmation;
			first.LastActivity = DateTime.UtcNow.AddMinutes(-10);

			// Act
			var second = await store.GetOrCreateAsync(userId, 1, ChatbotPlatform.SmsTwilio, "", 5);

			// Assert
			second.SessionId.Should().NotBe(first.SessionId);
			second.State.Should().Be(ChatbotDialogState.Idle);
			second.TtlMinutes.Should().Be(5);
		}

		[Test]
		public async Task SessionStore_InMemory_PreservesValidSessionStateWhenApplyingRequestedTtl()
		{
			// Arrange
			ChatbotConfig.UseRedisSessionStore = false;
			var store = new RedisSessionStore(Mock.Of<ICacheProvider>());
			var userId = "session-ttl-valid-user-001";
			var first = await store.GetOrCreateAsync(userId, 1, ChatbotPlatform.SmsTwilio, "", 30);
			first.State = ChatbotDialogState.AwaitingConfirmation;
			first.PendingIntent = ChatbotIntentType.DispatchCall;
			first.Context["description"] = "Structure fire";
			first.LastActivity = DateTime.UtcNow.AddMinutes(-2);

			// Act
			var second = await store.GetOrCreateAsync(userId, 1, ChatbotPlatform.SmsTwilio, "", 5);

			// Assert
			second.SessionId.Should().Be(first.SessionId);
			second.TtlMinutes.Should().Be(5);
			second.State.Should().Be(ChatbotDialogState.AwaitingConfirmation);
			second.PendingIntent.Should().Be(ChatbotIntentType.DispatchCall);
			second.Context["description"].Should().Be("Structure fire");
		}

		[Test]
		public async Task SessionStore_PruneWithoutCutoff_UsesEachSessionTtl()
		{
			// Arrange
			ChatbotConfig.UseRedisSessionStore = false;
			var store = new RedisSessionStore(Mock.Of<ICacheProvider>());
			var shortSession = await store.GetOrCreateAsync("session-prune-short-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 5);
			var longSession = await store.GetOrCreateAsync("session-prune-long-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);
			shortSession.LastActivity = DateTime.UtcNow.AddMinutes(-10);
			longSession.LastActivity = DateTime.UtcNow.AddMinutes(-10);

			// Act
			await store.PruneExpiredAsync();
			var recreatedShortSession = await store.GetOrCreateAsync("session-prune-short-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);
			var retainedLongSession = await store.GetOrCreateAsync("session-prune-long-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);

			// Assert
			recreatedShortSession.SessionId.Should().NotBe(shortSession.SessionId);
			retainedLongSession.SessionId.Should().Be(longSession.SessionId);
		}

		[Test]
		public async Task SessionStore_PruneWithCutoff_UsesExplicitCutoff()
		{
			// Arrange
			ChatbotConfig.UseRedisSessionStore = false;
			var store = new RedisSessionStore(Mock.Of<ICacheProvider>());
			var olderSession = await store.GetOrCreateAsync("session-prune-cutoff-old-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);
			var newerSession = await store.GetOrCreateAsync("session-prune-cutoff-new-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);
			olderSession.LastActivity = DateTime.UtcNow.AddMinutes(-10);
			newerSession.LastActivity = DateTime.UtcNow.AddMinutes(-2);

			// Act
			await store.PruneExpiredAsync(DateTime.UtcNow.AddMinutes(-5));
			var recreatedOlderSession = await store.GetOrCreateAsync("session-prune-cutoff-old-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);
			var retainedNewerSession = await store.GetOrCreateAsync("session-prune-cutoff-new-user-001", 1,
				ChatbotPlatform.SmsTwilio, "", 60);

			// Assert
			recreatedOlderSession.SessionId.Should().NotBe(olderSession.SessionId);
			retainedNewerSession.SessionId.Should().Be(newerSession.SessionId);
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
