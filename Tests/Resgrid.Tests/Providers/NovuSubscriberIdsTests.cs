using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.Messaging;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The one-off cleanup deletes the Novu subscribers of departments that no longer exist. It must only ever act on
	/// ids NovuProvider creates, and only when the department code really is gone.
	/// </summary>
	[TestFixture]
	public class NovuSubscriberIdsTests
	{
		private const string UserId = "5f2b6a0e-1c3d-4e5f-8a9b-0c1d2e3f4a5b";

		[Test]
		public void Parse_should_read_the_three_subscriber_shapes()
		{
			NovuSubscriberIds.Parse($"AB12_User_{UserId}").Should().Be(new NovuSubscriber($"AB12_User_{UserId}", "AB12", NovuSubscriberKind.User, UserId));
			NovuSubscriberIds.Parse($"AB12_IC_User_{UserId}").Should().Be(new NovuSubscriber($"AB12_IC_User_{UserId}", "AB12", NovuSubscriberKind.ICUser, UserId));
			NovuSubscriberIds.Parse("AB12_Unit_42").Should().Be(new NovuSubscriber("AB12_Unit_42", "AB12", NovuSubscriberKind.Unit, "42"));
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("someone@example.com")]
		[TestCase("_User_abc")]
		[TestCase("AB12_User_")]
		[TestCase("AB12_Dispatch_abc")]
		[TestCase("AB12_Unit_abc")]
		[TestCase("AB12_Unit_007")]
		[TestCase("AB12_Unit_-4")]
		[TestCase("AB12_user_abc")]
		public void Parse_should_ignore_ids_NovuProvider_does_not_create(string subscriberId)
		{
			NovuSubscriberIds.Parse(subscriberId).Should().BeNull();
		}

		[Test]
		public void FindOrphans_should_return_only_subscribers_of_codes_that_no_longer_exist()
		{
			var existing = new HashSet<string>(new[] { "LIVE" }, StringComparer.OrdinalIgnoreCase);
			var ids = new[]
			{
				$"LIVE_User_{UserId}", $"live_IC_User_{UserId}", "LIVE_Unit_1",
				$"GONE_User_{UserId}", $"GONE_IC_User_{UserId}", "GONE_Unit_2",
				"GONE_Something_else", "not-a-resgrid-subscriber"
			};

			var orphans = NovuSubscriberIds.FindOrphans(ids, existing);

			orphans.Select(o => o.SubscriberId).Should().Equal($"GONE_User_{UserId}", $"GONE_IC_User_{UserId}", "GONE_Unit_2");
		}

		[Test]
		public void FindRemovedMembers_should_return_only_user_subscribers_of_people_no_longer_in_the_department()
		{
			const string current = "11111111-1111-1111-1111-111111111111";
			const string removed = "22222222-2222-2222-2222-222222222222";
			var currentMembers = new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase)
			{
				["LIVE"] = new HashSet<string>(new[] { current }, StringComparer.OrdinalIgnoreCase)
			};
			var ids = new[]
			{
				$"LIVE_User_{current}", $"live_IC_User_{current.ToUpperInvariant()}",
				$"LIVE_User_{removed}", $"LIVE_IC_User_{removed}",
				"LIVE_Unit_7",
				$"GONE_User_{removed}",
				$"UNREAD_User_{removed}",
				"not-a-resgrid-subscriber"
			};

			var removedSubscribers = NovuSubscriberIds.FindRemovedMembers(ids, currentMembers);

			removedSubscribers.Select(r => r.SubscriberId).Should().Equal($"LIVE_User_{removed}", $"LIVE_IC_User_{removed}");
		}

		[Test]
		public void FindRemovedMembers_should_judge_nothing_without_member_lists()
		{
			NovuSubscriberIds.FindRemovedMembers(new[] { "LIVE_User_abc" }, new Dictionary<string, ISet<string>>()).Should().BeEmpty();
			NovuSubscriberIds.FindRemovedMembers(null, new Dictionary<string, ISet<string>>()).Should().BeEmpty();
		}

		[Test]
		public void FindDeletedUnits_should_return_only_unit_subscribers_of_units_no_longer_in_use()
		{
			var currentUnits = new Dictionary<string, ISet<int>>(StringComparer.OrdinalIgnoreCase)
			{
				["LIVE"] = new HashSet<int> { 7 },
				["EMPTY"] = new HashSet<int>()
			};
			var ids = new[]
			{
				"LIVE_Unit_7", "live_Unit_8",
				"EMPTY_Unit_3",
				$"LIVE_User_{UserId}",
				"GONE_Unit_9", "UNREAD_Unit_10"
			};

			var deleted = NovuSubscriberIds.FindDeletedUnits(ids, currentUnits);

			deleted.Select(d => d.SubscriberId).Should().Equal("live_Unit_8", "EMPTY_Unit_3");
		}

		[Test]
		public void FindDeletedUnits_should_judge_nothing_without_unit_lists()
		{
			NovuSubscriberIds.FindDeletedUnits(new[] { "LIVE_Unit_7" }, new Dictionary<string, ISet<int>>()).Should().BeEmpty();
		}

		[Test]
		public void FindOrphans_should_treat_a_missing_list_as_empty()
		{
			NovuSubscriberIds.FindOrphans(null, new HashSet<string> { "LIVE" }).Should().BeEmpty();
		}

		[Test]
		public void ReadPage_should_read_the_ids_and_whether_another_page_follows()
		{
			var (ids, hasMore) = NovuSubscriberIds.ReadPage(
				"{\"page\":0,\"pageSize\":2,\"hasMore\":true,\"data\":[{\"subscriberId\":\"A_User_1\"},{\"subscriberId\":\"A_Unit_2\"},{\"_id\":\"no-subscriber-id\"}]}", 100);

			ids.Should().Equal("A_User_1", "A_Unit_2");
			hasMore.Should().BeTrue();
		}

		[Test]
		public void ReadPage_should_stop_on_the_last_page()
		{
			var (ids, hasMore) = NovuSubscriberIds.ReadPage("{\"hasMore\":false,\"data\":[{\"subscriberId\":\"A_User_1\"}]}", 1);

			ids.Should().Equal("A_User_1");
			hasMore.Should().BeFalse("hasMore wins over a full page");
		}

		[Test]
		public void ReadPage_without_hasMore_should_continue_only_after_a_full_page()
		{
			NovuSubscriberIds.ReadPage("[{\"subscriberId\":\"A_User_1\"},{\"subscriberId\":\"A_User_2\"}]", 2).HasMore.Should().BeTrue();
			NovuSubscriberIds.ReadPage("{\"data\":[{\"subscriberId\":\"A_User_1\"}]}", 2).HasMore.Should().BeFalse();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not json")]
		[TestCase("{\"data\":\"oops\"}")]
		[TestCase("{\"message\":\"Unauthorized\"}")]
		public void ReadPage_should_report_an_unreadable_page(string body)
		{
			NovuSubscriberIds.ReadPage(body, 100).Ids.Should().BeNull();
		}
	}
}
