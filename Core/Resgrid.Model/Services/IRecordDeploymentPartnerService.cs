using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public enum RecordDeploymentPartnerErrors
	{
		None = 0,
		NotFound = 1,
		NotAuthorized = 2,
		ClosedOut = 3,
		/// <summary>The link is not enabled, or does not join this department to another.</summary>
		NotLinked = 4,
		/// <summary>Someone else changed the deployment at the same moment.</summary>
		Conflict = 5
	}

	/// <summary>A fill a partner department's admins are asked to accept (the Links "Agreements" page).</summary>
	public sealed class RecordDeploymentPartnerItem
	{
		public int OriginDepartmentId { get; set; }
		public string OriginDepartmentName { get; set; }
		public string Title { get; set; }
		public int SignatureRequestId { get; set; }
		public DateTime? RequestedOn { get; set; }
	}

	/// <summary>
	/// The linked partner department's acceptance of a Records deployment fill (round 10 A3; e-Sign §20 "Deployments (Records)": L2 for a partner
	/// agency on Resgrid, L4 otherwise). A deployment order may name a Department Link to the department it is filled for; each fill the
	/// department accepts then opens a signature request in the partner department (kind 27, its department admins at L2, bound to the order,
	/// request, resource and position, never the roster, travel or notes). The partner's acceptance is the plan's AssignmentAcceptedBy/On. A
	/// disabled or removed link, a changed partner or a declined fill voids an open request; every read re-checks the link.
	/// </summary>
	public interface IRecordDeploymentPartnerService
	{
		/// <summary>The department's enabled links (the partners an order may name).</summary>
		Task<List<DepartmentLink>> GetPartnerLinksAsync(int departmentId);

		/// <summary>Names (or with null clears) the linked partner of an order; open requests to a previous partner are voided, accepted fills go to the new one.</summary>
		Task<RecordDeploymentPartnerErrors> SetPartnerAsync(int departmentId, string userId, string orderId, int? departmentLinkId, CancellationToken cancellationToken = default);

		/// <summary>Opens the partner's acceptance request for an accepted fill when the order names a routable partner; the fill as it stands otherwise.</summary>
		Task<RmsExternalOrderFill> RequestAsync(int departmentId, string fillId, string userId, CancellationToken cancellationToken = default);

		/// <summary>Brings the fill's partner state up to date with its request (signed, declined, expired or voided).</summary>
		Task<RmsExternalOrderFill> RefreshAsync(RmsExternalOrderFill fill, CancellationToken cancellationToken = default);

		/// <summary>The fills a partner department is asked to accept now.</summary>
		Task<List<RecordDeploymentPartnerItem>> GetAwaitingAsync(int partnerDepartmentId);

		/// <summary>A declined fill's open partner request is voided.</summary>
		Task<int> LapseForFillAsync(int departmentId, string fillId, CancellationToken cancellationToken = default);

		/// <summary>Voids open partner requests the link (or, with null, any link) no longer routes.</summary>
		Task<int> LapseForLinkAsync(int? departmentLinkId, CancellationToken cancellationToken = default);
	}
}
