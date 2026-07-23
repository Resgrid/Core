using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Messages;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class MessageServiceInboxTests
	{
		[Test]
		public async Task InboxAndUnreadCountExcludeExpiredMessages()
		{
			var repository = new Mock<IMessageRepository>();
			var messages = new List<Message>
			{
				CreateMessage(1, null),
				CreateMessage(2, DateTime.UtcNow.AddMinutes(10)),
				CreateMessage(3, DateTime.UtcNow.AddMinutes(-10))
			};
			repository.Setup(x => x.GetInboxMessagesByUserIdAsync("user-1"))
				.ReturnsAsync(messages);
			repository.Setup(x => x.GetUnreadMessageCountAsync("user-1"))
				.ReturnsAsync(2);
			var service = new MessageService(repository.Object, null, null, null, null, null);

			var inbox = await service.GetInboxMessagesByUserIdAsync("user-1");
			var unreadCount = await service.GetUnreadMessagesCountByUserIdAsync("user-1");

			inbox.Should().HaveCount(2);
			inbox.Should().OnlyContain(x => x.MessageId != 3);
			unreadCount.Should().Be(2);
			repository.Verify(x => x.GetInboxMessagesByUserIdAsync("user-1"), Times.Once);
			repository.Verify(x => x.GetUnreadMessageCountAsync("user-1"), Times.Once);
		}

		[Test]
		public void ApiMappingUsesSystemSenderAndExposesCalendarRsvpMetadataForCurrentUser()
		{
			var message = CreateMessage(12, DateTime.UtcNow.AddMinutes(10));
			message.SystemGenerated = true;
			message.SendingUserId = "user-1";
			message.Type = (int)MessageTypes.CalendarRsvp;
			message.MessageRecipients.First().Note = TextResponsePromptMetadata.ForCalendarRsvp(8521);
			message.MessageRecipients.First().Response = "Yes";

			var result = MessagesController.ConvertMessageResultData(message, null, "user-1", null);

			result.IsSystem.Should().BeTrue();
			result.SendingName.Should().Be("System");
			result.CalendarItemId.Should().Be("8521");
			result.Responded.Should().BeTrue();
			result.ResponseType.Should().Be("Yes");
		}

		[Test]
		public void UnreadCountQueriesApplyUnreadAndActiveMessagePredicates()
		{
			var configurations = new SqlConfiguration[]
			{
				new SqlServerConfiguration(),
				new PostgreSqlConfiguration()
			};

			foreach (var configuration in configurations)
			{
				var query = new SelectUnreadMessageCountQuery(configuration).GetQuery();

				query.Should().Contain("ReadOn");
				query.Should().Contain("ExpireOn");
				query.Should().Contain("@CurrentDate");
			}
		}

		private static Message CreateMessage(int id, DateTime? expireOn)
		{
			return new Message
			{
				MessageId = id,
				Subject = "Message",
				Body = "Body",
				SentOn = DateTime.UtcNow,
				ExpireOn = expireOn,
				MessageRecipients = new List<MessageRecipient>
				{
					new MessageRecipient
					{
						MessageRecipientId = id,
						MessageId = id,
						UserId = "user-1"
					}
				}
			};
		}
	}
}
