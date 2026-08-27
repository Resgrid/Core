using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Builds safe projections of department data for unattended consumers (ADP plan section 8).
	/// Workflows never receive protected plaintext: when a department's protection is enforced,
	/// cataloged scalars become the exact REDACTED placeholder, cataloged binaries are omitted, and
	/// the projection carries is_redacted / redacted_fields / catalog_version metadata — all BEFORE
	/// serialization reaches any queue, run record, retry, dead letter, or designer preview. Never
	/// serialize plaintext and regex-redact afterward.
	/// </summary>
	public interface IProtectedProjectionService
	{
		/// <summary>
		/// Serializes a workflow event payload for the department. Unprotected departments get the
		/// plain serialization; enforced departments get the redacted projection. A redaction fault
		/// never falls back to plaintext — it degrades to a minimal safe payload.
		/// </summary>
		Task<string> BuildSafeWorkflowPayloadAsync(int departmentId, object eventPayload);

		/// <summary>
		/// The notification-safe view of a call for one egress channel (plan sections 9.1/9.3).
		/// Returns the ORIGINAL call when the department is not protection-enforced, or when its
		/// egress policy explicitly allows protected content on that channel. Otherwise returns a
		/// sanitized clone: the system-generated call number and structural/routing fields survive;
		/// every cataloged user-authored field is nulled and the nature reads the generic
		/// "sign in to Resgrid" line — safe to hand to any template, provider DTO, or TTS builder.
		/// ProtectedAfterPin behaves as GenericOnly until the PIN-release flow ships.
		/// </summary>
		Task<Call> BuildNotificationSafeCallAsync(int departmentId, Call call, ProtectedDataEgressChannel channel);
	}
}
