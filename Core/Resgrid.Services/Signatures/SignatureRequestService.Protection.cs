using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// ADP catalog 39 (round 8): the outside signer of a Records-owned signature (an inspection's occupant representative, a custody
	/// transfer's outside party) is the same person Records protects (<c>RmsInspections.SignatureName</c>, <c>RmsInvestigationCustody.ToExternal</c>).
	/// For a department that encrypts new writes the name is kept once and sealed through the generic seam (signer, record, and the capture's
	/// printed and witness names, which already hold the application-key ciphertext and are wrapped as they are); the snapshot the lists read
	/// without a grant is left empty, and the sealed evidence names the signer by role. Customers, vendors and outside evaluators are never
	/// encrypted: their documents are customer-facing or external (not under ADP).
	/// </summary>
	public partial class SignatureRequestService
	{
		/// <summary>What the sealed evidence and its audit page show instead of a protected outside signer's name.</summary>
		public const string ProtectedSignerName = "Name protected (Advanced Data Protection)";

		/// <summary>
		/// True when the request is on a Records-owned subject and the department encrypts new writes. An unreadable protection state counts
		/// as protected (nothing is copied; the write itself fails closed in the seam).
		/// </summary>
		private async Task<bool> ProtectsSignerIdentityAsync(SignatureRequestRecord request)
		{
			if (request == null || !ContentProtectedFields.IsRecordsSignatureSubject(request.SubjectKind))
				return false;
			if (_protection == null)
				return false;

			try
			{
				return await _protection.ShouldEncryptNewWritesAsync(request.DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The protection state of department {request.DepartmentId} could not be read for a Records signature; the signer's name is treated as protected.");
				return true;
			}
		}

		/// <summary>Seals a newly stored outside signer's name on a Records-owned request of a protected department.</summary>
		private async Task ProtectSignerAsync(SignatureRequestRecord request, SignatureRequestSigner signer, CancellationToken cancellationToken)
		{
			if (signer == null || signer.IsMember || string.IsNullOrEmpty(signer.ExternalName) || ProtectedDataEnvelope.HasEnvelopePrefix(signer.ExternalName))
				return;
			if (!await ProtectsSignerIdentityAsync(request))
				return;

			if (await SealAsync(request.DepartmentId, signer, signer.SignatureRequestSignerId, ContentProtectedFields.Signers, cancellationToken))
				await _signers.UpdateAsync(signer, cancellationToken, true);
		}

		/// <summary>Seals the outside signer's name on the signer, the signature record and the capture (printed and witness names).</summary>
		private async Task ProtectIdentityAsync(SignatureRequestRecord request, SignatureRequestSigner signer, SignatureRecord record, SignatureCapture capture,
			CancellationToken cancellationToken)
		{
			if (signer != null && await SealAsync(request.DepartmentId, signer, signer.SignatureRequestSignerId, ContentProtectedFields.Signers, cancellationToken))
				await _signers.UpdateAsync(signer, cancellationToken, true);
			if (record != null && await SealAsync(request.DepartmentId, record, record.SignatureRecordId, ContentProtectedFields.Records, cancellationToken))
				await _records.UpdateAsync(record, cancellationToken, true);
			if (capture != null && await SealAsync(request.DepartmentId, capture, capture.SignatureCaptureId, ContentProtectedFields.Captures, cancellationToken))
				await _captures.UpdateAsync(capture, cancellationToken, true);
		}

		/// <summary>
		/// One row through the generic seam (workload encrypt-only lane). The row was inserted first because its identity is the AAD row key;
		/// a blocked write throws so the plaintext is never left silently (the request stays open and can be voided).
		/// </summary>
		private async Task<bool> SealAsync<T>(int departmentId, T row, int rowId,
			System.Collections.Generic.IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, CancellationToken cancellationToken) where T : class
		{
			if (_protectedWrite == null || rowId <= 0)
				return false;

			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, row, null, rowId.ToString(CultureInfo.InvariantCulture), accessors,
				null, null, null, workloadCaller: true, cancellationToken);
			if (result != null && !result.Success)
				throw new InvalidOperationException($"Protected write blocked ({result.Reason}); a Records signature's signer name has transient plaintext pending re-encryption.");
			return result?.Changed == true;
		}

		/// <summary>A copy of an outside signer for the sealed evidence: the name replaced by <see cref="ProtectedSignerName"/>.</summary>
		private static SignatureRequestSigner MaskedSigner(SignatureRequestSigner signer)
		{
			if (signer == null || signer.IsMember)
				return signer;

			return new SignatureRequestSigner
			{
				SignatureRequestSignerId = signer.SignatureRequestSignerId,
				SignatureRequestId = signer.SignatureRequestId,
				DepartmentId = signer.DepartmentId,
				SignerOrder = signer.SignerOrder,
				Role = signer.Role,
				SignerKind = signer.SignerKind,
				UserId = signer.UserId,
				StepLabel = signer.StepLabel,
				ExternalName = ProtectedSignerName,
				ContactMasked = signer.ContactMasked,
				AuthMethod = signer.AuthMethod,
				Status = signer.Status,
				SignedOn = signer.SignedOn,
				DeclinedOn = signer.DeclinedOn,
				SignatureRecordId = signer.SignatureRecordId,
				ConsentTextVersion = signer.ConsentTextVersion,
				ConsentedOn = signer.ConsentedOn,
				OtpChannel = signer.OtpChannel,
				OtpVerifiedOn = signer.OtpVerifiedOn,
				RequiredLevel = signer.RequiredLevel,
				EligibilityJson = signer.EligibilityJson
			};
		}
	}
}
