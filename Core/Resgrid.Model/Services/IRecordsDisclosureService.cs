using System.Collections.Generic;
using System.Threading;
using Resgrid.Model.Repositories;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Public-records and access-to-information workflow (RMS plan section 4.7, registry M0171, RMS-3).
	/// <para>
	/// Section 5.8 asks for public-records export and redaction as a control. For a public agency this is a
	/// statutory obligation with a clock, so it is a tracked request rather than an export button: log the
	/// request, resolve its scope through the same Records authorization path everything else uses, redact
	/// against the classification catalog into a <em>new immutable artifact</em>, and release with a produced-set
	/// snapshot so a later amendment cannot silently change what was handed over.
	/// </para>
	/// <para>
	/// Callers must hold <c>RecordDisclosure_Update</c>; this service assumes the policy check already happened
	/// at the controller and enforces the workflow rules, not the permission.
	/// </para>
	/// </summary>
	public interface IRecordsDisclosureService
	{
		Task<RmsDisclosureRequest> CreateRequestAsync(int departmentId, string userId, RmsDisclosureRequest request, CancellationToken cancellationToken = default);

		Task<RmsDisclosureRequest> GetAsync(int departmentId, string requestId);

		Task<List<RmsDisclosureRequest>> QueryAsync(int departmentId, IEnumerable<RmsDisclosureState> states, int skip = 0, int take = 50);

		/// <summary>Saves the scope query and narrative; refused once a production exists, because the scope is what was produced against.</summary>
		Task<RmsDisclosureRequest> SaveScopeAsync(int departmentId, string userId, string requestId, string scopeNarrative, RmsRecordQuery scope, string redactionProfile, CancellationToken cancellationToken = default);

		/// <summary>
		/// What the scope resolves to right now, through the same authorization and group-scope path as the
		/// Records queue. Drafts are listed but never producible: an unfinished report is not a public record.
		/// </summary>
		Task<RmsDisclosureScopePreview> PreviewScopeAsync(int departmentId, string userId, string requestId, int take = 200);

		/// <summary>
		/// Builds a new immutable production: redacted content, the produced-set snapshot, a redaction log and a
		/// checksum. Never mutates a source revision.
		/// </summary>
		Task<RmsDisclosureProduction> ProduceAsync(int departmentId, string userId, string requestId, string redactionProfile = null, CancellationToken cancellationToken = default);

		/// <summary>Releases a prepared production to the requester and closes the statutory clock.</summary>
		Task<RmsDisclosureProduction> ReleaseAsync(int departmentId, string userId, string productionId, CancellationToken cancellationToken = default);

		Task<List<RmsDisclosureProduction>> GetProductionsAsync(int departmentId, string requestId);

		/// <summary>Closes a request without release — denied under an exemption, or withdrawn. A reason is required.</summary>
		Task<RmsDisclosureRequest> CloseAsync(int departmentId, string userId, string requestId, RmsDisclosureState disposition, string reason, CancellationToken cancellationToken = default);

		/// <summary>Re-computes a production's checksum from its stored artifact; false means it was tampered with.</summary>
		Task<bool> VerifyProductionAsync(int departmentId, string productionId);
	}
}
