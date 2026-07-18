using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Records outbound prompts whose replies may arrive later over SMS or another text channel.
	/// </summary>
	public interface ITextResponsePromptService
	{
		Task RecordCalendarRsvpPromptAsync(CalendarItem calendarItem, string userId,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
