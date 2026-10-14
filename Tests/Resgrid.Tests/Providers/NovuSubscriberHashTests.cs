using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.Messaging;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Once HMAC is enabled on the Novu environment, an inbox session is refused unless its subscriberHash matches
	/// what Novu computes itself, so these hashes have to be byte-for-byte Novu's. The expected values were made
	/// with Node's crypto exactly as Novu's createHash does it:
	/// <c>createHmac('sha256', secretKey).update(subscriberId).digest('hex')</c>.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class NovuSubscriberHashTests
	{
		private const string UserId = "5f2b6a0e-1c3d-4e5f-8a9b-0c1d2e3f4a5b";

		private string _secretKey;
		private NovuProvider _provider;

		[SetUp]
		public void SetUp()
		{
			_secretKey = ChatConfig.NovuSecretKey;
			ChatConfig.NovuSecretKey = "test-secret";
			_provider = new NovuProvider();
		}

		[TearDown]
		public void TearDown()
		{
			ChatConfig.NovuSecretKey = _secretKey;
		}

		[Test]
		public void GetUserSubscriberHash_should_match_novus_hash_of_the_user_subscriber_id()
		{
			_provider.GetUserSubscriberHash(UserId, "ABCD")
				.Should().Be("b3995c49417694a492c60267ecf12e461609a9e3d9346d8a3c7673b86246bacf");
		}

		[Test]
		public void GetICUserSubscriberHash_should_match_novus_hash_of_the_ic_subscriber_id()
		{
			_provider.GetICUserSubscriberHash(UserId, "ABCD")
				.Should().Be("cf7629d30f037fff0edad1fe94434c9630b0a68c6aa360742d1a81f18dbb07d6");
		}

		[Test]
		public void GetUnitSubscriberHash_should_match_novus_hash_of_the_unit_subscriber_id()
		{
			_provider.GetUnitSubscriberHash(15, "ABCD")
				.Should().Be("b2d272a4967b616e050dbf0314750f020fc409083ea5b9afc0a38124e878ba87");
		}

		[Test]
		public void Hashes_should_be_null_when_novu_is_not_configured()
		{
			ChatConfig.NovuSecretKey = "";

			_provider.GetUserSubscriberHash(UserId, "ABCD").Should().BeNull();
			_provider.GetICUserSubscriberHash(UserId, "ABCD").Should().BeNull();
			_provider.GetUnitSubscriberHash(15, "ABCD").Should().BeNull();
		}
	}
}
