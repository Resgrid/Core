using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>An occupancy with its hazards, contact links and provenance, revealed for the ambient caller.</summary>
	public class OccupancyAggregate
	{
		public RmsOccupancy Occupancy { get; set; }
		public List<RmsOccupancyHazard> Hazards { get; set; } = new List<RmsOccupancyHazard>();
		public List<RmsOccupancyContactLink> ContactLinks { get; set; } = new List<RmsOccupancyContactLink>();
		public List<RmsOccupancyFieldProvenance> Provenance { get; set; } = new List<RmsOccupancyFieldProvenance>();
		public List<RmsOccupancyCrosswalk> Crosswalks { get; set; } = new List<RmsOccupancyCrosswalk>();
		public int OpenViolationCount { get; set; }
		public ProtectedReadResult Protection { get; set; } = new ProtectedReadResult();
	}

	/// <summary>
	/// The occupancy/property master and its transition from Contacts pre-plans (RMS plan section 4.3, RMS-5):
	/// CRUD on the master, the ContactPreplan/Contact/POI crosswalk inventory and decisions, merge, field provenance,
	/// the department's structure-write ownership switch, and <see cref="OccupancyDispatchProjectionV1"/>.
	/// Reads need Record_View; changes need the PreventionAdmin permission (registry value 69).
	/// </summary>
	public interface IRecordsOccupancyService
	{
		Task<bool> IsModuleEnabledAsync(int departmentId);

		Task<List<RmsOccupancy>> ListAsync(int departmentId, string userId, RmsOccupancyQuery query);
		Task<int> CountAsync(int departmentId, string userId, RmsOccupancyQuery query);
		Task<OccupancyAggregate> GetAsync(int departmentId, string userId, string occupancyId);
		Task<RmsOccupancy> SaveAsync(int departmentId, string userId, RmsOccupancy input, CancellationToken cancellationToken = default);
		Task DeleteAsync(int departmentId, string userId, string occupancyId, CancellationToken cancellationToken = default);
		Task<RmsOccupancy> MarkReviewedAsync(int departmentId, string userId, string occupancyId, int nextReviewMonths, CancellationToken cancellationToken = default);

		Task<RmsOccupancyHazard> SaveHazardAsync(int departmentId, string userId, RmsOccupancyHazard input, CancellationToken cancellationToken = default);
		Task DeleteHazardAsync(int departmentId, string userId, string hazardId, CancellationToken cancellationToken = default);

		Task<RmsOccupancyContactLink> LinkContactAsync(int departmentId, string userId, string occupancyId, string contactId, RmsOccupancyContactRole role, bool isPrimary, CancellationToken cancellationToken = default);
		Task UnlinkContactAsync(int departmentId, string userId, string linkId, CancellationToken cancellationToken = default);

		/// <summary>Scans pre-plans, contacts with site coordinates and POIs into crosswalk candidates grouped by normalized address / proximity.</summary>
		Task<OccupancyCrosswalkInventoryResult> InventoryCandidatesAsync(int departmentId, string userId, CancellationToken cancellationToken = default);
		Task<List<RmsOccupancyCrosswalk>> GetCandidatesAsync(int departmentId, string userId, RmsOccupancyCrosswalkState state, int skip, int take);
		/// <summary>Binds a candidate to an occupancy; with a null occupancy id a new occupancy is created from the source with field provenance.</summary>
		Task<RmsOccupancy> LinkCandidateAsync(int departmentId, string userId, string crosswalkId, string occupancyId, CancellationToken cancellationToken = default);
		Task RejectCandidateAsync(int departmentId, string userId, string crosswalkId, string reason, CancellationToken cancellationToken = default);
		Task<RmsOccupancy> MergeAsync(int departmentId, string userId, string sourceOccupancyId, string targetOccupancyId, CancellationToken cancellationToken = default);

		Task<OccupancyReconciliationStatus> GetReconciliationStatusAsync(int departmentId);
		/// <summary>Switches structure writes to RMS for the department. Refused while any candidate or unreconciled pre-plan remains.</summary>
		Task<RmsOccupancyOwnership> SwitchWriteOwnershipAsync(int departmentId, string userId, string reason, CancellationToken cancellationToken = default);

		Task<OccupancyDispatchProjectionV1> GetDispatchProjectionAsync(int departmentId, string occupancyId, CancellationToken cancellationToken = default);
		Task<OccupancyDispatchProjectionV1> GetDispatchProjectionForContactAsync(int departmentId, string contactId, CancellationToken cancellationToken = default);
		Task<Dictionary<string, OccupancyDispatchProjectionV1>> GetDispatchProjectionsForContactsAsync(int departmentId, IEnumerable<string> contactIds, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// What the Contacts service asks before a pre-plan write and during a site-info read (RMS plan section 4.3 write
	/// cutover): after a department switches structure ownership to RMS, Contacts pre-plan writes are refused with a
	/// stable reason and Contacts reads project from the occupancy master through the Phase A pre-plan shape.
	/// Implemented in Records; Contacts takes it lazily to avoid a construction cycle.
	/// </summary>
	public interface IContactPreplanOwnershipGate
	{
		public const string RecordsOwnedReason = "contacts_preplan_records_owned";

		Task<bool> IsRecordsOwnedAsync(int departmentId);
		/// <summary>The occupancy projection for each contact that is linked to one, in the Contacts pre-plan shape; empty when Contacts still owns writes.</summary>
		Task<Dictionary<string, ContactPreplan>> GetPreplanProjectionsAsync(int departmentId, IEnumerable<string> contactIds, CancellationToken cancellationToken = default);
		/// <summary>The occupancy id a contact projects from, when any.</summary>
		Task<string> GetOccupancyIdForContactAsync(int departmentId, string contactId);
	}
}
