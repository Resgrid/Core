using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class MobileCarrierTests
	{
		[Test]
		public void CarriersMapShouldPullBackCorrectValue()
		{
			var mts = Carriers.CarriersMap[MobileCarriers.MTSMobility];

			mts.Should().NotBeNullOrWhiteSpace();
			mts.Should().Be("{0}@text.mtsmobility.com");
		}

		[Test]
		public void CarriersNumberLengthShouldPullBackCorrectValue()
		{
			var rogers = Carriers.CarriersNumberLength[MobileCarriers.RogersWireless];

			rogers.Should().NotBeNull();
			rogers.Should().Be(Tuple.Create(10, "Format is ###-###-####, No country code."));
		}

		[Test]
		public void DirectSendCarriersShouldPullBackCorrectValue()
		{
			//var returnValue = Carriers.DirectSendCarriers.Contains(MobileCarriers.Att);
			var returnValue = Carriers.DirectSendCarriers.Contains(MobileCarriers.BellMobility);

			returnValue.Should().BeTrue();
		}
	}
}
