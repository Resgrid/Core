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
		/// <summary>
		/// The safe view of a member message for one outbound channel (catalog v7). A protected
		/// department's message body and subject are encrypted at rest, and notification hosts hold
		/// no grant and no broker, so what they would otherwise hand a carrier is ciphertext. The
		/// sanitized clone carries no content at all - the member is told a message is waiting and
		/// reads it signed in.
		/// </summary>
		/// <param name="culture">
		/// The RECIPIENT's language; the clone carries text a member actually reads. Null falls back
		/// to English.
		/// </param>
		Task<Message> BuildNotificationSafeMessageAsync(int departmentId, Message message,
			ProtectedDataEgressChannel channel, string culture = null);

		/// <param name="culture">
		/// The RECIPIENT's language. The sanitized clone carries text a member actually reads on
		/// their handset, so it is localized like every other outbound string; the safe view is
		/// built per recipient, so the caller passes the profile language it already has. Null
		/// falls back to English — a unit device or a member with no language set.
		/// </param>
		Task<Call> BuildNotificationSafeCallAsync(int departmentId, Call call, ProtectedDataEgressChannel channel,
			string culture = null);

		/// <summary>
		/// True when this channel must receive only sanitized (generic) content for the department:
		/// protection is enforced and the channel's egress mode does not allow protected content.
		/// Exists for notifications that carry protected data WITHOUT a call (trouble alerts, unit
		/// locations, personnel rosters) — the channel decision must never depend on a call object
		/// being present. Fails closed: an unknown protection state reads as sanitized.
		/// </summary>
		Task<bool> IsChannelSanitizedAsync(int departmentId, ProtectedDataEgressChannel channel);
	}
}
