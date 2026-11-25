using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Building-familiarization credit (§13a.1, "Not in Vector"; LMS add-on): completed Records occupancy inspections and occupancy /
	/// contact pre-plan reviews credit the person who did them, in the department's chosen category (e.g. the ISO pre-fire planning
	/// category), through ILearningRecordWriter as <see cref="LearningRecordSourceKinds.BuildingFamiliarization"/> records: one per visit,
	/// idempotent by the visit, voided when an inspection is cancelled or deleted. Worker 94 reads only departments that turned it on,
	/// asks the LMS gate once per such department, and moves a per-department watermark through the changed visits.
	/// </summary>
	public class LmsFamiliarizationCreditService : ILmsFamiliarizationCreditService
	{
		public const int PageSize = 500;

		/// <summary>The first run looks back this far (the visits Resgrid already holds), never further.</summary>
		public static readonly TimeSpan FirstRunLookBack = TimeSpan.FromDays(400);

		/// <summary>Each run re-reads a little before the watermark (late commits); the writer keeps it idempotent.</summary>
		public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(10);

		private readonly ILmsFamiliarizationSourceRepository _sources;
		private readonly ILmsProgramStatesRepository _states;
		private readonly ILmsProgramSettingsService _settings;
		private readonly ILearningRecordWriter _writer;
		private readonly IDepartmentsService _departments;
		private readonly ILmsAccessService _access;

		public LmsFamiliarizationCreditService(ILmsFamiliarizationSourceRepository sources, ILmsProgramStatesRepository states, ILmsProgramSettingsService settings,
			ILearningRecordWriter writer, IDepartmentsService departments, ILmsAccessService access)
		{
			_sources = sources;
			_states = states;
			_settings = settings;
			_writer = writer;
			_departments = departments;
			_access = access;
		}

		public async Task<LmsFamiliarizationRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsFamiliarizationRunResult();
			foreach (var departmentId in await _states.GetDepartmentsWithProgramSettingsAsync() ?? new List<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var settings = await _settings.GetAsync(departmentId);
					if (!settings.FamiliarizationEnabled || settings.FamiliarizationCategoryId <= 0)
						continue;
					if (!await _access.CanAuthorAndManageAsync(departmentId))
						continue;

					var state = await _states.GetForDepartmentAsync(departmentId);
					var since = state?.FamiliarizationScannedThrough.HasValue == true ? state.FamiliarizationScannedThrough.Value - Overlap : utcNow - FirstRunLookBack;
					var one = await CreditAsync(departmentId, settings, since, utcNow, cancellationToken);
					result.Departments++;
					result.Written += one.Written;
					result.Voided += one.Voided;
					result.Unchanged += one.Unchanged;
					result.Failures += one.Failures;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Failures++;
					Logging.LogException(ex, $"Building familiarization credit failed for department {departmentId}.");
				}
			}

			return result;
		}

		public async Task<LmsFamiliarizationRunResult> CreditAsync(int departmentId, LmsProgramSettings settings, DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken = default)
		{
			var result = new LmsFamiliarizationRunResult { Departments = 1 };
			settings = (settings ?? new LmsProgramSettings()).Normalized();
			if (!settings.FamiliarizationEnabled || settings.FamiliarizationCategoryId <= 0 || untilUtc <= sinceUtc)
				return result;

			var visits = new List<LmsFamiliarizationVisit>();
			var through = untilUtc;
			if (settings.FamiliarizationFromInspections)
				through = Min(through, await ReadAsync(visits, () => _sources.GetInspectionsChangedAsync(departmentId, sinceUtc, untilUtc, PageSize)));
			if (settings.FamiliarizationFromPreplanReviews)
			{
				through = Min(through, await ReadAsync(visits, () => _sources.GetOccupancyReviewsAsync(departmentId, sinceUtc, untilUtc, PageSize)));
				through = Min(through, await ReadAsync(visits, () => _sources.GetPreplanReviewsAsync(departmentId, sinceUtc, untilUtc, PageSize)));
			}

			// A member who is no longer current earns nothing new (their old records stay); a withdrawn visit voids its record.
			var current = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var visit in visits.Where(v => v.ModifiedOn <= through).GroupBy(v => v.SourceRefId, StringComparer.Ordinal).Select(g => g.OrderBy(v => v.ModifiedOn).Last()))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					LearningRecordWriteResult written;
					if (visit.Withdrawn || string.IsNullOrWhiteSpace(visit.UserId))
						written = await _writer.VoidSourceAsync(departmentId, LearningRecordSourceKinds.BuildingFamiliarization, visit.SourceRefId, "familiarization_visit_withdrawn", null, cancellationToken);
					else if (!current.Contains(visit.UserId))
						continue;
					else
						written = await _writer.WriteSourceAsync(departmentId, LearningRecordSourceKinds.BuildingFamiliarization, visit.SourceRefId, new[] { Draft(visit, settings) }, null, cancellationToken);

					result.Written += written?.Written ?? 0;
					result.Voided += written?.Voided ?? 0;
					result.Unchanged += written?.Unchanged ?? 0;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Failures++;
					Logging.LogException(ex, $"Building familiarization credit for {visit.SourceRefId} in department {departmentId} failed.");
				}
			}

			if (result.Failures == 0)
				await _states.SetFamiliarizationScannedThroughAsync(departmentId, through, cancellationToken);
			return result;
		}

		/// <summary>The ledger entry of one visit: the visit's own duration when it has a sensible one, else the configured minutes.</summary>
		public static LearningRecordDraft Draft(LmsFamiliarizationVisit visit, LmsProgramSettings settings)
		{
			var minutes = (decimal)settings.FamiliarizationMinutes;
			if (visit.StartedOn.HasValue && visit.CompletedOn > visit.StartedOn.Value)
			{
				var actual = (decimal)(visit.CompletedOn - visit.StartedOn.Value).TotalMinutes;
				if (actual >= 5m && actual <= 12m * 60m)
					minutes = actual;
			}

			return new LearningRecordDraft
			{
				UserId = visit.UserId,
				Title = visit.Kind == "Inspection" ? "Building familiarization: inspection " + (visit.Reference ?? string.Empty)
					: visit.Kind == "OccupancyReview" ? "Building familiarization: pre-plan review " + (visit.Reference ?? string.Empty)
					: "Building familiarization: pre-plan review",
				CompletedOn = visit.CompletedOn,
				StartedOn = visit.StartedOn,
				EndedOn = visit.StartedOn.HasValue ? visit.CompletedOn : (DateTime?)null,
				Hours = Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero),
				CategoryId = settings.FamiliarizationCategoryId,
				MethodOfTraining = (int)MethodsOfTraining.OnTheJob,
				ExternalId = visit.SourceRefId,
				Status = LearningRecordStatuses.Verified
			};
		}

		/// <summary>Reads one source page; returns how far it is complete (the last change read when the page was full, else the window end).</summary>
		private static async Task<DateTime> ReadAsync(List<LmsFamiliarizationVisit> into, Func<Task<List<LmsFamiliarizationVisit>>> read)
		{
			var page = await read() ?? new List<LmsFamiliarizationVisit>();
			into.AddRange(page);
			return page.Count >= PageSize ? page.Max(v => v.ModifiedOn) : DateTime.MaxValue;
		}

		private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;
	}
}
