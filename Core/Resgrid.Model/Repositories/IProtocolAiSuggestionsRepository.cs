using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Enhanced AI protocol suggestions for calls (Protocols P4, M0377).</summary>
	public interface IProtocolAiSuggestionsRepository : IRepository<ProtocolAiSuggestion>
	{
		/// <summary>The department's suggestion by id, or null (another department's never comes back).</summary>
		Task<ProtocolAiSuggestion> GetByIdForDepartmentAsync(int departmentId, int protocolAiSuggestionId);

		/// <summary>The suggestions made for a call, newest first.</summary>
		Task<List<ProtocolAiSuggestion>> GetForCallAsync(int departmentId, int callId);

		/// <summary>Marks the suggestion confirmed once (the first confirmation wins); false when it was already confirmed or is not the department's.</summary>
		Task<bool> TryConfirmAsync(int departmentId, int protocolAiSuggestionId, string userId, DateTime confirmedOnUtc, CancellationToken cancellationToken = default);
	}
}
