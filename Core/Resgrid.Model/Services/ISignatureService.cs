using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The one signing layer (S0, plan §21): L1 attest (the signed-in session) and L2 step-up (a fresh step-up recorded as the
	/// session's second-factor evidence, or a passkey assertion whose WebAuthn challenge embeds the artifact hash). A signature
	/// binds to an artifact hash, never a live row; its statement is explicit and stored; each use has a level floor that a
	/// department may raise but never lower.
	/// </summary>
	public interface ISignatureService
	{
		/// <summary>
		/// Records a signature when the proof meets the use's level (L2 needs a step-up within the step-up window, or a
		/// passkey assertion bound to exactly this artifact hash). Refusals come back as an outcome, never as a stored row.
		/// </summary>
		Task<SignatureResult> SignAsync(SignatureRequest request, SignatureProof proof, CancellationToken cancellationToken = default);

		/// <summary>
		/// Recomputes the artifact's hash from the subject as it is stored now (a protocol version's content, a certification
		/// record) and compares it with the signed hash.
		/// </summary>
		Task<SignatureVerification> VerifyAsync(int departmentId, int signatureRecordId);

		/// <summary>The signature against an artifact hash the caller computed.</summary>
		SignatureVerification Verify(SignatureRecord record, string currentArtifactSha256);

		/// <summary>Voids a signature (it stays, marked voided with who, when and why). Null when it is not the department's.</summary>
		Task<SignatureRecord> VoidAsync(int departmentId, int signatureRecordId, string userId, string reason, CancellationToken cancellationToken = default);

		/// <summary>The subject's signatures, oldest first.</summary>
		Task<List<SignatureRecord>> GetForSubjectAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId);

		/// <summary>The subject's signatures, each checked against the subject as it is stored now.</summary>
		Task<List<SignatureVerification>> GetVerifiedForSubjectAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId);

		Task<SignatureRecord> GetByIdAsync(int departmentId, int signatureRecordId);

		/// <summary>The canonical artifact of a certification verification (holder, type and dates; never protected text or bytes).</summary>
		string CertificationArtifactSha256(PersonnelCertification certification);
	}
}
