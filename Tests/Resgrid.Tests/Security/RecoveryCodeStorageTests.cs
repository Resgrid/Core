using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Stores;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	[TestFixture, NonParallelizable]
	public class RecoveryCodeStorageTests
	{
		private const string UserId = "7C2B7E55-1A7E-4A7E-9C63-3B2A5A1D0F42";
		private string _key, _salt;
		private InMemoryUserMfaStateRepository _state;
		private IdentityUserStore _store;
		private IdentityUser _user;

		[SetUp]
		public void SetUp()
		{
			_key = SecurityConfig.EncryptionKey;
			_salt = SecurityConfig.EncryptionSaltValue;
			SecurityConfig.EncryptionKey = "unit-test-master-key-0123456789ab";
			SecurityConfig.EncryptionSaltValue = "unit-test-salt";

			_state = new InMemoryUserMfaStateRepository();
			var users = new Mock<IIdentityUserRepository>();
			// The legacy plaintext token is read through the identity repository, as IdentityUserStore does in production.
			users.Setup(r => r.GetTokenAsync(It.IsAny<string>(), "[AspNetUserStore]", "RecoveryCodes"))
				.ReturnsAsync((string userId, string _, string _) => _state.LegacyTokens.TryGetValue(userId, out var value) ? value : null);
			_store = new IdentityUserStore(Mock.Of<IConnectionProvider>(), users.Object, Mock.Of<IUnitOfWork>(), _state);
			_user = new IdentityUser { Id = UserId };
		}

		[TearDown]
		public void TearDown()
		{
			SecurityConfig.EncryptionKey = _key;
			SecurityConfig.EncryptionSaltValue = _salt;
		}

		[Test]
		public void Hashes_are_deterministic_normalised_and_bound_to_the_user()
		{
			var hash = RecoveryCodeHasher.Hash(UserId, "ABCDE-FGHIJ");

			hash.Should().HaveCount(32);
			RecoveryCodeHasher.Hash(UserId, " abcde-fghij ").Should().Equal(hash);
			RecoveryCodeHasher.Hash(UserId, "ABCDEFGHIJ").Should().Equal(hash);
			RecoveryCodeHasher.Hash("another-user", "ABCDE-FGHIJ").Should().NotEqual(hash);
			RecoveryCodeHasher.Hash(UserId, "ABCDE-FGHIK").Should().NotEqual(hash);
		}

		[Test]
		public void Hashes_depend_on_the_master_key_and_fail_closed_without_it()
		{
			var hash = RecoveryCodeHasher.Hash(UserId, "ABCDE-FGHIJ");

			SecurityConfig.EncryptionKey = "a-different-master-key-0123456789";
			RecoveryCodeHasher.Hash(UserId, "ABCDE-FGHIJ").Should().NotEqual(hash);

			SecurityConfig.EncryptionKey = "";
			FluentActions.Invoking(() => RecoveryCodeHasher.Hash(UserId, "ABCDE-FGHIJ")).Should().Throw<InvalidOperationException>();
		}

		[Test]
		public async Task New_codes_are_stored_as_hashes_and_each_redeems_once()
		{
			await _store.ReplaceCodesAsync(_user, new[] { "AAAAA-11111", "BBBBB-22222" }, CancellationToken.None);

			_state.Codes.Should().HaveCount(2);
			_state.Codes.Select(c => c.Hash.Length).Should().AllBeEquivalentTo(32);
			(await _store.CountCodesAsync(_user, CancellationToken.None)).Should().Be(2);

			(await _store.RedeemCodeAsync(_user, "aaaaa-11111", CancellationToken.None)).Should().BeTrue();
			(await _store.RedeemCodeAsync(_user, "AAAAA-11111", CancellationToken.None)).Should().BeFalse("a code is single-use");
			(await _store.RedeemCodeAsync(_user, "CCCCC-33333", CancellationToken.None)).Should().BeFalse();
			(await _store.CountCodesAsync(_user, CancellationToken.None)).Should().Be(1);
		}

		[Test]
		public async Task Regeneration_invalidates_every_previous_code()
		{
			await _store.ReplaceCodesAsync(_user, new[] { "AAAAA-11111" }, CancellationToken.None);
			await _store.ReplaceCodesAsync(_user, new[] { "DDDDD-44444" }, CancellationToken.None);

			(await _store.RedeemCodeAsync(_user, "AAAAA-11111", CancellationToken.None)).Should().BeFalse();
			(await _store.RedeemCodeAsync(_user, "DDDDD-44444", CancellationToken.None)).Should().BeTrue();
		}

		[Test]
		public async Task Replacing_with_no_codes_leaves_none()
		{
			await _store.ReplaceCodesAsync(_user, new[] { "AAAAA-11111" }, CancellationToken.None);
			await _store.ReplaceCodesAsync(_user, Array.Empty<string>(), CancellationToken.None);

			(await _store.CountCodesAsync(_user, CancellationToken.None)).Should().Be(0);
		}

		[Test]
		public async Task Legacy_plaintext_codes_migrate_on_first_use_and_stay_single_use()
		{
			_state.LegacyTokens[UserId] = "LEGAC-00001;LEGAC-00002;LEGAC-00003";

			(await _store.CountCodesAsync(_user, CancellationToken.None)).Should().Be(3);
			_state.LegacyTokens.Should().NotContainKey(UserId, "the plaintext token is deleted once migrated");

			(await _store.RedeemCodeAsync(_user, "LEGAC-00002", CancellationToken.None)).Should().BeTrue();
			(await _store.RedeemCodeAsync(_user, "LEGAC-00002", CancellationToken.None)).Should().BeFalse();
			(await _store.CountCodesAsync(_user, CancellationToken.None)).Should().Be(2);
		}

		[Test]
		public async Task A_legacy_token_with_every_code_spent_migrates_to_none()
		{
			_state.LegacyTokens[UserId] = "";

			(await _store.CountCodesAsync(_user, CancellationToken.None)).Should().Be(0);
			(await _store.RedeemCodeAsync(_user, "LEGAC-00001", CancellationToken.None)).Should().BeFalse();
			_state.LegacyTokens.Should().NotContainKey(UserId);
		}

		[Test]
		public async Task Blank_codes_are_rejected_without_touching_storage()
		{
			(await _store.RedeemCodeAsync(_user, "  ", CancellationToken.None)).Should().BeFalse();
			(await _store.RedeemCodeAsync(_user, null, CancellationToken.None)).Should().BeFalse();
		}
	}
}
