using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// In-memory stand-ins for the RMS-2 incident report repositories (M0164–M0166). Reuses FakeRmsStore for the
	/// shared tables (revisions, group scope, shares, projections, audits, outbox, unit of work) so the incident
	/// report service and the submission worker logic can be asserted without a database.
	/// </summary>
	public sealed class FakeIncidentStore
	{
		public FakeRmsStore Shared { get; } = new FakeRmsStore();

		public List<RmsIncidentReport> Reports { get; } = new List<RmsIncidentReport>();
		public List<RmsSourceFact> Facts { get; } = new List<RmsSourceFact>();
		public List<RmsUnitResponse> Units { get; } = new List<RmsUnitResponse>();
		public List<RmsIncidentType> Types { get; } = new List<RmsIncidentType>();
		public List<RmsActionTactic> Tactics { get; } = new List<RmsActionTactic>();
		public List<RmsAid> Aids { get; } = new List<RmsAid>();
		public List<RmsLocation> Locations { get; } = new List<RmsLocation>();
		public List<RmsNarrative> Narratives { get; } = new List<RmsNarrative>();
		public List<RmsValidationIssue> Issues { get; } = new List<RmsValidationIssue>();
		public List<RmsSubmission> Submissions { get; } = new List<RmsSubmission>();
		public List<RmsSubmissionExchange> Exchanges { get; } = new List<RmsSubmissionExchange>();
		public List<RmsSignature> Signatures { get; } = new List<RmsSignature>();
		public List<RmsIncidentModule> Modules { get; } = new List<RmsIncidentModule>();
		public List<RmsIncidentResource> Resources { get; } = new List<RmsIncidentResource>();
		public List<RmsCasualtyRescue> Casualties { get; } = new List<RmsCasualtyRescue>();
		public List<RmsExposure> Exposures { get; } = new List<RmsExposure>();
		public List<RmsIncidentAnalysis> Analyses { get; } = new List<RmsIncidentAnalysis>();
		public List<RmsIncidentProperty> Properties { get; } = new List<RmsIncidentProperty>();
		public List<RmsIncidentVehicle> Vehicles { get; } = new List<RmsIncidentVehicle>();

		public Mock<IRmsIncidentReportsRepository> ReportsRepo { get; } = new Mock<IRmsIncidentReportsRepository>();
		public Mock<IRmsSourceFactsRepository> FactsRepo { get; } = new Mock<IRmsSourceFactsRepository>();
		public Mock<IRmsUnitResponsesRepository> UnitsRepo { get; } = new Mock<IRmsUnitResponsesRepository>();
		public Mock<IRmsIncidentTypesRepository> TypesRepo { get; } = new Mock<IRmsIncidentTypesRepository>();
		public Mock<IRmsActionTacticsRepository> TacticsRepo { get; } = new Mock<IRmsActionTacticsRepository>();
		public Mock<IRmsAidsRepository> AidsRepo { get; } = new Mock<IRmsAidsRepository>();
		public Mock<IRmsLocationsRepository> LocationsRepo { get; } = new Mock<IRmsLocationsRepository>();
		public Mock<IRmsNarrativesRepository> NarrativesRepo { get; } = new Mock<IRmsNarrativesRepository>();
		public Mock<IRmsValidationIssuesRepository> IssuesRepo { get; } = new Mock<IRmsValidationIssuesRepository>();
		public Mock<IRmsSubmissionsRepository> SubmissionsRepo { get; } = new Mock<IRmsSubmissionsRepository>();
		public Mock<IRmsSubmissionExchangesRepository> ExchangesRepo { get; } = new Mock<IRmsSubmissionExchangesRepository>();
		public Mock<IRmsSignaturesRepository> SignaturesRepo { get; } = new Mock<IRmsSignaturesRepository>();
		public Mock<IRmsIncidentModulesRepository> ModulesRepo { get; } = new Mock<IRmsIncidentModulesRepository>();
		public Mock<IRmsIncidentResourcesRepository> ResourcesRepo { get; } = new Mock<IRmsIncidentResourcesRepository>();
		public Mock<IRmsCasualtyRescuesRepository> CasualtiesRepo { get; } = new Mock<IRmsCasualtyRescuesRepository>();
		public Mock<IRmsExposuresRepository> ExposuresRepo { get; } = new Mock<IRmsExposuresRepository>();
		public Mock<IRmsIncidentAnalysesRepository> AnalysesRepo { get; } = new Mock<IRmsIncidentAnalysesRepository>();
		public Mock<IRmsIncidentPropertiesRepository> PropertiesRepo { get; } = new Mock<IRmsIncidentPropertiesRepository>();
		public Mock<IRmsIncidentVehiclesRepository> VehiclesRepo { get; } = new Mock<IRmsIncidentVehiclesRepository>();

		public List<RmsRevision> Revisions => Shared.Revisions;
		public List<RmsRecordGroupScope> Scopes => Shared.Scopes;
		public List<RmsRecordSearchProjection> Projections => Shared.Projections;
		public List<RmsAccessAudit> Audits => Shared.Audits;
		public List<DomainEventOutboxEntry> Outbox => Shared.Outbox;
		public Mock<IUnitOfWork> UnitOfWork => Shared.UnitOfWork;

		public FakeIncidentStore()
		{
			// Reports
			ReportsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsIncidentReport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsIncidentReport e, CancellationToken c, bool f) => { Reports.Add(e); return e; });
			ReportsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsIncidentReport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsIncidentReport e, CancellationToken c, bool f) => { Reports.RemoveAll(x => x.RmsIncidentReportId == e.RmsIncidentReportId); Reports.Add(e); return e; });
			ReportsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Reports.FirstOrDefault(x => x.DepartmentId == d && x.RmsIncidentReportId == id));
			ReportsRepo.Setup(r => r.GetByCallAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, int call, string entity) => Reports.FirstOrDefault(x => x.DepartmentId == d && x.CallId == call && x.ReportingEntityId == entity && x.DeletedOn == null));
			ReportsRepo.Setup(r => r.GetByCallAnyEntityAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int call) => Reports.Where(x => x.DepartmentId == d && x.CallId == call).ToList());
			ReportsRepo.Setup(r => r.GetByNerisIncidentIdAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Reports.FirstOrDefault(x => x.DepartmentId == d && x.NerisIncidentId == id));
			// The state filter is honoured: a fake that ignores it would let a queue-count bug pass unnoticed.
			ReportsRepo.Setup(r => r.QueryAsync(It.IsAny<int>(), It.IsAny<RmsIncidentReportQuery>()))
				.ReturnsAsync((int d, RmsIncidentReportQuery q) => MatchReports(d, q).ToList());
			ReportsRepo.Setup(r => r.CountAsync(It.IsAny<int>(), It.IsAny<RmsIncidentReportQuery>()))
				.ReturnsAsync((int d, RmsIncidentReportQuery q) => MatchReports(d, q).Count());
			ReportsRepo.Setup(r => r.GetYearsAsync(It.IsAny<int>()))
				.ReturnsAsync((int d) => Reports.Where(x => x.DepartmentId == d).Select(x => (x.CallCreatedOn ?? x.CreatedOn).Year).Distinct().ToList());
			ReportsRepo.Setup(r => r.GetMaxRecordNumberSequenceAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string prefix) => Reports
					.Where(x => x.DepartmentId == d && x.RecordNumber != null && x.RecordNumber.StartsWith(prefix, StringComparison.Ordinal))
					.Select(x => int.TryParse(x.RecordNumber.Substring(prefix.Length), out var n) ? n : 0)
					.DefaultIfEmpty(0).Max());
			ReportsRepo.Setup(r => r.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long expected, CancellationToken c) =>
				{
					var row = Reports.FirstOrDefault(x => x.DepartmentId == d && x.RmsIncidentReportId == id && x.RowVersion == expected);
					if (row == null) return false;
					row.RowVersion = expected + 1;
					return true;
				});

			// Child rows (draft rows carry RevisionId null; revision copies carry the revision id)
			Wire(FactsRepo, Facts, x => x.RmsSourceFactId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(UnitsRepo, Units, x => x.RmsUnitResponseId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(TypesRepo, Types, x => x.RmsIncidentTypeId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(TacticsRepo, Tactics, x => x.RmsActionTacticId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(AidsRepo, Aids, x => x.RmsAidId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(LocationsRepo, Locations, x => x.RmsLocationId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(NarrativesRepo, Narratives, x => x.RmsNarrativeId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(ModulesRepo, Modules, x => x.RmsIncidentModuleId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(ResourcesRepo, Resources, x => x.RmsIncidentResourceId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(CasualtiesRepo, Casualties, x => x.RmsCasualtyRescueId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(ExposuresRepo, Exposures, x => x.RmsExposureId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(PropertiesRepo, Properties, x => x.RmsIncidentPropertyId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);
			Wire(VehiclesRepo, Vehicles, x => x.RmsIncidentVehicleId, x => x.DepartmentId, x => x.RecordId, x => x.RevisionId);

			ModulesRepo.Setup(r => r.GetForRecordByKindAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsIncidentModuleKind>()))
				.ReturnsAsync((int d, string rec, string rev, RmsIncidentModuleKind kind) => (IEnumerable<RmsIncidentModule>)Modules
					.Where(x => x.DepartmentId == d && x.RecordId == rec && x.RevisionId == rev && x.ModuleKind == (int)kind).OrderBy(x => x.Ordinal).ToList());

			// Incident analysis (RMS-3): its own aggregate root, one per report
			AnalysesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsIncidentAnalysis>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsIncidentAnalysis e, CancellationToken c, bool f) => { Analyses.Add(e); return e; });
			AnalysesRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsIncidentAnalysis>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsIncidentAnalysis e, CancellationToken c, bool f) => { Analyses.RemoveAll(x => x.RmsIncidentAnalysisId == e.RmsIncidentAnalysisId); Analyses.Add(e); return e; });
			AnalysesRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Analyses.FirstOrDefault(x => x.DepartmentId == d && x.RmsIncidentAnalysisId == id));
			AnalysesRepo.Setup(r => r.GetForReportAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string reportId) => Analyses.FirstOrDefault(x => x.DepartmentId == d && x.IncidentReportId == reportId && x.DeletedOn == null));
			AnalysesRepo.Setup(r => r.GetAwaitingIncidentAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int take) => (IEnumerable<RmsIncidentAnalysis>)Analyses
					.Where(x => x.DepartmentId == d && x.State == (int)RmsIncidentAnalysisState.Finalized && x.NerisAnalysisId == null && x.DeletedOn == null).Take(take).ToList());
			AnalysesRepo.Setup(r => r.CountByStateAsync(It.IsAny<int>(), It.IsAny<RmsIncidentAnalysisState>()))
				.ReturnsAsync((int d, RmsIncidentAnalysisState state) => Analyses.Count(x => x.DepartmentId == d && x.State == (int)state && x.DeletedOn == null));
			AnalysesRepo.Setup(r => r.CountVisibleByStateAsync(It.IsAny<int>(), It.IsAny<RmsIncidentAnalysisState>(), It.IsAny<List<int>>(), It.IsAny<string>()))
				.ReturnsAsync((int d, RmsIncidentAnalysisState state, List<int> groups, string user) => Analyses.Count(x => x.DepartmentId == d && x.State == (int)state && x.DeletedOn == null
					&& MatchReports(d, new RmsIncidentReportQuery { VisibleGroupIds = groups, ViewerUserId = user }).Any(r => r.RmsIncidentReportId == x.IncidentReportId)));

			// Validation issues: a run replaces every issue of its source
			IssuesRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Issues.Where(x => x.DepartmentId == d && x.RecordId == id).ToList());
			IssuesRepo.Setup(r => r.ReplaceForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<RmsValidationSource>(), It.IsAny<IEnumerable<RmsValidationIssue>>(), It.IsAny<CancellationToken>()))
				.Returns((int d, string id, RmsValidationSource source, IEnumerable<RmsValidationIssue> issues, CancellationToken c) =>
				{
					Issues.RemoveAll(x => x.DepartmentId == d && x.RecordId == id && x.Source == (int)source);
					foreach (var issue in issues ?? Enumerable.Empty<RmsValidationIssue>())
					{
						issue.RmsValidationIssueId ??= Guid.NewGuid().ToString();
						issue.DepartmentId = d;
						issue.RecordId = id;
						issue.Source = (int)source;
						Issues.Add(issue);
					}
					return Task.CompletedTask;
				});

			// Submissions
			SubmissionsRepo.Setup(r => r.TryConfirmNotCreatedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, string destination, DateTime now, CancellationToken c) =>
				{
					var row = Submissions.FirstOrDefault(s => s.DepartmentId == d && s.RmsSubmissionId == id && s.RowVersion == version && s.ExternalId == null
						&& (s.LeaseExpiresOn == null || s.LeaseExpiresOn <= now) && (s.DestinationIdentity == null || s.DestinationIdentity == destination)
						&& (s.RequiresReconciliation || s.CreatePendingReceipt || s.State == (int)RmsSubmissionState.Failed || s.State == (int)RmsSubmissionState.Rejected));
					if (row == null) return false;
					row.DestinationIdentity = destination; row.RequiresReconciliation = false; row.CreatePendingReceipt = false;
					row.State = (int)RmsSubmissionState.Rejected; row.NextAttemptOn = null; row.LeaseOwner = null; row.LeaseExpiresOn = null;
					row.CompletedOn = now; row.ModifiedOn = now; row.RowVersion++;
					return true;
				});
			SubmissionsRepo.Setup(r => r.TryBindUnsentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, string destination, DateTime now, CancellationToken c) =>
				{
					var row = Submissions.FirstOrDefault(s => s.DepartmentId == d && s.RmsSubmissionId == id && s.RowVersion == version
						&& (s.LeaseExpiresOn == null || s.LeaseExpiresOn <= now) && s.DestinationIdentity == null && s.SentOn == null && s.Attempts == 0
						&& s.ExternalId == null && !s.RequiresReconciliation && !s.CreatePendingReceipt);
					if (row == null) return false;
					row.DestinationIdentity = destination; row.State = (int)RmsSubmissionState.Queued; row.NextAttemptOn = now;
					row.CompletedOn = null; row.ModifiedOn = now; row.RowVersion++;
					return true;
				});
			SubmissionsRepo.Setup(r => r.TryReconcileReceiptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, string externalId, string destination, DateTime now, CancellationToken c) =>
				{
					var row = Submissions.FirstOrDefault(s => s.DepartmentId == d && s.RmsSubmissionId == id && s.RowVersion == version
						&& (s.LeaseExpiresOn == null || s.LeaseExpiresOn <= now) && (s.RequiresReconciliation || s.CreatePendingReceipt));
					if (row == null) return false;
					row.ExternalId = externalId; row.DestinationIdentity = destination; row.RequiresReconciliation = false; row.CreatePendingReceipt = false;
					row.State = (int)RmsSubmissionState.AwaitingDestination; row.NextAttemptOn = now; row.LeaseOwner = null; row.LeaseExpiresOn = null;
					row.CompletedOn = null; row.ModifiedOn = now; row.RowVersion++;
					return true;
				});
			ExchangesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsSubmissionExchange>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSubmissionExchange e, CancellationToken c, bool f) => { Exchanges.Add(e); return e; });
			ExchangesRepo.Setup(r => r.GetForSubmissionAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Exchanges.Where(e => e.DepartmentId == d && e.SubmissionId == id).ToList());
			AnalysesRepo.Setup(r => r.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long expected, CancellationToken c) =>
				{
					var row = Analyses.FirstOrDefault(x => x.DepartmentId == d && x.RmsIncidentAnalysisId == id && x.RowVersion == expected);
					if (row == null) return false;
					row.RowVersion = expected + 1;
					return true;
				});
			SubmissionsRepo.Setup(r => r.TryFenceLeaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, long version, string owner, DateTime now, CancellationToken c) =>
				{
					var row = Submissions.FirstOrDefault(x => x.DepartmentId == d && x.RmsSubmissionId == id && x.RowVersion == version
						&& !string.IsNullOrEmpty(owner) && x.LeaseOwner == owner && x.LeaseExpiresOn > now
						&& (x.State == (int)RmsSubmissionState.Queued || x.State == (int)RmsSubmissionState.AwaitingDestination || x.State == (int)RmsSubmissionState.Failed));
					if (row == null) return false;
					row.RowVersion++;
					return true;
				});
			SubmissionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsSubmission>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSubmission e, CancellationToken c, bool f) => { Submissions.Add(e); return e; });
			SubmissionsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsSubmission>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSubmission e, CancellationToken c, bool f) => { Submissions.RemoveAll(x => x.RmsSubmissionId == e.RmsSubmissionId); Submissions.Add(e); return e; });
			SubmissionsRepo.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Submissions.FirstOrDefault(x => x.DepartmentId == d && x.RmsSubmissionId == id));
			SubmissionsRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Submissions.Where(x => x.DepartmentId == d && x.RecordId == id).OrderByDescending(x => x.QueuedOn).ToList());
			SubmissionsRepo.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>()))
				.ReturnsAsync((string key) => Submissions.FirstOrDefault(x => x.IdempotencyKey == key));
			SubmissionsRepo.Setup(r => r.CountByStateAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int state) => Submissions.Count(x => x.DepartmentId == d && x.State == state));
			SubmissionsRepo.Setup(r => r.SupersedeOpenForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string id, string except, DateTime now, CancellationToken c) =>
				{
					var open = Submissions.Where(x => x.DepartmentId == d && x.RecordId == id && x.RmsSubmissionId != except && IsOpen(x.State)).ToList();
					foreach (var row in open)
					{
						row.State = (int)RmsSubmissionState.Superseded;
						row.CompletedOn = now;
						row.ModifiedOn = now;
					}
					return open.Count;
				});
			SubmissionsRepo.Setup(r => r.ClaimDueBatchAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string owner, TimeSpan lease, int batch, DateTime now, CancellationToken c) =>
				{
					var due = Submissions
						.Where(x => (x.State == (int)RmsSubmissionState.Queued || x.State == (int)RmsSubmissionState.AwaitingDestination ||
							(x.State == (int)RmsSubmissionState.Failed && x.RequiresReconciliation && Exchanges.Any(e => e.SubmissionId == x.RmsSubmissionId && e.Stage == "Response"
								&& !Exchanges.Any(a => a.ExchangeId == e.ExchangeId && a.Stage == "Applied"))))
							&& (x.NextAttemptOn == null || x.NextAttemptOn <= now)
							&& (x.LeaseExpiresOn == null || x.LeaseExpiresOn < now))
						.OrderBy(x => x.QueuedOn).Take(batch).ToList();
					foreach (var row in due)
					{
						row.LeaseOwner = owner;
						row.LeaseExpiresOn = now.Add(lease);
						row.RowVersion++;
					}
					return due;
				});

			// Signatures
			SignaturesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsSignature>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSignature e, CancellationToken c, bool f) => { Signatures.Add(e); return e; });
			SignaturesRepo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => Signatures.Where(x => x.DepartmentId == d && x.RecordId == id).ToList());
			SignaturesRepo.Setup(r => r.GetForRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<RmsSignatureIntent>()))
				.ReturnsAsync((int d, string rev, RmsSignatureIntent intent) => Signatures.FirstOrDefault(x => x.DepartmentId == d && x.RevisionId == rev && x.Intent == (int)intent));
		}

		private IEnumerable<RmsIncidentReport> MatchReports(int departmentId, RmsIncidentReportQuery query)
		{
			var rows = Reports.Where(x => x.DepartmentId == departmentId && x.DeletedOn == null);
			if (query?.VisibleGroupIds != null) rows = rows.Where(r => r.AuthorUserId == query.ViewerUserId || r.OwnerUserId == query.ViewerUserId || r.ReviewerUserId == query.ViewerUserId
				|| Scopes.Any(s => s.DepartmentId == departmentId && s.RecordId == r.RmsIncidentReportId && query.VisibleGroupIds.Contains(s.DepartmentGroupId)));
			if (query?.States != null && query.States.Count > 0)
				rows = rows.Where(x => query.States.Contains(x.State));
			if (query?.CallId != null)
				rows = rows.Where(x => x.CallId == query.CallId.Value);
			if (!string.IsNullOrWhiteSpace(query?.OwnerUserId))
				rows = rows.Where(x => x.OwnerUserId == query.OwnerUserId);
			if (query?.StationGroupId != null)
				rows = rows.Where(x => x.StationGroupId == query.StationGroupId.Value);
			return rows;
		}

		public static bool IsOpen(int state)
		{
			return state == (int)RmsSubmissionState.Queued || state == (int)RmsSubmissionState.InFlight || state == (int)RmsSubmissionState.AwaitingDestination;
		}

		private static void Wire<T, TRepo>(Mock<TRepo> repo, List<T> rows, Func<T, string> id, Func<T, int> dept, Func<T, string> record, Func<T, string> revision)
			where T : class, IEntity
			where TRepo : class, IRmsIncidentChildRepository<T>
		{
			repo.Setup(r => r.InsertAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((T e, CancellationToken c, bool f) => { rows.Add(e); return e; });
			repo.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((T e, CancellationToken c, bool f) => { rows.RemoveAll(x => id(x) == id(e)); rows.Add(e); return e; });
			repo.Setup(r => r.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string rec, string rev) => (IEnumerable<T>)rows.Where(x => dept(x) == d && record(x) == rec && revision(x) == rev).ToList());
			repo.Setup(r => r.DeleteDraftForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string rec, CancellationToken c) => rows.RemoveAll(x => dept(x) == d && record(x) == rec && revision(x) == null));
		}
	}
}
