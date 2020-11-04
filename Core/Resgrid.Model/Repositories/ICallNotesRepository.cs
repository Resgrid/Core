using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallNotesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallNote}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallNote}" />
	public interface ICallNotesRepository: IRepository<CallNote>
	{
		/// <summary>
		/// Gets the call notes by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallNote&gt;&gt;.</returns>
		Task<IEnumerable<CallNote>> GetCallNotesByCallIdAsync(int callId);
	}
}
