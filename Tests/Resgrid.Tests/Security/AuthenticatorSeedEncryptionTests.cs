using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Security;
using Resgrid.Repositories.DataRepository.Stores;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 14: TOTP seeds at rest. Seeds are written encrypted once the gate is on, every build
	/// reads both forms, older seeds are re-encrypted as they are read, keys rotate by id, and nothing moves between accounts
	/// or between the active and staged seed.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AuthenticatorSeedEncryptionTests
	{
		private const string UserId = "7C2B7E55-1A7E-4A7E-9C63-3B2A5A1D0F42";
		private const string OtherUserId = "11111111-2222-3333-4444-555555555555";
		private const string Seed = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

		private string _key, _salt, _ring, _active;
		private bool _gate;
		private Dictionary<(string UserId, string Provider, string Name), string> _tokens;
		private int _replaces;
		private Mock<IIdentityUserRepository> _users;
		private InMemoryUserMfaStateRepository _state;
		private IdentityUserStore _store;
		private IdentityUser _user;

		[SetUp]
		public void SetUp()
		{
			(_key, _salt, _ring, _active, _gate) = (SecurityConfig.EncryptionKey, SecurityConfig.EncryptionSaltValue, TwoFactorConfig.AuthenticatorSeedKeyRing,
				TwoFactorConfig.AuthenticatorSeedActiveKeyId, TwoFactorConfig.AuthenticatorSeedEncryptionEnabled);
			SecurityConfig.EncryptionKey = "unit-test-master-key-0123456789ab";
			SecurityConfig.EncryptionSaltValue = "unit-test-salt";
			TwoFactorConfig.AuthenticatorSeedKeyRing = "";
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = "";
			TwoFactorConfig.AuthenticatorSeedEncryptionEnabled = true;

			_tokens = new Dictionary<(string, string, string), string>();
			_replaces = 0;
			_users = new Mock<IIdentityUserRepository>();
			_users.Setup(r => r.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((string user, string provider, string name) => _tokens.GetValueOrDefault((user, provider, name)));
			_users.Setup(r => r.SetTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string user, string provider, string name, string value, CancellationToken _) => _tokens[(user, provider, name)] = value)
				.Returns(Task.CompletedTask);
			_users.Setup(r => r.TryReplaceTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((string user, string provider, string name, string expected, string value, CancellationToken _) =>
				{
					if (!_tokens.TryGetValue((user, provider, name), out var current) || !string.Equals(current, expected, StringComparison.Ordinal))
						return false;
					_tokens[(user, provider, name)] = value;
					_replaces++;
					return true;
				});
			_users.Setup(r => r.GetTokensPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string provider, string name, string after, int take, CancellationToken _) => _tokens
					.Where(t => t.Key.Provider == provider && t.Key.Name == name && (after == null || string.CompareOrdinal(t.Key.UserId, after) > 0))
					.OrderBy(t => t.Key.UserId, StringComparer.Ordinal).Take(take)
					.Select(t => new UserTokenValue { UserId = t.Key.UserId, Value = t.Value }).ToList());
			_state = new InMemoryUserMfaStateRepository();
			_store = new IdentityUserStore(Mock.Of<IConnectionProvider>(), _users.Object, Mock.Of<IUnitOfWork>(), _state);
			_user = new IdentityUser { Id = UserId };
		}

		[TearDown]
		public void TearDown()
		{
			SecurityConfig.EncryptionKey = _key;
			SecurityConfig.EncryptionSaltValue = _salt;
			TwoFactorConfig.AuthenticatorSeedKeyRing = _ring;
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = _active;
			TwoFactorConfig.AuthenticatorSeedEncryptionEnabled = _gate;
		}

		private string Stored(string userId = UserId) => _tokens.GetValueOrDefault((userId, AuthenticatorSeedProtector.ActiveLoginProvider, AuthenticatorSeedProtector.ActiveTokenName));

		private void Store(string value, string userId = UserId) =>
			_tokens[(userId, AuthenticatorSeedProtector.ActiveLoginProvider, AuthenticatorSeedProtector.ActiveTokenName)] = value;

		private static string RandomKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

		// ---- The protector ------------------------------------------------------------------------------------------------

		[Test]
		public void A_seed_is_encrypted_under_the_active_key_and_bound_to_its_account_and_use()
		{
			var stored = AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed);
			stored.Should().StartWith("tseed1:m1:").And.NotContain(Seed);
			AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed).Should().NotBe(stored, "every write has its own nonce");

			var read = AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, stored);
			read.Value.Should().Be(Seed);
			read.IsPlaintext.Should().BeFalse();
			read.NeedsRewrap.Should().BeFalse();
			AuthenticatorSeedProtector.Unprotect(UserId.ToLowerInvariant(), AuthenticatorSeedUse.Active, stored).Value.Should().Be(Seed,
				"user ids compare without case, as the database does");

			AuthenticatorSeedProtector.Unprotect(OtherUserId, AuthenticatorSeedUse.Active, stored).Failed.Should().BeTrue("copied to another account");
			AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Staged, stored).Failed.Should().BeTrue("moved from active to staged");

			var bytes = Convert.FromBase64String(stored.Substring("tseed1:m1:".Length));
			bytes[^1] ^= 1;
			AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, "tseed1:m1:" + Convert.ToBase64String(bytes)).Failed.Should().BeTrue("tampered");
			AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, "tseed1:m1:not base64!").Failed.Should().BeTrue();
			AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, "tseed1:nokey").Failed.Should().BeTrue();

			var legacy = AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, Seed);
			legacy.IsPlaintext.Should().BeTrue();
			legacy.Value.Should().Be(Seed);
		}

		[Test]
		public void Keys_rotate_by_id_and_a_seed_under_a_removed_key_reads_as_unreadable()
		{
			var k1 = RandomKey();
			TwoFactorConfig.AuthenticatorSeedKeyRing = "k1=" + k1;
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = "k1";
			var underK1 = AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed);
			underK1.Should().StartWith("tseed1:k1:");
			var underM1 = AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active,
				ProtectWith("", "", UserId));
			underM1.Value.Should().Be(Seed, "the master-derived key stays readable after a ring is configured");
			underM1.NeedsRewrap.Should().BeTrue();

			TwoFactorConfig.AuthenticatorSeedKeyRing = "k1=" + k1 + "; k2=" + RandomKey();
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = "k2";
			var read = AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, underK1);
			read.Value.Should().Be(Seed);
			read.NeedsRewrap.Should().BeTrue("k1 is no longer the active key");

			TwoFactorConfig.AuthenticatorSeedKeyRing = "k2=" + RandomKey();
			AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, underK1).Failed.Should().BeTrue();
		}

		private static string ProtectWith(string ring, string active, string userId)
		{
			var (savedRing, savedActive) = (TwoFactorConfig.AuthenticatorSeedKeyRing, TwoFactorConfig.AuthenticatorSeedActiveKeyId);
			TwoFactorConfig.AuthenticatorSeedKeyRing = ring;
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = active;
			try { return AuthenticatorSeedProtector.Protect(userId, AuthenticatorSeedUse.Active, Seed); }
			finally { (TwoFactorConfig.AuthenticatorSeedKeyRing, TwoFactorConfig.AuthenticatorSeedActiveKeyId) = (savedRing, savedActive); }
		}

		[Test]
		public void An_unusable_key_configuration_is_reported_and_refuses_to_write()
		{
			AuthenticatorSeedProtector.Readiness().Should().BeEmpty();

			foreach (var (ring, active) in new[]
			{
				("k1=" + RandomKey(), "k9"), ("k1=not-base64", "k1"), ("k1=" + Convert.ToBase64String(new byte[16]), "k1"), ("=abc", ""),
				("k1=" + RandomKey() + ";k1=" + RandomKey(), "k1"), ("", "bad id!")
			})
			{
				TwoFactorConfig.AuthenticatorSeedKeyRing = ring;
				TwoFactorConfig.AuthenticatorSeedActiveKeyId = active;
				AuthenticatorSeedProtector.Readiness().Should().NotBeEmpty($"ring '{ring}', active '{active}'");
			}

			TwoFactorConfig.AuthenticatorSeedKeyRing = "k1=" + RandomKey();
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = "k9";
			FluentActions.Invoking(() => AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed))
				.Should().Throw<InvalidOperationException>("never written unencrypted by mistake");

			TwoFactorConfig.AuthenticatorSeedKeyRing = "";
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = "";
			var stored = AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed);
			SecurityConfig.EncryptionKey = "";
			AuthenticatorSeedProtector.Readiness().Should().ContainSingle();
			AuthenticatorSeedProtector.Unprotect(UserId, AuthenticatorSeedUse.Active, stored).Failed.Should().BeTrue("without the master key there is no m1");
		}

		// ---- The store ----------------------------------------------------------------------------------------------------

		[Test]
		public async Task With_the_gate_off_seeds_are_written_as_before_and_both_forms_still_read()
		{
			TwoFactorConfig.AuthenticatorSeedEncryptionEnabled = false;
			await _store.SetAuthenticatorKeyAsync(_user, Seed, CancellationToken.None);
			Stored().Should().Be(Seed, "an older build can still read what this one writes");
			(await _store.GetAuthenticatorKeyAsync(_user, CancellationToken.None)).Should().Be(Seed);
			_replaces.Should().Be(0, "nothing is re-encrypted while the gate is off");

			Store(AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed));
			(await _store.GetAuthenticatorKeyAsync(_user, CancellationToken.None)).Should().Be(Seed, "turning the gate off never loses encrypted seeds");
			_replaces.Should().Be(0);
		}

		[Test]
		public async Task With_the_gate_on_seeds_are_written_encrypted_and_older_ones_re_encrypted_once_on_read()
		{
			await _store.SetAuthenticatorKeyAsync(_user, Seed, CancellationToken.None);
			Stored().Should().StartWith("tseed1:").And.NotContain(Seed);
			(await _store.GetAuthenticatorKeyAsync(_user, CancellationToken.None)).Should().Be(Seed);
			_replaces.Should().Be(0);

			Store(Seed, OtherUserId);
			var other = new IdentityUser { Id = OtherUserId };
			(await _store.GetAuthenticatorKeyAsync(other, CancellationToken.None)).Should().Be(Seed);
			Stored(OtherUserId).Should().StartWith("tseed1:");
			(await _store.GetAuthenticatorKeyAsync(other, CancellationToken.None)).Should().Be(Seed);
			_replaces.Should().Be(1, "re-encrypted once, then read as it is");
		}

		[Test]
		public async Task A_re_encryption_never_overwrites_a_replacement_written_meanwhile()
		{
			Store(Seed);
			_users.Setup(r => r.GetTokenAsync(UserId, AuthenticatorSeedProtector.ActiveLoginProvider, AuthenticatorSeedProtector.ActiveTokenName))
				.ReturnsAsync(() =>
				{
					var read = Stored();
					Store("REPLACEMENTKEYREPLACEMENTKEY2345"); // another request replaced the authenticator after this read
					return read;
				});

			(await _store.GetAuthenticatorKeyAsync(_user, CancellationToken.None)).Should().Be(Seed, "this request used what it read");
			Stored().Should().Be("REPLACEMENTKEYREPLACEMENTKEY2345", "the replacement stands");
			_replaces.Should().Be(0);
		}

		[Test]
		public async Task An_unreadable_seed_reads_as_no_authenticator_so_codes_fail_closed()
		{
			Store(AuthenticatorSeedProtector.Protect(OtherUserId, AuthenticatorSeedUse.Active, Seed));

			(await _store.GetAuthenticatorKeyAsync(_user, CancellationToken.None)).Should().BeNull();
			var manager = Manager();
			(await manager.VerifyTwoFactorTokenAsync(_user, TokenOptions.DefaultAuthenticatorProvider,
				TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(Seed), TotpCalculator.CurrentTimeStep(DateTime.UtcNow)).ToString("D6")))
				.Should().BeFalse();
		}

		[Test]
		public async Task A_staged_replacement_is_encrypted_too_and_other_tokens_are_untouched()
		{
			var staged = StagedAuthenticatorKey.Serialize(Seed, DateTime.UtcNow);
			await _store.SetTokenAsync(_user, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName, staged, CancellationToken.None);
			_tokens[(UserId, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName)].Should().StartWith("tseed1:").And.NotContain(Seed);
			(await _store.GetTokenAsync(_user, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName, CancellationToken.None))
				.Should().Be(staged);

			await _store.SetTokenAsync(_user, "SomeProvider", "SomeToken", "plain", CancellationToken.None);
			_tokens[(UserId, "SomeProvider", "SomeToken")].Should().Be("plain");
		}

		[Test]
		public async Task A_code_from_an_encrypted_seed_verifies_once()
		{
			await _store.SetAuthenticatorKeyAsync(_user, Seed, CancellationToken.None);
			var manager = Manager();
			var code = TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(Seed), TotpCalculator.CurrentTimeStep(DateTime.UtcNow)).ToString("D6");

			(await manager.VerifyTwoFactorTokenAsync(_user, TokenOptions.DefaultAuthenticatorProvider, code)).Should().BeTrue();
			(await manager.VerifyTwoFactorTokenAsync(_user, TokenOptions.DefaultAuthenticatorProvider, code)).Should().BeFalse("a time step is used once");
		}

		private UserManager<IdentityUser> Manager()
		{
			var options = new IdentityOptions();
			var manager = new UserManager<IdentityUser>(_store, Options.Create(options), null, null, null, null, null, null,
				NullLogger<UserManager<IdentityUser>>.Instance);
			manager.RegisterTokenProvider(TokenOptions.DefaultAuthenticatorProvider, new ResgridAuthenticatorTokenProvider(_state));
			return manager;
		}

		// ---- The migration -------------------------------------------------------------------------------------------------

		[Test]
		public async Task The_migration_encrypts_every_seed_then_decrypts_them_for_a_rollback()
		{
			for (var i = 0; i < 7; i++)
				Store(Seed, $"user-{i}");
			_tokens[("user-3", StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName)] = StagedAuthenticatorKey.Serialize(Seed, DateTime.UtcNow);
			Store(AuthenticatorSeedProtector.Protect("someone-else", AuthenticatorSeedUse.Active, Seed), "user-9");

			var encrypted = await AuthenticatorSeedMigrator.RunAsync(_users.Object, AuthenticatorSeedMigration.Encrypt, 3);
			encrypted.Scanned.Should().Be(9);
			encrypted.Rewritten.Should().Be(8);
			encrypted.Unreadable.Should().Be(1, "a value moved between accounts is left alone and reported");
			_tokens.Where(t => t.Key.UserId != "user-9").Should().OnlyContain(t => t.Value.StartsWith("tseed1:"));

			(await AuthenticatorSeedMigrator.RunAsync(_users.Object, AuthenticatorSeedMigration.Encrypt, 3)).Rewritten.Should().Be(0, "safe to run again");
			await FluentActions.Awaiting(() => AuthenticatorSeedMigrator.RunAsync(_users.Object, AuthenticatorSeedMigration.Decrypt, 3))
				.Should().ThrowAsync<InvalidOperationException>("reads would re-encrypt behind a decrypt while the gate is on");

			TwoFactorConfig.AuthenticatorSeedEncryptionEnabled = false;
			var decrypted = await AuthenticatorSeedMigrator.RunAsync(_users.Object, AuthenticatorSeedMigration.Decrypt, 3);
			decrypted.Rewritten.Should().Be(8);
			Stored("user-0").Should().Be(Seed);
			await FluentActions.Awaiting(() => AuthenticatorSeedMigrator.RunAsync(_users.Object, AuthenticatorSeedMigration.Encrypt, 3))
				.Should().ThrowAsync<InvalidOperationException>("encrypting needs every host to read encrypted seeds first");
		}

		[Test]
		public async Task A_rotation_re_encrypts_seeds_under_the_retired_key()
		{
			Store(AuthenticatorSeedProtector.Protect(UserId, AuthenticatorSeedUse.Active, Seed));
			TwoFactorConfig.AuthenticatorSeedKeyRing = "k2=" + RandomKey();
			TwoFactorConfig.AuthenticatorSeedActiveKeyId = "k2";

			(await AuthenticatorSeedMigrator.RunAsync(_users.Object, AuthenticatorSeedMigration.Encrypt, 10)).Rewritten.Should().Be(1);
			Stored().Should().StartWith("tseed1:k2:");
			(await _store.GetAuthenticatorKeyAsync(_user, CancellationToken.None)).Should().Be(Seed);
		}
	}
}
