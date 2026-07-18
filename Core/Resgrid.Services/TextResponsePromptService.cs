using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class TextResponsePromptService : ITextResponsePromptService
	{
		private readonly IMessageService _messageService;

		public TextResponsePromptService(IMessageService messageService)
		{
			_messageService = messageService;
		}

		public async Task RecordCalendarRsvpPromptAsync(CalendarItem calendarItem, string userId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (calendarItem == null || calendarItem.CalendarItemId <= 0
				|| calendarItem.SignupType != (int)CalendarItemSignupTypes.RSVP
				|| string.IsNullOrWhiteSpace(userId))
				return;

			var now = DateTime.UtcNow;
			var metadata = TextResponsePromptMetadata.ForCalendarRsvp(calendarItem.CalendarItemId);
			var inbox = await _messageService.GetInboxMessagesByUserIdAsync(userId);
			var message = inbox?.Find(candidate =>
				!candidate.IsDeleted
				&& candidate.Type == (int)MessageTypes.CalendarRsvp
				&& (candidate.ExpireOn == null || candidate.ExpireOn > now)
				&& candidate.MessageRecipients?.Any(recipient =>
					!recipient.IsDeleted
					&& string.Equals(recipient.UserId, userId, StringComparison.Ordinal)
					&& TextResponsePromptMetadata.TryGetCalendarItemId(recipient.Note, out var calendarItemId)
					&& calendarItemId == calendarItem.CalendarItemId) == true);

			if (message == null)
			{
				message = new Message
				{
					MessageRecipients = new List<MessageRecipient>
					{
						new MessageRecipient
						{
							UserId = userId,
							Note = metadata
						}
					}
				};
			}

			message.Subject = ("Calendar RSVP: " + calendarItem.Title).Truncate(150);
			message.Body = ($"Event #{calendarItem.CalendarItemId}: {calendarItem.Title}. Reply YES or NO.").Truncate(4000);
			message.SendingUserId = calendarItem.CreatorUserId;
			message.SentOn = now;
			message.ExpireOn = now.AddDays(1);
			message.SystemGenerated = true;
			message.IsBroadcast = true;
			message.Type = (int)MessageTypes.CalendarRsvp;

			await _messageService.SaveMessageAsync(message, cancellationToken);
		}
	}
}
