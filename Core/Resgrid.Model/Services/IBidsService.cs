using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Contractor bids (Workforce &amp; Business Operations plan, C4; decisions 14, 22). A bid is a priced estimate
	/// for a customer contact under an optional service contract; accepting one launches the deployment wizard whose
	/// last step is <see cref="ConvertBidToDeploymentAsync"/>. Lifecycle changes publish the registry 74–78 triggers
	/// through the domain outbox. Callers authorize.
	/// </summary>
	public interface IBidsService
	{
		Task<List<Bid>> GetBidsForDepartmentAsync(int departmentId, BidStatuses? status = null, int skip = 0, int take = 100);
		Task<int> CountBidsForDepartmentAsync(int departmentId, BidStatuses? status = null);
		Task<List<Bid>> GetBidsByContactIdAsync(string contactId, int departmentId, int skip = 0, int take = 100);
		/// <summary>The department's bids raised under one contract (the contract detail page), newest first.</summary>
		Task<List<Bid>> GetBidsForContractAsync(string serviceContractId, int departmentId);
		/// <summary>The bid with its lines; null when missing or deleted.</summary>
		Task<Bid> GetBidByIdAsync(string bidId, int departmentId);

		/// <summary>Allocates the number and resolves the rate schedule and discount (contract → profile) for a new draft.</summary>
		Task<Bid> CreateDraftBidAsync(int departmentId, string contactId, string serviceContractId, string title, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Header fields of a Draft or Submitted bid; numbers, status, estimates and provenance stay server-owned.</summary>
		Task<Bid> SaveBidAsync(Bid bid, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Upserts lines by id (stale lines removed), snapshots rates from the schedule for lines without one, recalculates estimates.</summary>
		Task<Bid> SaveBidLineItemsAsync(string bidId, int departmentId, List<BidLineItem> lineItems, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<Bid> RecalculateEstimatesAsync(string bidId, int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Draft → Submitted without e-mail (hand delivery).</summary>
		Task<Bid> SubmitBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Renders the PDF, e-mails it, marks the bid Submitted/Sent and publishes <c>BidSent</c>.</summary>
		Task<Bid> SendBidAsync(string bidId, int departmentId, string toEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<Bid> AcceptBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<Bid> DeclineBidAsync(string bidId, int departmentId, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<Bid> WithdrawBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<Bid> ExpireBidAsync(string bidId, int departmentId, CancellationToken cancellationToken = default);
		/// <summary>Soft-deletes a Draft bid.</summary>
		Task<bool> DeleteBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		Task<string> RenderBidHtmlAsync(string bidId, int departmentId);
		Task<byte[]> GetBidPdfAsync(string bidId, int departmentId);

		/// <summary>Everything the "Schedule Deployment Call" wizard prefills from an accepted bid.</summary>
		Task<BidConversionContext> GetBidConversionContextAsync(string bidId, int departmentId);
		/// <summary>Transactional: Call (+ contact) → Deployment (+ roster, equipment, rate snapshots) → calendar item; stamps the bid with both ids.</summary>
		Task<BidConversionResult> ConvertBidToDeploymentAsync(BidConversionRequest request, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Daily sweep: Submitted bids past <c>ValidUntil</c> expire (worker 31). Returns the number expired.</summary>
		Task<int> RunExpirySweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default);
	}
}
