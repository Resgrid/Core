using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department record numbering (setting 72): the number pattern and the next number of each sequence it produces.
	/// Allocation itself stays with the Records and incident report services; this is the administration side. Records request
	/// and prevention number patterns are saved through <see cref="IDocumentNumberingService"/>.
	/// </summary>
	public interface IRecordsNumberingService
	{
		/// <summary>
		/// Every sequence the pattern in <paramref name="config"/> produces in the numbering year named <paramref name="year"/>
		/// (see <see cref="RecordsNumberingConfig.YearStart"/>), with what each issues next.
		/// </summary>
		Task<List<RecordNumberSequenceStatus>> GetSequencesAsync(int departmentId, RecordsNumberingConfig config, int year);

		/// <summary>
		/// Saves the pattern, digit width and year start, then raises each requested next number. A next number may only rise: one
		/// below what its sequence would already issue is refused, so a department never re-issues a number it has used. Next
		/// numbers are not applied in a save that changes the year start, since they were entered against the old year's sequences.
		/// </summary>
		Task<RecordsNumberingSaveResult> SaveAsync(int departmentId, string userId, RecordsNumberingUpdate update, CancellationToken cancellationToken = default(CancellationToken));
	}
}
