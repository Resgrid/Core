using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CallTests
	{
		[Test]
		public void DidDispatchCountChange_ReturnsFalse_ForFreshCall()
		{
			var call = new Call();

			call.DidDispatchCountChange().Should().BeFalse();
		}

		[Test]
		public void DidDispatchCountChange_ReturnsFalse_AfterFirstDispatchIncrease()
		{
			var call = new Call();

			call.IncreaseDispatchCount();

			call.PreviousDispatchCount.Should().Be(0);
			call.DispatchCount.Should().Be(1);
			call.DidDispatchCountChange().Should().BeFalse();
		}

		[Test]
		public void DidDispatchCountChange_ReturnsTrue_AfterEscalationIncrease()
		{
			var call = new Call { DispatchCount = 1 };

			call.IncreaseDispatchCount();

			call.PreviousDispatchCount.Should().Be(1);
			call.DispatchCount.Should().Be(2);
			call.DidDispatchCountChange().Should().BeTrue();
		}

		[Test]
		public void DidDispatchCountChange_ReturnsFalse_WhenCountUnchanged()
		{
			var call = new Call { PreviousDispatchCount = 2, DispatchCount = 2 };

			call.DidDispatchCountChange().Should().BeFalse();
		}
	}
}
