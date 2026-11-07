using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Unread inbox messages: one COUNT with the inbox list's rules (not deleted, not expired, ReadOn null), on
	/// IX_MessageRecipients_UserId_IsDeleted_ReadOn (M0312). Not cached: read state changes per member.
	/// </summary>
	public class MessagesBadgeCountProvider : IBadgeCountProvider
	{
		private readonly IMessageService _messageService;

		public MessagesBadgeCountProvider(IMessageService messageService)
		{
			_messageService = messageService;
		}

		public string Key => BadgeCountKeys.Messages;

		public string RequiredPolicy => "Messages_View";

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			return new BadgeCount(Key, await _messageService.GetUnreadMessagesCountByUserIdAsync(context.UserId));
		}
	}
}
