using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class LocalDevKeyWrappingProviderTests
	{
		private LocalDevKeyWrappingProvider _provider;

		[SetUp]
		public void SetUp()
		{
			_provider = new LocalDevKeyWrappingProvider();
		}

		[Test]
		public async Task Wrap_then_unwrap_round_trips_a_256_bit_key()
		{
			var wrapped = await _provider.GenerateWrappedDataKeyAsync(42);

			wrapped.ProviderType.Should().Be("LocalDev");
			wrapped.WrappedKeyBase64.Should().NotBeNullOrEmpty();

			var dek = await _provider.UnwrapDataKeyAsync(42, wrapped.WrappedKeyBase64);
			dek.Should().HaveCount(32);
		}

		[Test]
		public async Task Each_generated_key_is_distinct()
		{
			var first = await _provider.GenerateWrappedDataKeyAsync(42);
			var second = await _provider.GenerateWrappedDataKeyAsync(42);

			first.WrappedKeyBase64.Should().NotBe(second.WrappedKeyBase64);
		}

		[Test]
		public async Task Unwrap_under_another_department_fails_cryptographically()
		{
			// Even the dev provider enforces department binding as AAD, mirroring the OpenBao
			// derived-context failure mode the acceptance tests require.
			var wrapped = await _provider.GenerateWrappedDataKeyAsync(42);

			var act = async () => await _provider.UnwrapDataKeyAsync(43, wrapped.WrappedKeyBase64);
			await act.Should().ThrowAsync<CryptographicException>();
		}

		[Test]
		public async Task Malformed_blob_is_rejected()
		{
			var act = async () => await _provider.UnwrapDataKeyAsync(42, Convert.ToBase64String(new byte[10]));
			await act.Should().ThrowAsync<CryptographicException>();
		}
	}
}
