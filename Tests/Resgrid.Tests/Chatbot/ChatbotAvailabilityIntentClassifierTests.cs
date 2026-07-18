using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Chatbot.NLU.Providers;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Keyword-classifier routing tests for the availability/call-responder/poll/schedule intents,
	/// including ordering regressions (the new "who ..." patterns must not swallow personnel lookups
	/// and the "what ... calls" patterns must not swallow the call list).
	/// </summary>
	[TestFixture]
	public class ChatbotAvailabilityIntentClassifierTests
	{
		private KeywordIntentClassifier _classifier;

		[SetUp]
		public void Setup()
		{
			_classifier = new KeywordIntentClassifier();
		}

		[TestCase("my status?", "my_status")]
		[TestCase("my staffing?", "my_status")]
		[TestCase("who's around?", "who_available")]
		[TestCase("Who's available?", "who_available")]
		[TestCase("who is available to respond", "who_available")]
		[TestCase("anyone free", "who_available")]
		[TestCase("who can respond?", "who_available")]
		[TestCase("Units available?", "units_available")]
		[TestCase("available units", "units_available")]
		[TestCase("what units are available?", "units_available")]
		[TestCase("What calls am I on?", "my_calls")]
		[TestCase("my calls", "my_calls")]
		[TestCase("Whats my schedule?", "my_schedule")]
		[TestCase("my unread messages?", "list_messages")]
		[TestCase("new messages", "list_messages")]
		public async Task Classifies_intent(string text, string expectedIntent)
		{
			var result = await _classifier.ClassifyAsync(text);
			result.IntentName.Should().Be(expectedIntent, because: $"\"{text}\" should classify as {expectedIntent}");
		}

		[Test]
		public async Task WhoIsOnCall_extracts_call_reference()
		{
			var result = await _classifier.ClassifyAsync("Who's on call 26-1?");
			result.IntentName.Should().Be("call_responders");
			result.Parameters["mode"].Should().Be("all");
			result.Parameters["callRef"].Should().Be("26-1");
		}

		[Test]
		public async Task WhoIsEnRoute_extracts_mode_and_reference()
		{
			var result = await _classifier.ClassifyAsync("Who's in route to the fire?");
			result.IntentName.Should().Be("call_responders");
			result.Parameters["mode"].Should().Be("enroute");
			result.Parameters["callRef"].Should().Be("the fire");
		}

		[Test]
		public async Task WhoIsOnScene_extracts_mode()
		{
			var result = await _classifier.ClassifyAsync("who's on scene at the fire");
			result.IntentName.Should().Be("call_responders");
			result.Parameters["mode"].Should().Be("onscene");
			result.Parameters["callRef"].Should().Be("the fire");
		}

		[Test]
		public async Task WhoGotDispatched_extracts_reference()
		{
			var result = await _classifier.ClassifyAsync("Who got dispatched to the medical?");
			result.IntentName.Should().Be("call_dispatched");
			result.Parameters["callRef"].Should().Be("the medical");
		}

		[Test]
		public async Task UnitCalls_extracts_unit_name()
		{
			var result = await _classifier.ClassifyAsync("What calls is Rescue 6 on?");
			result.IntentName.Should().Be("unit_calls");
			result.Parameters["unitName"].Should().Be("Rescue 6");
		}

		[Test]
		public async Task Poll_extracts_question()
		{
			var result = await _classifier.ClassifyAsync("Poll members to see who's available for a red flag on 7/22");
			result.IntentName.Should().Be("create_poll");
			result.Parameters["question"].Should().Contain("available for a red flag on 7/22");
		}

		[Test]
		public async Task MySchedule_extracts_day()
		{
			var result = await _classifier.ClassifyAsync("my schedule for 7/22");
			result.IntentName.Should().Be("my_schedule");
			result.Parameters["day"].Should().Be("7/22");
		}

		// Ordering regressions: the new patterns must not swallow existing intents.

		[Test]
		public async Task WhoIsName_still_personnel_lookup()
		{
			var result = await _classifier.ClassifyAsync("who is john");
			result.IntentName.Should().Be("personnel_lookup");
			result.Parameters["query"].Should().Be("john");
		}

		[TestCase("responding", "respond_to_call")]
		[TestCase("on my way", "respond_to_call")]
		[TestCase("omw", "respond_to_call")]
		[TestCase("not responding", "respond_to_call")]
		[TestCase("not going", "respond_to_call")]
		[TestCase("available", "set_staffing")]
		[TestCase("units", "list_units")]
		[TestCase("show active calls", "list_calls")]
		[TestCase("messages", "list_messages")]
		public async Task Existing_intents_unchanged(string text, string expectedIntent)
		{
			var result = await _classifier.ClassifyAsync(text);
			result.IntentName.Should().Be(expectedIntent);
		}

		[TestCase("responding", "yes")]
		[TestCase("omw", "yes")]
		[TestCase("not responding", "no")]
		[TestCase("not going", "no")]
		public async Task Bare_call_response_extracts_response(string text, string expectedResponse)
		{
			var result = await _classifier.ClassifyAsync(text);

			result.IntentName.Should().Be("respond_to_call");
			result.Parameters["response"].Should().Be(expectedResponse);
		}

		[TestCase("omw to 26-1?", "26-1", "yes")]
		[TestCase("not responding to the fire!", "the fire", "no")]
		public async Task Referenced_call_response_cleans_call_reference(string text, string expectedReference, string expectedResponse)
		{
			var result = await _classifier.ClassifyAsync(text);

			result.IntentName.Should().Be("respond_to_call");
			result.Parameters["callRef"].Should().Be(expectedReference);
			result.Parameters["response"].Should().Be(expectedResponse);
		}
	}
}
