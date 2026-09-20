using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for chatbot linking codes (ChatbotLinkingCodes). Inherits standard CRUD from
	/// <see cref="IRepository{T}"/> (including GetAllByUserIdAsync, used for the per-user daily cap).
	/// </summary>
	public interface IChatbotLinkingCodeRepository : IRepository<ChatbotLinkingCode>
	{
		/// <summary>Gets the most recent linking code row for a given code value.</summary>
		Task<ChatbotLinkingCode> GetByCodeAsync(string code);

		/// <summary>Atomically consumes an unused, unexpired code. Returns false if it cannot be consumed.</summary>
		Task<bool> TryConsumeAsync(string id, int platform, string platformUserId, DateTime utcNow);
	}
}
