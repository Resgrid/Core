using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Communications;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class MessagesInboxWidgetProviderTests
	{
		private Mock<IMessageService> _messages;
		private Mock<IProtectedReadService> _protectedReads;
		private Mock<IUserProfileService> _profiles;

		private static Message Message(int id, int? departmentId, string subject, int minutesAgo, string sender = "sender-1", bool system = false) => new Message
		{
			MessageId = id, DepartmentId = departmentId, Subject = subject, SentOn = Now.AddMinutes(-minutesAgo), SendingUserId = sender, SystemGenerated = system,
			Body = "body " + id
		};

		private MessagesInboxWidgetProvider Provider(List<Message> unread, int count)
		{
			_messages = new Mock<IMessageService>(MockBehavior.Strict);
			_messages.Setup(x => x.GetUnreadInboxMessagesByUserIdAsync(UserId)).ReturnsAsync(unread);
			_messages.Setup(x => x.GetUnreadMessagesCountByUserIdAsync(UserId)).ReturnsAsync(count);

			_protectedReads = new Mock<IProtectedReadService>(MockBehavior.Strict);
			_profiles = new Mock<IUserProfileService>(MockBehavior.Strict);
			_profiles.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>()))
				.ReturnsAsync(new List<UserProfile> { new UserProfile { UserId = "sender-1", FirstName = "Ada", LastName = "Lovelace" } });

			return new MessagesInboxWidgetProvider(_messages.Object, _protectedReads.Object, _profiles.Object);
		}

		private void ResolvesAs(int departmentId, Func<Message, string> subject)
		{
			_protectedReads.Setup(x => x.ResolveMessagesForReadAsync(departmentId, It.IsAny<IReadOnlyList<Message>>(), It.IsAny<string>(), UserId, It.IsAny<CancellationToken>()))
				.Callback<int, IReadOnlyList<Message>, string, string, CancellationToken>((_, list, _, _, _) =>
				{
					foreach (var message in list)
						message.Subject = subject(message);
				})
				.ReturnsAsync(new ProtectedReadResult());
		}

		[Test]
		public async Task Unprotected_subjects_are_plain_with_sender_names_and_the_navbar_count()
		{
			var provider = Provider(new List<Message> { Message(1, DepartmentId, "Drill tonight", 5), Message(2, DepartmentId, "Shift swap", 50, system: true) }, 7);
			ResolvesAs(DepartmentId, m => m.Subject);

			var data = (MessagesInboxWidgetData)await provider.GetDataAsync(For("messages.inbox"), CancellationToken.None);

			data.UnreadCount.Should().Be(7);
			data.Items.Should().HaveCount(2);
			data.Items[0].Subject.Value.Should().Be("Drill tonight");
			data.Items[0].Subject.IsProtected.Should().BeFalse();
			data.Items[0].SenderName.Should().Be("Ada Lovelace");
			data.Items[1].SenderName.Should().BeEmpty("a system message has no sender");
			data.Items[0].Url.Should().Be("/User/Messages/ViewMessage?messageId=1");
		}

		[Test]
		public async Task Redacted_subjects_become_shields_and_never_carry_the_literal()
		{
			var provider = Provider(new List<Message> { Message(1, DepartmentId, "rgdp:v1:sealed", 5) }, 1);
			ResolvesAs(DepartmentId, _ => ProtectedDataEnvelope.RedactionValue);

			var data = (MessagesInboxWidgetData)await provider.GetDataAsync(
				For("messages.inbox", adpEnforced: true, adpPermissions: new[] { PermissionTypes.ViewProtectedOperationalData }), CancellationToken.None);

			var subject = data.Items.Single().Subject;
			subject.IsProtected.Should().BeTrue();
			subject.CanReveal.Should().BeTrue();
			subject.Value.Should().BeNull();
		}

		[Test]
		public async Task A_member_without_the_view_permission_cannot_reveal()
		{
			var provider = Provider(new List<Message> { Message(1, DepartmentId, "x", 5) }, 1);
			ResolvesAs(DepartmentId, _ => ProtectedDataEnvelope.RedactionValue);

			var data = (MessagesInboxWidgetData)await provider.GetDataAsync(For("messages.inbox", adpEnforced: true), CancellationToken.None);

			data.Items.Single().Subject.Should().BeEquivalentTo(new { IsProtected = true, CanReveal = false, Value = (string)null });
		}

		[Test]
		public async Task Each_owning_department_resolves_separately_and_only_the_active_one_gets_the_grant()
		{
			var provider = Provider(new List<Message> { Message(1, DepartmentId, "mine", 5), Message(2, OtherDepartmentId, "theirs", 6) }, 2);
			ResolvesAs(DepartmentId, m => m.Subject);
			ResolvesAs(OtherDepartmentId, _ => ProtectedDataEnvelope.RedactionValue);

			var data = (MessagesInboxWidgetData)await provider.GetDataAsync(
				For("messages.inbox", adpEnforced: true, adpPermissions: new[] { PermissionTypes.ViewProtectedOperationalData }, grant: "grant-token"), CancellationToken.None);

			_protectedReads.Verify(x => x.ResolveMessagesForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<Message>>(), "grant-token", UserId, It.IsAny<CancellationToken>()), Times.Once);
			_protectedReads.Verify(x => x.ResolveMessagesForReadAsync(OtherDepartmentId, It.IsAny<IReadOnlyList<Message>>(), null, UserId, It.IsAny<CancellationToken>()), Times.Once);
			data.Items.Single(i => i.MessageId == 2).Subject.Should().BeEquivalentTo(new { IsProtected = true, CanReveal = false });
			data.Items.Single(i => i.MessageId == 1).Subject.Value.Should().Be("mine");
		}

		[Test]
		public async Task Only_the_capped_rows_are_resolved()
		{
			var unread = Enumerable.Range(1, 12).Select(i => Message(i, DepartmentId, "s" + i, i)).ToList();
			var provider = Provider(unread, 12);
			var resolved = 0;
			_protectedReads.Setup(x => x.ResolveMessagesForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<Message>>(), It.IsAny<string>(), UserId, It.IsAny<CancellationToken>()))
				.Callback<int, IReadOnlyList<Message>, string, string, CancellationToken>((_, list, _, _, _) => resolved += list.Count)
				.ReturnsAsync(new ProtectedReadResult());

			var data = (MessagesInboxWidgetData)await provider.GetDataAsync(
				For("messages.inbox", CommsPlanningWidgetContexts.Settings(("maxItems", 3))), CancellationToken.None);

			data.Items.Select(i => i.MessageId).Should().Equal(1, 2, 3);
			resolved.Should().Be(3);
		}

		[Test]
		public async Task An_empty_inbox_reads_nothing_else()
		{
			var provider = Provider(new List<Message>(), 0);

			var data = (MessagesInboxWidgetData)await provider.GetDataAsync(For("messages.inbox"), CancellationToken.None);

			data.Items.Should().BeEmpty();
			_profiles.Verify(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>()), Times.Never);
		}
	}
}
