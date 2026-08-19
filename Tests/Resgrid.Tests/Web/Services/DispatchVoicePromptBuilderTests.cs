using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class DispatchVoicePromptBuilderTests
	{
		private static Call CreateCall(int dispatchCount = 0)
		{
			return new Call
			{
				Name = "Call 42",
				Priority = (int)CallPriority.High,
				NatureOfCall = "Structure fire",
				DispatchCount = dispatchCount
			};
		}

		[Test]
		public void should_announce_new_call_with_nature_then_address_then_priority()
		{
			DispatchVoicePromptBuilder.BuildDispatchPrompt(CreateCall(), "123 Main St")
				.Should().Be("New call, Call 42. Nature, Structure fire. Address, 123 Main St. Priority, High.");
		}

		[Test]
		public void should_omit_the_place_sentence_when_no_address_is_available()
		{
			DispatchVoicePromptBuilder.BuildDispatchPrompt(CreateCall(), null)
				.Should().Be("New call, Call 42. Nature, Structure fire. Priority, High.");
		}

		[TestCase(0, "New call")]
		[TestCase(1, "New call")]
		[TestCase(2, "Second alarm")]
		[TestCase(3, "Third alarm")]
		[TestCase(4, "Fourth alarm")]
		[TestCase(9, "Ninth alarm")]
		[TestCase(12, "Alarm 12")]
		public void should_announce_the_alarm_level_from_the_dispatch_count(int dispatchCount, string expectedIntro)
		{
			DispatchVoicePromptBuilder.BuildDispatchPrompt(CreateCall(dispatchCount), "123 Main St")
				.Should().StartWith($"{expectedIntro}, Call 42.");
		}

		[Test]
		public void should_speak_location_instead_of_address_for_freeform_places()
		{
			DispatchVoicePromptBuilder.BuildDispatchPrompt(CreateCall(), "At the bottom of Bucks Canyon")
				.Should().Contain("Location, At the bottom of Bucks Canyon.");
		}

		[Test]
		public void should_strip_html_from_the_nature_of_call()
		{
			var call = CreateCall();
			call.NatureOfCall = "<div>Structure fire</div>";

			DispatchVoicePromptBuilder.BuildDispatchPrompt(call, null)
				.Should().Contain("Nature, Structure fire.");
		}

		[TestCase("123 Main St, Springfield, WA 98111, USA", "123 Main St, Springfield")]
		[TestCase("123 Main St, Springfield, WA, USA", "123 Main St, Springfield")]
		[TestCase("123 Main St, Springfield, Washington 98111", "123 Main St, Springfield")]
		[TestCase("123 Main St, Springfield, WA", "123 Main St, Springfield")]
		[TestCase("450 Elk Run Rd, Victor, Idaho, United States", "450 Elk Run Rd, Victor")]
		[TestCase("1 Front St, Toronto, M5J 2X5, Canada", "1 Front St, Toronto")]
		public void trim_address_should_drop_trailing_state_zip_and_country(string input, string expected)
		{
			DispatchVoicePromptBuilder.TrimAddressForSpeech(input).Should().Be(expected);
		}

		[TestCase("Building 5, Floor 3, Room 10")]
		[TestCase("At the bottom of Bucks Canyon")]
		[TestCase("123 Main St, Springfield")]
		[TestCase("Main St and 5th Ave")]
		public void trim_address_should_leave_freeform_locations_untouched(string input)
		{
			DispatchVoicePromptBuilder.TrimAddressForSpeech(input).Should().Be(input);
		}

		[Test]
		public void trim_address_should_always_keep_at_least_one_segment()
		{
			DispatchVoicePromptBuilder.TrimAddressForSpeech("WA 98111").Should().Be("WA 98111");
		}
	}
}
