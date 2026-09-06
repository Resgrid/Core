using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// RMS-1 value seam (plan section 5.9.1): the only caller of the details repository. Every write passes the
	/// Protected Data seam first (ADP catalog v10: the Logs-parity narrative/contact/location/coroner columns are
	/// encrypted in place for an enrolled department) and then <see cref="PrepareForStorage"/>, which enforces
	/// the storage contract so nothing can write ciphertext unmarked or a protected row without its envelopes.
	/// Every read resolves envelopes for the ambient caller, so a row leaves here as plaintext (grant held) or
	/// as the REDACTED sentinel, never as ciphertext.
	/// </summary>
	public class RmsRecordValueService : IRmsRecordValueService
	{
		private readonly IRmsOperationalRecordDetailsRepository _details;
		private readonly IRecordsProtectionService _protection;

		public RmsRecordValueService(IRmsOperationalRecordDetailsRepository details, IRecordsProtectionService protection)
		{
			_details = details;
			_protection = protection;
		}

		/// <summary>Pre-ADP construction (unit tests and tools that never enroll a department).</summary>
		public RmsRecordValueService(IRmsOperationalRecordDetailsRepository details) : this(details, null) { }

		public async Task<RmsOperationalRecordDetail> GetDraftAsync(int departmentId, string recordId)
		{
			var row = await _details.GetDraftAsync(departmentId, recordId);
			await RevealAsync(departmentId, row);
			return row;
		}

		public async Task<RmsOperationalRecordDetail> GetByRevisionAsync(int departmentId, string recordId, string revisionId)
		{
			var row = await _details.GetByRevisionAsync(departmentId, recordId, revisionId);
			await RevealAsync(departmentId, row);
			return row;
		}

		public async Task<IEnumerable<RmsOperationalRecordDetail>> GetDraftsForRecordsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var rows = (await _details.GetDraftsForRecordsAsync(departmentId, recordIds))?.ToList() ?? new List<RmsOperationalRecordDetail>();
			if (_protection != null && rows.Count > 0)
				await _protection.RevealDetailsAsync(departmentId, rows);
			return rows;
		}

		public async Task<RmsOperationalRecordDetail> InsertAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default)
		{
			await ProtectAsync(details, cancellationToken);
			PrepareForStorage(details);
			return await _details.InsertAsync(details, cancellationToken, true);
		}

		public async Task<RmsOperationalRecordDetail> UpdateAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default)
		{
			await ProtectAsync(details, cancellationToken);
			PrepareForStorage(details);
			return await _details.UpdateAsync(details, cancellationToken, true);
		}

		public async Task<RmsOperationalRecordDetail> SaveOrUpdateAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken = default)
		{
			await ProtectAsync(details, cancellationToken);
			PrepareForStorage(details);
			return await _details.SaveOrUpdateAsync(details, cancellationToken, true);
		}

		private async Task RevealAsync(int departmentId, RmsOperationalRecordDetail row)
		{
			if (_protection != null && row != null)
				await _protection.RevealDetailsAsync(departmentId, new[] { row });
		}

		private async Task ProtectAsync(RmsOperationalRecordDetail details, CancellationToken cancellationToken)
		{
			if (_protection == null || details == null)
				return;

			// A REDACTED placeholder can only be restored from the stored copy of the same row (the envelope's
			// AAD is bound to the row key). A working-draft update has one; a revision copy never does, and the
			// callers that write copies require a revealed draft before they get here.
			RmsOperationalRecordDetail existing = null;
			if (details.RevisionId == null && RmsProtectedFields.Details.Values.Any(a => a.Get(details) == ProtectedDataEnvelope.RedactionValue))
				existing = await _details.GetDraftAsync(details.DepartmentId, details.RecordId);

			await _protection.ProtectDetailsAsync(details.DepartmentId, details, existing, null, cancellationToken);

			// Nothing to seal (every cataloged column empty) leaves no envelope behind; the marker then says the
			// row is unprotected, which is what the enrollment sweep will find true when it looks.
			if (details.IsProtected && string.IsNullOrEmpty(details.ProtectedEnvelope) && !RmsProtectedFields.Details.Values.Any(a => ProtectedDataEnvelope.HasEnvelopePrefix(a.Get(details))))
			{
				details.IsProtected = false;
				details.ProtectedCatalogVersion = 0;
			}
		}

		/// <summary>
		/// The storage contract. An unprotected row carries no envelope (neither the legacy row envelope nor an
		/// in-column one) and catalog version 0; a row marked protected carries at least one. Anything else is a
		/// caller writing around the enrollment path and is refused.
		/// </summary>
		public static void PrepareForStorage(RmsOperationalRecordDetail details)
		{
			if (details == null)
				throw new ArgumentNullException(nameof(details));

			var hasColumnEnvelope = RmsProtectedFields.Details.Values.Any(a => ProtectedDataEnvelope.HasEnvelopePrefix(a.Get(details)));
			if (RmsProtectedFields.Details.Values.Any(a => a.Get(details) == ProtectedDataEnvelope.RedactionValue))
				throw new InvalidOperationException("A REDACTED placeholder cannot be stored as record content; reveal the row before saving it.");

			if (!details.IsProtected)
			{
				if (!string.IsNullOrEmpty(details.ProtectedEnvelope) || hasColumnEnvelope)
					throw new InvalidOperationException("A protected envelope was supplied for a record detail row that is not marked protected; envelopes are written only through Protected Data enrollment.");

				details.ProtectedCatalogVersion = 0;
				return;
			}

			if (string.IsNullOrEmpty(details.ProtectedEnvelope) && !hasColumnEnvelope)
				throw new InvalidOperationException("A record detail row marked protected must carry its envelope.");
		}
	}
}
