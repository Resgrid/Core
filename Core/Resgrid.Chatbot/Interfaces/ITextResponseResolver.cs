using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface ITextResponseResolver
	{
		Task<IReadOnlyList<PendingTextResponse>> GetPendingResponsesAsync(string userId, int departmentId,
			DateTime sinceUtc);

		Task<ChatbotResponse> RecordResponseAsync(PendingTextResponse target, string answer,
			ChatbotSession session);
	}
}
