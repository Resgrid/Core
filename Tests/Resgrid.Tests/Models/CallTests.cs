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

		[Test]
		public void GetDisplayName_PrefixesTheNumberOntoTheName()
		{
			var call = new Call { Number = "26-45", Name = "Structure Fire" };

			call.GetDisplayName().Should().Be("26-45 Structure Fire");
		}

		[Test]
		public void GetDisplayName_ReturnsTheNameAlone_WhenThereIsNoNumber()
		{
			var call = new Call { Name = "Structure Fire" };

			call.GetDisplayName().Should().Be("Structure Fire");
		}

		[Test]
		public void GetDisplayName_ReturnsTheNumberAlone_WhenThereIsNoName()
		{
			var call = new Call { Number = "26-45" };

			call.GetDisplayName().Should().Be("26-45");
		}

		[Test]
		public void GetDisplayName_DoesNotDoubleUpWhenTheNameAlreadyLeadsWithTheNumber()
		{
			var call = new Call { Number = "26-45", Name = "26-45 Structure Fire" };

			call.GetDisplayName().Should().Be("26-45 Structure Fire");
		}

		[Test]
		public void GetDisplayName_IsEmpty_WhenTheCallHasNeither()
		{
			new Call().GetDisplayName().Should().BeEmpty();
		}

		[Test]
		public void GetDisplayName_FallsBackToTheNumber_WhenTheNameIsStillEnveloped()
		{
			// Every caller of this helper names a chat channel, and that name is persisted and shown
			// to the whole department. Calls.Name is cataloged; Calls.Number deliberately is not.
			var call = new Call { Number = "26-45", Name = "rgdp:1:2:c3RydWN0dXJlLWZpcmU=" };

			call.GetDisplayName().Should().Be("26-45");
		}

		[Test]
		public void GetDisplayName_FallsBackToTheNumber_WhenTheNameIsRedacted()
		{
			// A durable label reading "26-45 REDACTED" would be worse than one reading "26-45".
			var call = new Call { Number = "26-45", Name = ProtectedDataEnvelope.RedactionValue };

			call.GetDisplayName().Should().Be("26-45");
		}

		[Test]
		public void GetDisplayName_IsEmpty_WhenAProtectedCallHasNoNumber()
		{
			new Call { Name = "rgdp:1:2:c3RydWN0dXJlLWZpcmU=" }.GetDisplayName().Should().BeEmpty();
		}
	}
}
