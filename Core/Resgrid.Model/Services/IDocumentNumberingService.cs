using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department document numbers (see <see cref="DocumentNumberKinds"/>): work orders, invoices, bids and daily time reports
	/// (setting 117) and the records request and prevention numbers (setting 72's DocumentPatterns). A kind with no pattern of
	/// the department's own keeps its built-in numbers, which its service still allocates; a custom pattern takes its numbers
	/// from DocumentNumberSequences. Both number by the numbering year (a fiscal year when the department starts one).
	/// </summary>
	public interface IDocumentNumberingService
	{
		/// <summary>
		/// The numbering year <paramref name="utcDate"/> falls in for <paramref name="kind"/>, read in the department's time zone:
		/// Records kinds by setting 72's year start, the others by setting 117's. The calendar year when none is set.
		/// </summary>
		Task<int> GetNumberingYearAsync(int departmentId, string kind, DateTime utcDate);

		/// <summary>The department's own pattern for <paramref name="kind"/>, or null when the kind keeps its built-in numbers.</summary>
		Task<DocumentNumberPattern> GetPatternAsync(int departmentId, string kind, bool bypassCache = false);

		/// <summary>
		/// Takes the next number in the department's pattern for a document dated <paramref name="utcDate"/>. Null when the kind
		/// keeps its built-in numbers, in which case the caller allocates them as it always has. Each number is issued once.
		/// </summary>
		Task<string> TakeCustomNumberAsync(int departmentId, string kind, DateTime utcDate, CancellationToken cancellationToken = default);

		/// <summary>Every kind on one screen (Records kinds or the rest), with its pattern, sample and next number, without taking any.</summary>
		Task<List<DocumentNumberStatus>> GetStatusesAsync(int departmentId, bool records, DateTime utcDate);

		/// <summary>
		/// Saves setting 117: the year start and the work order, invoice, bid and daily time report patterns, then raises any
		/// requested next numbers (each may only rise). A refused year start saves nothing; a refused pattern keeps the kind's old one.
		/// </summary>
		Task<DocumentNumberingSaveResult> SaveAsync(int departmentId, string userId, DocumentNumberingUpdate update, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves the Records kinds' patterns into setting 72 (records requests, occupancies, inspections, permits, investigations
		/// and evidence) and raises any requested next numbers. Their year start is setting 72's own, saved with Records numbering.
		/// </summary>
		Task<DocumentNumberingSaveResult> SaveRecordsPatternsAsync(int departmentId, string userId, List<DocumentNumberPatternUpdate> patterns, CancellationToken cancellationToken = default);
	}
}
