using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The Records work queues and the crosswalk gap report (RMS plan RMS-3).
	/// <para>
	/// Each count is produced independently and a failure degrades to a warning rather than an exception. A
	/// dashboard that shows nine correct numbers and says the tenth is unavailable is useful; one that throws
	/// because the disclosure table is missing is not.
	/// </para>
	/// </summary>
	public class RecordsDashboardService : IRecordsDashboardService
	{
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidentReports;
		private readonly IRmsIncidentAnalysesRepository _analyses;
		private readonly IRmsRecordDueStatesRepository _dueStates;
		private readonly IRmsDisclosureRequestsRepository _disclosures;
		private readonly INerisProfileService _neris;
		private readonly ICallsService _calls;
		private readonly IRecordsAuthorizationService _authorization;

		public RecordsDashboardService(IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository incidentReports,
			IRmsIncidentAnalysesRepository analyses, IRmsRecordDueStatesRepository dueStates, IRmsDisclosureRequestsRepository disclosures,
			INerisProfileService neris, ICallsService calls, IRecordsAuthorizationService authorization)
		{
			_records = records;
			_incidentReports = incidentReports;
			_analyses = analyses;
			_dueStates = dueStates;
			_disclosures = disclosures;
			_neris = neris;
			_calls = calls;
			_authorization = authorization;
		}

		public async Task<RecordsDashboard> GetAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var dashboard = new RecordsDashboard();
			var now = DateTime.UtcNow;
			if (!await _authorization.IsActiveMemberAsync(userId, departmentId)) throw new UnauthorizedAccessException();
			var visible = (await _authorization.GetVisibleGroupIdsAsync(userId, departmentId))?.ToList();

			await SafeAsync(dashboard, "operational queues", async () =>
			{
				dashboard.OperationalDrafts = await _records.CountVisibleAsync(departmentId, new[] { (int)RmsRecordState.Draft }, visible, userId);
				dashboard.OperationalAwaitingReview = await _records.CountVisibleAsync(departmentId, new[] { (int)RmsRecordState.ReadyForReview }, visible, userId);
				dashboard.OperationalReturned = await _records.CountVisibleAsync(departmentId, new[] { (int)RmsRecordState.Returned }, visible, userId);
			});

			await SafeAsync(dashboard, "incident report queues", async () =>
			{
				dashboard.IncidentIncomplete = await CountReportsAsync(departmentId, visible, userId, RmsRecordState.Draft, RmsRecordState.Returned);
				dashboard.IncidentAwaitingReview = await CountReportsAsync(departmentId, visible, userId, RmsRecordState.ReadyForReview, RmsRecordState.Approved);
				dashboard.IncidentSubmitted = await CountReportsAsync(departmentId, visible, userId, RmsRecordState.Submitted);
				dashboard.IncidentAccepted = await CountReportsAsync(departmentId, visible, userId, RmsRecordState.Accepted);
				dashboard.IncidentRejected = await CountReportsAsync(departmentId, visible, userId, RmsRecordState.Rejected);
			});

			await SafeAsync(dashboard, "overdue obligations", async () =>
			{
				dashboard.Overdue = await _dueStates.CountVisibleOverdueAsync(departmentId, visible, userId);
			});

			await SafeAsync(dashboard, "incident analyses", async () =>
			{
				dashboard.AnalysesAwaitingFiling = await _analyses.CountVisibleByStateAsync(departmentId, RmsIncidentAnalysisState.Finalized, visible, userId);
			});

			await SafeAsync(dashboard, "disclosure requests", async () =>
			{
				if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures)) return;
				var open = 0;
				foreach (var state in new[] { RmsDisclosureState.Received, RmsDisclosureState.Scoping, RmsDisclosureState.InReview, RmsDisclosureState.Produced })
					open += await _disclosures.CountByStateAsync(departmentId, state);

				dashboard.DisclosuresOpen = open;
				dashboard.DisclosuresOverdue = await _disclosures.CountOverdueAsync(departmentId, now);
			});

			var currentScope = await _authorization.GetVisibleGroupIdsAsync(userId, departmentId);
			if (!await _authorization.IsActiveMemberAsync(userId, departmentId)
				|| (visible == null) != (currentScope == null) || visible != null && !visible.ToHashSet().SetEquals(currentScope))
				throw new UnauthorizedAccessException("Record access changed while the dashboard was loading. Reload the dashboard.");
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures))
			{ dashboard.DisclosuresOpen = 0; dashboard.DisclosuresOverdue = 0; }
			return dashboard;
		}

		public async Task<NerisCrosswalkCoverage> GetCrosswalkCoverageAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var coverage = new NerisCrosswalkCoverage { ContractVersion = _neris.ContractVersion };

			List<RmsNerisCrosswalk> crosswalks;
			try
			{
				crosswalks = await _neris.GetCrosswalksAsync(departmentId) ?? new List<RmsNerisCrosswalk>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Crosswalk coverage could not read the department {departmentId} crosswalks.");
				coverage.Warnings.Add("The department's crosswalks could not be read.");
				return coverage;
			}

			var mapped = crosswalks
				.Where(c => c != null && string.Equals(c.SetKey, "incident_type", StringComparison.Ordinal) && string.Equals(c.LocalSource, NerisCrosswalkSources.CallType, StringComparison.Ordinal))
				.GroupBy(c => c.LocalCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().NerisCode, StringComparer.OrdinalIgnoreCase);

			List<CallType> callTypes;
			try
			{
				callTypes = await _calls.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Crosswalk coverage could not read the department {departmentId} call types.");
				coverage.Warnings.Add("The department's call types could not be read.");
				return coverage;
			}

			var incidentTypes = _neris.GetValueSet("incident_type");
			var known = incidentTypes?.Codes == null
				? null
				: new HashSet<string>(incidentTypes.Codes, StringComparer.Ordinal);

			foreach (var callType in callTypes.Where(t => !string.IsNullOrWhiteSpace(t?.Type)).OrderBy(t => t.Type, StringComparer.OrdinalIgnoreCase))
			{
				mapped.TryGetValue(callType.Type, out var nerisCode);
				coverage.Items.Add(new NerisCrosswalkCoverageItem
				{
					SetKey = "incident_type",
					LocalCode = callType.Type,
					NerisCode = nerisCode
				});

				coverage.TotalLocalCodes++;
				if (string.IsNullOrWhiteSpace(nerisCode))
				{
					coverage.UnmappedCount++;
					continue;
				}

				coverage.MappedCount++;

				// A mapping to a code the pinned contract no longer carries looks configured and fails at
				// submission. Counting it separately is the difference between a gap report and a false all-clear.
				if (known != null && !known.Contains(nerisCode))
					coverage.StaleMappingCount++;
			}

			return coverage;
		}

		private async Task<int> CountReportsAsync(int departmentId, List<int> visible, string userId, params RmsRecordState[] states)
		{
			return await _incidentReports.CountAsync(departmentId, new RmsIncidentReportQuery
			{
				States = states.Select(s => (int)s).ToList(),
				VisibleGroupIds = visible, ViewerUserId = userId,
				Take = 1
			});
		}

		/// <summary>One failed count degrades to a warning; the rest of the dashboard still renders.</summary>
		private static async Task SafeAsync(RecordsDashboard dashboard, string what, Func<Task> work)
		{
			try
			{
				await work();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Records dashboard could not produce {what}.");
				dashboard.Warnings.Add($"The {what} count is unavailable.");
			}
		}
	}
}
