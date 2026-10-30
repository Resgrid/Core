using System.Net;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class PublicNetworkAddressTests
	{
		[TestCase("8.8.8.8")]
		[TestCase("198.17.255.255")]
		[TestCase("198.20.0.1")]
		[TestCase("2001:4860:4860::8888")]
		[TestCase("2001:200::1", Description = "first allocation after the IETF block")]
		[TestCase("2a00:1450:4001::1")]
		[TestCase("2606:4700::1111")]
		[TestCase("3ffe::1", Description = "global unicast below the documentation block")]
		[TestCase("3fff:1000::1", Description = "global unicast just past the documentation block")]
		[TestCase("64:ff9b::808:808", Description = "NAT64 of a public IPv4 address")]
		public void A_routable_public_address_is_public(string address)
		{
			PublicNetworkAddress.IsPublic(IPAddress.Parse(address)).Should().BeTrue();
		}

		[TestCase("0.0.0.0")]
		[TestCase("10.0.0.1")]
		[TestCase("127.0.0.1")]
		[TestCase("169.254.169.254")]
		[TestCase("172.16.0.1")]
		[TestCase("192.168.1.1")]
		[TestCase("192.0.0.8")]
		[TestCase("100.64.0.1")]
		[TestCase("198.18.0.1")]
		[TestCase("198.19.255.255")]
		[TestCase("224.0.0.1")]
		[TestCase("::")]
		[TestCase("::1")]
		[TestCase("fe80::1")]
		[TestCase("fc00::1")]
		[TestCase("ff02::1")]
		[TestCase("::ffff:10.0.0.1")]
		[TestCase("::a00:1", Description = "IPv4-compatible")]
		[TestCase("100::1", Description = "discard")]
		[TestCase("2001::1", Description = "Teredo")]
		[TestCase("2001:2::1", Description = "benchmarking")]
		[TestCase("2001:1ff::1", Description = "end of the IETF protocol block")]
		[TestCase("2001:db8::1", Description = "documentation")]
		[TestCase("2002:a00:1::1", Description = "6to4 of 10.0.0.1")]
		[TestCase("3fff::1", Description = "documentation")]
		[TestCase("3fff:fff::1", Description = "end of the documentation block")]
		[TestCase("4000::1", Description = "outside global unicast")]
		public void An_internal_or_reserved_address_is_not_public(string address)
		{
			PublicNetworkAddress.IsPublic(IPAddress.Parse(address)).Should().BeFalse();
		}

		[TestCase("64:ff9b::a00:1", Description = "10.0.0.1")]
		[TestCase("64:ff9b::7f00:1", Description = "127.0.0.1")]
		[TestCase("64:ff9b::a9fe:a9fe", Description = "169.254.169.254")]
		[TestCase("64:ff9b:1::a00:1", Description = "local-use NAT64, IPv4 position unknown")]
		public void A_nat64_address_that_carries_an_internal_ipv4_address_is_not_public(string address)
		{
			// A NAT64 gateway connects to the IPv4 address inside, so the IPv6 form must not get a request past the check.
			PublicNetworkAddress.IsPublic(IPAddress.Parse(address)).Should().BeFalse();
		}

		[Test]
		public void No_address_is_not_public()
		{
			PublicNetworkAddress.IsPublic(null).Should().BeFalse();
		}
	}
}
