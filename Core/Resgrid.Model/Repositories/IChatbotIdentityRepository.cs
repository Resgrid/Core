using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for chat-platform identity links (ChatbotUserIdentities). Inherits standard CRUD
	/// from <see cref="IRepository{T}"/> (including GetByIdAsync and GetAllByUserIdAsync).
	/// </summary>
	public interface IChatbotIdentityRepository : IRepository<ChatbotIdentity>
	{
		/// <summary>Gets the identity for a specific platform + platform user id (the unique lookup).</summary>
		Task<ChatbotIdentity> GetByPlatformAndUserAsync(int platform, string platformUserId);

		/// <summary>Gets an identity by platform user id across any platform (used for phone-number matches).</summary>
		Task<ChatbotIdentity> GetByPlatformUserIdAsync(string platformUserId);
	}
}
