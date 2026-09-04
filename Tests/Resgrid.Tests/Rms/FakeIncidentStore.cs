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
		public List<RmsSignature> Signatures { get; } = new List<RmsSignature>();

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
		public Mock<IRmsSignaturesRepository> SignaturesRepo { get; } = new Mock<IRmsSignaturesRepository>();

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
			ReportsRepo.Setup(r => r.QueryAsync(It.IsAny<int>(), It.IsAny<RmsIncidentReportQuery>()))
				.ReturnsAsync((int d, RmsIncidentReportQuery q) => Reports.Where(x => x.DepartmentId == d && x.DeletedOn == null).ToList());
			ReportsRepo.Setup(r => r.CountAsync(It.IsAny<int>(), It.IsAny<RmsIncidentReportQuery>()))
				.ReturnsAsync((int d, RmsIncidentReportQuery q) => Reports.Count(x => x.DepartmentId == d && x.DeletedOn == null));
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
			SubmissionsRepo.Setup(r => r.CountByStateAsync(It.IsAny<int>()))
				.ReturnsAsync((int state) => Submissions.Count(x => x.State == state));
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
						.Where(x => (x.State == (int)RmsSubmissionState.Queued || x.State == (int)RmsSubmissionState.AwaitingDestination)
							&& (x.NextAttemptOn == null || x.NextAttemptOn <= now)
							&& (x.LeaseExpiresOn == null || x.LeaseExpiresOn < now))
						.OrderBy(x => x.QueuedOn).Take(batch).ToList();
					foreach (var row in due)
					{
						row.LeaseOwner = owner;
						row.LeaseExpiresOn = now.Add(lease);
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
