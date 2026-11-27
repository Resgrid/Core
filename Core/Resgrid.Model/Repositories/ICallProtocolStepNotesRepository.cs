using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Step-through notes (CallProtocolStepNotes, M0403, ADP catalog 39).</summary>
	public interface ICallProtocolStepNotesRepository : IRepository<CallProtocolStepNote>
	{
		/// <summary>The note an offline event (its client step id) already wrote on the call protocol, or null.</summary>
		Task<CallProtocolStepNote> GetByClientStepIdAsync(int departmentId, int callProtocolId, string clientStepId);

		/// <summary>The department's notes with these ids (a read of the step logs that point at them).</summary>
		Task<List<CallProtocolStepNote>> GetByIdsAsync(int departmentId, IReadOnlyCollection<string> ids);
	}
}
