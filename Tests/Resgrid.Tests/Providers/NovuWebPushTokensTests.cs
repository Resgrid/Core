using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Messaging;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// A web channel is written with Novu's replacing PUT, so the list sent has to carry every browser that
	/// should keep receiving, and a list that could not be read must never be guessed at.
	/// </summary>
	[TestFixture]
	public class NovuWebPushTokensTests
	{
		[Test]
		public void Add_should_keep_existing_browsers_and_put_the_new_token_last()
		{
			NovuWebPushTokens.Add(new[] { "a", "b" }, "c", 10).Should().Equal("a", "b", "c");
		}

		[Test]
		public void Add_should_move_a_reregistered_token_to_the_end_without_duplicating_it()
		{
			NovuWebPushTokens.Add(new[] { "a", "b", "c" }, "a", 10).Should().Equal("b", "c", "a");
		}

		[Test]
		public void Add_should_drop_the_oldest_tokens_past_the_cap()
		{
			NovuWebPushTokens.Add(new[] { "a", "b", "c" }, "d", 3).Should().Equal("b", "c", "d");
		}

		[Test]
		public void Add_should_keep_the_new_token_even_with_a_nonsense_cap()
		{
			NovuWebPushTokens.Add(new[] { "a" }, "b", 0).Should().Equal("b");
		}

		[Test]
		public void Add_should_ignore_blank_and_duplicate_entries_already_on_the_channel()
		{
			NovuWebPushTokens.Add(new[] { "a", "", "a", " " }, "b", 10).Should().Equal("a", "b");
		}

		[Test]
		public void Remove_should_leave_every_other_browser()
		{
			NovuWebPushTokens.Remove(new[] { "a", "b", "c" }, "b").Should().Equal("a", "c");
		}

		[Test]
		public void Remove_should_allow_an_empty_channel()
		{
			NovuWebPushTokens.Remove(new[] { "a" }, "a").Should().BeEmpty();
		}

		[Test]
		public void FindIntegrationIds_should_collect_every_environment_carrying_the_identifier()
		{
			const string json = @"{ ""data"": [
				{ ""_id"": ""int-dev"", ""identifier"": ""resgrid-web-fcm"", ""providerId"": ""fcm"" },
				{ ""_id"": ""int-native"", ""identifier"": ""respond-firebase-cloud-messaging"", ""providerId"": ""fcm"" },
				{ ""_id"": ""int-prod"", ""identifier"": ""resgrid-web-fcm"", ""providerId"": ""fcm"" } ] }";

			NovuWebPushTokens.FindIntegrationIds(json, "resgrid-web-fcm").Should().BeEquivalentTo(new[] { "int-dev", "int-prod" });
		}

		[Test]
		public void FindIntegrationIds_should_read_a_bare_array()
		{
			NovuWebPushTokens.FindIntegrationIds(@"[{ ""_id"": ""x"", ""identifier"": ""web"" }]", "web").Should().BeEquivalentTo(new[] { "x" });
		}

		[TestCase("")]
		[TestCase("<html>gateway error</html>")]
		[TestCase(@"{ ""data"": { ""unexpected"": true } }")]
		public void FindIntegrationIds_should_report_an_unreadable_body(string body)
		{
			NovuWebPushTokens.FindIntegrationIds(body, "web").Should().BeNull();
		}

		[Test]
		public void FindChannelTokens_should_read_only_the_web_channel()
		{
			const string json = @"{ ""data"": { ""subscriberId"": ""DEPT_User_1"", ""channels"": [
				{ ""_integrationId"": ""int-native"", ""providerId"": ""fcm"", ""credentials"": { ""deviceTokens"": [ ""phone"" ] } },
				{ ""_integrationId"": ""int-apns"", ""providerId"": ""apns"", ""credentials"": { ""deviceTokens"": [ ""iphone"" ] } },
				{ ""_integrationId"": ""int-prod"", ""providerId"": ""fcm"", ""credentials"": { ""deviceTokens"": [ ""chrome"", ""edge"" ] } } ] } }";

			NovuWebPushTokens.FindChannelTokens(json, new HashSet<string> { "int-dev", "int-prod" }).Should().Equal("chrome", "edge");
		}

		[Test]
		public void FindChannelTokens_should_treat_a_missing_channel_as_empty()
		{
			const string json = @"{ ""data"": { ""channels"": [ { ""_integrationId"": ""int-native"", ""credentials"": { ""deviceTokens"": [ ""phone"" ] } } ] } }";

			NovuWebPushTokens.FindChannelTokens(json, new HashSet<string> { "int-prod" }).Should().BeEmpty();
		}

		[Test]
		public void FindChannelTokens_should_treat_a_subscriber_without_channels_as_empty()
		{
			NovuWebPushTokens.FindChannelTokens(@"{ ""data"": { ""subscriberId"": ""DEPT_User_1"" } }", new HashSet<string> { "int-prod" }).Should().BeEmpty();
		}

		[TestCase("")]
		[TestCase("not json")]
		[TestCase(@"[ ""array"" ]")]
		[TestCase(@"{ ""data"": { ""channels"": ""oops"" } }")]
		public void FindChannelTokens_should_report_an_unreadable_body(string body)
		{
			NovuWebPushTokens.FindChannelTokens(body, new HashSet<string> { "int-prod" }).Should().BeNull();
		}

		[TestCase("{}")]
		[TestCase("{\"deviceTokens\":null}")]
		[TestCase("{\"deviceTokens\":\"browser\"}")]
		[TestCase("{\"deviceTokens\":[\"browser\",42]}")]
		public void An_unreadable_matching_channel_must_not_be_treated_as_empty(string credentials)
		{
			var json = "{\"channels\":[{\"_integrationId\":\"web\",\"credentials\":" + credentials + "}]}";
			NovuWebPushTokens.FindChannelTokens(json, new HashSet<string> { "web" }).Should().BeNull();
		}

		[Test]
		public void A_valid_empty_matching_channel_can_receive_its_first_token()
		{
			NovuWebPushTokens.FindChannelTokens("{\"channels\":[{\"_integrationId\":\"web\",\"credentials\":{\"deviceTokens\":[]}}]}", new HashSet<string> { "web" }).Should().BeEmpty();
		}
	}
}
