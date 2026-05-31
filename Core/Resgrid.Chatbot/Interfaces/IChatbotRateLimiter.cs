using System.Threading.Tasks;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>
	/// Per-minute rate limiting for inbound chatbot messages, enforced per user and per department.
	/// Uses Redis for cross-instance counting when available, falling back to in-memory otherwise.
	/// </summary>
	public interface IChatbotRateLimiter
	{
		/// <summary>
		/// Records an inbound message and returns false if it would exceed either the per-user or
		/// per-department limit for the current minute. A limit &lt;= 0 means unlimited.
		/// </summary>
		Task<bool> TryAcquireAsync(string userId, int departmentId, int perUserPerMinute, int perDepartmentPerMinute);
	}
}
