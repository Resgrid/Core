using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for the chatbot message audit log (ChatbotMessageLog). Inherits standard CRUD
	/// from <see cref="IRepository{T}"/>; rows are written for messages the chatbot could not
	/// handle so unmet feature requests can be analyzed per department.
	/// </summary>
	public interface IChatbotMessageLogRepository : IRepository<ChatbotMessageLog>
	{
		/// <summary>
		/// Gets logged messages for a department since the given UTC timestamp, newest first. Every
		/// row is a gap by construction (only unhandled/fallback messages are written), so no
		/// reason filter is applied here — group by ErrorInfo when analyzing.
		/// </summary>
		Task<IEnumerable<ChatbotMessageLog>> GetUnhandledByDepartmentAsync(int departmentId, DateTime sinceUtc);

		/// <summary>
		/// Gets logged messages across ALL departments since the given UTC timestamp, newest first
		/// (system-wide feature-gap analysis, e.g. the BackOffice report).
		/// </summary>
		Task<IEnumerable<ChatbotMessageLog>> GetAllSinceAsync(DateTime sinceUtc);
	}
}
