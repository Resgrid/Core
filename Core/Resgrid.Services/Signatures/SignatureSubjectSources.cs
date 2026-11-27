using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// Business Operations deployment time report: the customer (shift-ticket) signature. Binds to the report's content (entries,
	/// numbers, notes) — not its status, approvals or the signature fields — so approving or billing the report, or writing the
	/// signer's name back, does not invalidate the signature. On completion the signer's name and time are written to the report's
	/// existing customer-signature fields (the typed name stays the fallback when L4 is unavailable).
	/// </summary>
	public class TimeReportSignatureSource : ISignatureSubjectSource
	{
		private static readonly string[] Volatile =
		{
			"Status", "ContractorSignedByUserId", "ContractorSignedOn", "CustomerSignerName", "CustomerSignedOn", "SubmittedByUserId", "SubmittedOn",
			"ApprovedByUserId", "ApprovedOn", "InvoiceId", "RmsExternalOrderFillId", "IsEditable", "IsLive", "Scope", "AddedOn", "AddedByUserId"
		};

		private readonly ITimeTrackingService _timeTracking;

		public TimeReportSignatureSource(ITimeTrackingService timeTracking)
		{
			_timeTracking = timeTracking;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.TimeReport;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var report = await _timeTracking.GetTimeReportByIdAsync(subjectId, departmentId);
			if (report == null || report.DepartmentId != departmentId || report.IsDeleted)
				return null;

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = report.DeploymentTimeReportId,
				Title = "DTR " + report.NumberText() + " " + report.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				RevisionId = report.NumberText(),
				RevisionSha256 = CanonicalContent.Hash("time-report/1", report, Volatile),
				DocumentHtml = await _timeTracking.RenderTimeReportHtmlAsync(report.DeploymentTimeReportId, departmentId),
				SuggestedSignerName = report.CustomerSignerName
			};
		}

		public async Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(signer?.ExternalName))
				return;
			await _timeTracking.SignTimeReportAsync(request.SubjectId, request.DepartmentId, false, signer.ExternalName, actingUserId, null, "e-signature request " + request.SignatureRequestId, cancellationToken);
		}
	}

	/// <summary>Business Operations bid: the customer's acceptance. On completion a submitted bid is accepted through BidsService.</summary>
	public class BidSignatureSource : ISignatureSubjectSource
	{
		private static readonly string[] Volatile =
		{
			"Status", "SentOn", "SentToEmail", "AcceptedOn", "DeclinedOn", "DeclineReason", "ConvertedCallId", "ConvertedDeploymentId", "IsEditable", "IsConverted",
			"AddedOn", "AddedByUserId"
		};

		private readonly IBidsService _bids;

		public BidSignatureSource(IBidsService bids)
		{
			_bids = bids;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.Bid;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var bid = await _bids.GetBidByIdAsync(subjectId, departmentId);
			if (bid == null || bid.DepartmentId != departmentId || bid.IsDeleted)
				return null;

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = bid.BidId,
				Title = (string.IsNullOrWhiteSpace(bid.DisplayNumber) ? "Bid " + bid.BidNumber.ToString(CultureInfo.InvariantCulture) : bid.DisplayNumber) + " " + bid.Title,
				RevisionId = bid.DisplayNumber,
				RevisionSha256 = CanonicalContent.Hash("bid/1", bid, Volatile),
				DocumentHtml = await _bids.RenderBidHtmlAsync(bid.BidId, departmentId),
				SuggestedSignerEmail = bid.SentToEmail
			};
		}

		public async Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default)
		{
			var bid = await _bids.GetBidByIdAsync(request.SubjectId, request.DepartmentId);
			if (bid != null && bid.Status == (int)BidStatuses.Submitted)
				await _bids.AcceptBidAsync(bid.BidId, request.DepartmentId, actingUserId, null, "e-signature request " + request.SignatureRequestId, cancellationToken);
		}
	}

	/// <summary>Business Operations service contract: the counterparty's signature. Activation stays a manual step on the contract.</summary>
	public class ServiceContractSignatureSource : ISignatureSubjectSource
	{
		private static readonly string[] Volatile = { "Status", "AddedOn", "AddedByUserId" };

		private readonly IServiceContractService _contracts;

		public ServiceContractSignatureSource(IServiceContractService contracts)
		{
			_contracts = contracts;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.ServiceContract;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var contract = await _contracts.GetContractByIdAsync(subjectId, departmentId);
			if (contract == null || contract.DepartmentId != departmentId || contract.IsDeleted)
				return null;

			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left}table{border-collapse:collapse}</style></head><body>");
			html.Append("<h1>").Append(E(contract.Name)).Append("</h1><table>");
			void Row(string label, string value) => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			Row("Contract #", contract.ContractNumber);
			Row("Type", ((ServiceContractTypes)contract.ContractType).ToString());
			Row("Starts", contract.StartOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			Row("Ends", contract.EndOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—");
			Row("Discount %", contract.DiscountPercent?.ToString("0.##", CultureInfo.InvariantCulture));
			Row("Terms (net days)", contract.TermsNetDays?.ToString(CultureInfo.InvariantCulture));
			Row("Max deployment days", contract.MaxDeploymentDays?.ToString(CultureInfo.InvariantCulture));
			Row("Response time (min)", contract.ResponseTimeMinutes?.ToString(CultureInfo.InvariantCulture));
			Row("Point of hire", contract.PointOfHire);
			Row("Notes", contract.Notes);
			html.Append("</table></body></html>");

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = contract.ServiceContractId,
				Title = string.IsNullOrWhiteSpace(contract.ContractNumber) ? contract.Name : contract.ContractNumber + " " + contract.Name,
				RevisionId = contract.ContractNumber,
				RevisionSha256 = CanonicalContent.Hash("service-contract/1", contract, Volatile),
				DocumentHtml = html.ToString()
			};
		}

		public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>
	/// Records inspection: the occupant representative's signature. Read through the inspections service (its own access rules);
	/// binds to the inspection's identity and outcome only — never to ADP-protected text — so the hash is the same for every reader.
	/// Records code is unchanged: the typed occupant name stays where it is.
	/// </summary>
	public class RecordsInspectionSignatureSource : ISignatureSubjectSource
	{
		private readonly IRecordsInspectionsService _inspections;

		public RecordsInspectionSignatureSource(IRecordsInspectionsService inspections)
		{
			_inspections = inspections;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.RecordsInspection;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;
			var aggregate = await _inspections.GetAsync(departmentId, userId, subjectId);
			var inspection = aggregate?.Inspection;
			if (inspection == null || inspection.DepartmentId != departmentId || inspection.DeletedOn.HasValue)
				return null;

			var stable = new
			{
				inspection.RmsInspectionId,
				inspection.InspectionNumber,
				inspection.RmsOccupancyId,
				inspection.RmsInspectionProgramId,
				inspection.ParentInspectionId,
				inspection.Result,
				Completed = inspection.CompletedOn?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				inspection.NoticeReference
			};

			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left}table{border-collapse:collapse}</style></head><body>");
			html.Append("<h1>").Append(E(inspection.InspectionNumber)).Append("</h1><table>");
			void Row(string label, string value) => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
			Row("Program", aggregate.Program?.Name);
			Row("Result", ((RmsInspectionResult)inspection.Result).ToString());
			Row("Scheduled", inspection.ScheduledOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			Row("Completed", inspection.CompletedOn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
			Row("Items", (aggregate.Items?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
			Row("Open violations", (aggregate.Violations?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
			Row("Notice", inspection.NoticeReference);
			html.Append("</table></body></html>");

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = inspection.RmsInspectionId,
				Title = "Inspection " + inspection.InspectionNumber,
				RevisionId = inspection.InspectionNumber,
				RevisionSha256 = CanonicalContent.Hash("records-inspection/1", stable),
				DocumentHtml = html.ToString()
			};
		}

		public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	/// <summary>
	/// Records evidence custody transfer to an external party (lab, court, outside agency). Binds to the transfer's identity and
	/// sequence (never its ADP-protected reason or party text). Records code is unchanged.
	/// </summary>
	public class CustodyTransferSignatureSource : ISignatureSubjectSource
	{
		private readonly IRmsInvestigationCustodyRepository _custody;

		public CustodyTransferSignatureSource(IRmsInvestigationCustodyRepository custody)
		{
			_custody = custody;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.CustodyTransfer;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return null;
			var row = await _custody.GetByIdAsync(subjectId);
			if (row == null || row.DepartmentId != departmentId)
				return null;

			var stable = new
			{
				row.RmsInvestigationCustodyId,
				row.RmsInvestigationEvidenceId,
				row.Sequence,
				TransferredOn = row.TransferredOn.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
				row.FromUserId,
				row.ToUserId,
				row.ResultingState
			};

			string E(string v) => WebUtility.HtmlEncode(v ?? string.Empty);
			var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px}th,td{border:1px solid #bbb;padding:4px 6px;text-align:left}table{border-collapse:collapse}</style></head><body>" +
				"<h1>Evidence custody transfer</h1><table>" +
				"<tr><th>Evidence</th><td>" + E(row.RmsInvestigationEvidenceId) + "</td></tr>" +
				"<tr><th>Transfer #</th><td>" + row.Sequence.ToString(CultureInfo.InvariantCulture) + "</td></tr>" +
				"<tr><th>Transferred (UTC)</th><td>" + row.TransferredOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "</td></tr>" +
				"<tr><th>Resulting state</th><td>" + E(((RmsEvidenceState)row.ResultingState).ToString()) + "</td></tr></table></body></html>";

			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = row.RmsInvestigationCustodyId,
				Title = "Custody transfer " + row.Sequence.ToString(CultureInfo.InvariantCulture),
				RevisionId = row.Sequence.ToString(CultureInfo.InvariantCulture),
				RevisionSha256 = CanonicalContent.Hash("custody-transfer/1", stable),
				DocumentHtml = html,
				RetentionYears = null,
				ContextId = row.RmsInvestigationEvidenceId
			};
		}

		public Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
