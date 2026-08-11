using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace ChatPresenceServiceTests
	{
		public class with_the_chat_presence_service : TestBase
		{
			protected IChatPresenceService _chatPresenceService;
			protected Mock<ICacheProvider> _cacheProviderMock;
			protected Dictionary<string, string> _cache;

			protected with_the_chat_presence_service()
			{
				BuildService();
			}

			protected override void Before_all_tests()
			{
				BuildService();
			}

			// Dictionary-backed cache fake: TTLs are ignored (expiry is not under test), everything else
			// behaves like the real store so set/get/remove interplay is exercised end to end.
			private void BuildService()
			{
				_cache = new Dictionary<string, string>(StringComparer.Ordinal);
				_cacheProviderMock = new Mock<ICacheProvider>();

				_cacheProviderMock.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
					.ReturnsAsync((string key, string value, TimeSpan ttl) =>
					{
						_cache[key] = value;
						return true;
					});
				_cacheProviderMock.Setup(x => x.GetStringAsync(It.IsAny<string>()))
					.ReturnsAsync((string key) => _cache.TryGetValue(key, out var value) ? value : null);
				_cacheProviderMock.Setup(x => x.RemoveAsync(It.IsAny<string>()))
					.ReturnsAsync((string key) => _cache.Remove(key));

				_chatPresenceService = new ChatPresenceService(_cacheProviderMock.Object);
			}
		}

		[TestFixture]
		public class when_tracking_active_channels : with_the_chat_presence_service
		{
			[Test]
			public async Task marks_the_user_active_in_the_channel()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1");

				var active = await _chatPresenceService.GetUsersActiveInChannelAsync(1, new List<string> { "user-a", "user-b" }, "chan-1");

				active.Should().ContainSingle().Which.Should().Be("user-a");
			}

			[Test]
			public async Task a_user_active_in_another_channel_is_not_returned()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-2");

				var active = await _chatPresenceService.GetUsersActiveInChannelAsync(1, new List<string> { "user-a" }, "chan-1");

				active.Should().BeEmpty();
			}

			[Test]
			public async Task clearing_removes_the_marker()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1");
				await _chatPresenceService.ClearActiveChannelAsync(1, "user-a");

				var active = await _chatPresenceService.GetUsersActiveInChannelAsync(1, new List<string> { "user-a" }, "chan-1");

				active.Should().BeEmpty();
			}

			[Test]
			public async Task setting_a_null_channel_clears_the_marker()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1");
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", null);

				var active = await _chatPresenceService.GetUsersActiveInChannelAsync(1, new List<string> { "user-a" }, "chan-1");

				active.Should().BeEmpty();
			}
		}

		[TestFixture]
		public class when_tracking_unit_active_channels : with_the_chat_presence_service
		{
			[Test]
			public async Task marks_the_acting_unit_active_in_the_channel()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1", 7);

				(await _chatPresenceService.IsUnitActiveInChannelAsync(1, 7, "chan-1")).Should().BeTrue();
				(await _chatPresenceService.IsUnitActiveInChannelAsync(1, 7, "chan-2")).Should().BeFalse();
			}

			[Test]
			public async Task clearing_the_user_clears_the_unit_marker_too()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1", 7);
				await _chatPresenceService.ClearActiveChannelAsync(1, "user-a");

				(await _chatPresenceService.IsUnitActiveInChannelAsync(1, 7, "chan-1")).Should().BeFalse();
			}

			[Test]
			public async Task switching_acting_unit_clears_the_previous_units_marker()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1", 7);
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1", 9);

				(await _chatPresenceService.IsUnitActiveInChannelAsync(1, 7, "chan-1")).Should().BeFalse();
				(await _chatPresenceService.IsUnitActiveInChannelAsync(1, 9, "chan-1")).Should().BeTrue();
			}

			[Test]
			public async Task heartbeat_touch_keeps_the_active_markers_refreshed()
			{
				await _chatPresenceService.SetActiveChannelAsync(1, "user-a", "chan-1", 7);

				await _chatPresenceService.TouchAsync(1, "user-a");

				// Touch re-writes both markers (fresh TTL) without altering their values.
				(await _chatPresenceService.IsUnitActiveInChannelAsync(1, 7, "chan-1")).Should().BeTrue();
				var active = await _chatPresenceService.GetUsersActiveInChannelAsync(1, new List<string> { "user-a" }, "chan-1");
				active.Should().ContainSingle();
				_cacheProviderMock.Verify(x => x.SetStringAsync("chatactive:1:user-a", It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.AtLeast(2));
			}
		}
	}
}
