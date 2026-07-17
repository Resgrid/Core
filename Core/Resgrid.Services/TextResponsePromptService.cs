using System;
using System.Collections.Generic;
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
			var message = new Message
			{
				Subject = ("Calendar RSVP: " + calendarItem.Title).Truncate(150),
				Body = ($"Event #{calendarItem.CalendarItemId}: {calendarItem.Title}. Reply YES or NO.").Truncate(4000),
				SendingUserId = calendarItem.CreatorUserId,
				SentOn = now,
				ExpireOn = now.AddDays(1),
				SystemGenerated = true,
				IsBroadcast = true,
				Type = (int)MessageTypes.CalendarRsvp,
				MessageRecipients = new List<MessageRecipient>
				{
					new MessageRecipient
					{
						UserId = userId,
						Note = TextResponsePromptMetadata.ForCalendarRsvp(calendarItem.CalendarItemId)
					}
				}
			};

			await _messageService.SaveMessageAsync(message, cancellationToken);
		}
	}
}
