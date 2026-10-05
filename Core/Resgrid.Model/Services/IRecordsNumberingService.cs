using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department record numbering (setting 72): the number pattern and the next number of each sequence it produces.
	/// Allocation itself stays with the Records and incident report services; this is the administration side.
	/// </summary>
	public interface IRecordsNumberingService
	{
		/// <summary>Every sequence the pattern in <paramref name="config"/> produces in <paramref name="year"/>, with what each issues next.</summary>
		Task<List<RecordNumberSequenceStatus>> GetSequencesAsync(int departmentId, RecordsNumberingConfig config, int year);

		/// <summary>
		/// Saves the pattern and digit width, then raises each requested next number. A next number may only rise: one
		/// below what its sequence would already issue is refused, so a department never re-issues a number it has used.
		/// </summary>
		Task<RecordsNumberingSaveResult> SaveAsync(int departmentId, string userId, RecordsNumberingUpdate update, CancellationToken cancellationToken = default(CancellationToken));
	}
}
