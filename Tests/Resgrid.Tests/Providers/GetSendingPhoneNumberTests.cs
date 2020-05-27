using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.NumberProvider;
using System;

namespace Resgrid.Tests.Providers
{
	namespace GetSendingPhoneNumberTests
	{
		public class with_the_text_message_provider : TestBase
		{
			protected TextMessageProvider _textMessageProvider;

			protected with_the_text_message_provider()
			{
				_textMessageProvider = new TextMessageProvider();
			}
		}

		[TestFixture]
		public class when_trying_to_get_sending_number : with_the_text_message_provider
		{
			[Test]
			public void should_be_valid_for_empty()
			{
				if (!String.IsNullOrWhiteSpace(Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber))
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("");
					result.Should().NotBeNullOrEmpty();
					//result.Should().Be(Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber);
				}
				else
				{
					true.Should().BeTrue();
				}

			}

			[Test]
			public void should_be_valid_for_null()
			{
				if (!String.IsNullOrWhiteSpace(Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber))
				{
					var result = _textMessageProvider.GetSendingPhoneNumber(null);
					result.Should().NotBeNullOrEmpty();
					//result.Should().Be(Resgrid.Config.NumberProviderConfig.SignalWireResgridNumber);
				}
				else
				{
					true.Should().BeTrue();
				}
			}

			[Test]
			public void should_be_valid_for_cali()
			{
				if (Resgrid.Config.NumberProviderConfig.SignalWireZones.Count > 0)
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("2135556987");
					result.Should().NotBeNullOrEmpty();
					result.Should().BeOneOf(Resgrid.Config.NumberProviderConfig.SignalWireZones[1]);
				}
				else
				{
					true.Should().BeTrue();
				}

			}

			[Test]
			public void should_be_valid_for_montana()
			{
				if (Resgrid.Config.NumberProviderConfig.SignalWireZones.Count > 0)
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("4065556987");
					result.Should().NotBeNullOrEmpty();
					result.Should().BeOneOf(Resgrid.Config.NumberProviderConfig.SignalWireZones[2]);
				}
				else
				{
					true.Should().BeTrue();
				}
			}

			[Test]
			public void should_be_valid_for_illnois()
			{
				if (Resgrid.Config.NumberProviderConfig.SignalWireZones.Count > 0)
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("8475556987");
					result.Should().NotBeNullOrEmpty();
					result.Should().BeOneOf(Resgrid.Config.NumberProviderConfig.SignalWireZones[3]);
				}
				else
				{
					true.Should().BeTrue();
				}
			}

			[Test]
			public void should_be_valid_for_texas()
			{
				if (Resgrid.Config.NumberProviderConfig.SignalWireZones.Count > 0)
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("5125556987");
					result.Should().NotBeNullOrEmpty();
					result.Should().BeOneOf(Resgrid.Config.NumberProviderConfig.SignalWireZones[4]);
				}
				else
				{
					true.Should().BeTrue();
				}
			}


			[Test]
			public void should_be_valid_for_maryland()
			{
				if (Resgrid.Config.NumberProviderConfig.SignalWireZones.Count > 0)
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("4105556987");
					result.Should().NotBeNullOrEmpty();
					result.Should().BeOneOf(Resgrid.Config.NumberProviderConfig.SignalWireZones[5]);
				}
				else
				{
					true.Should().BeTrue();
				}
			}

			[Test]
			public void should_be_valid_for_virginia()
			{
				if (Resgrid.Config.NumberProviderConfig.SignalWireZones.Count > 0)
				{
					var result = _textMessageProvider.GetSendingPhoneNumber("7575556987");
					result.Should().NotBeNullOrEmpty();
					result.Should().BeOneOf(Resgrid.Config.NumberProviderConfig.SignalWireZones[6]);
				}
				else
				{
					true.Should().BeTrue();
				}
			}
		}
	}
}
