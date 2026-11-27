using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T5 data exit and CE reporting (round 6D). LMS services: they consume baseline services (learning records, certifications,
	// members); baseline code never calls them (§17.3).

	/// <summary>
	/// The department's data exit (R-C12): requests (owner/admin only, audited), worker 99's build into the LMS store, and the expiring
	/// download. Never asks the add-on gate, so it works after a lapse (§17.3 rules 1–2).
	/// </summary>
	public interface ILmsDataExportService
	{
		/// <summary>Queues an export. Refused for anyone but a current department owner or admin, and while two are already queued or running.</summary>
		Task<LmsDataExitResult> RequestAsync(LmsActor actor, LmsDataExportScopes scope, bool includeMedia, CancellationToken cancellationToken = default);

		/// <summary>The department's exports, newest first (owner/admin only; empty for anyone else).</summary>
		Task<List<LmsDataExport>> GetExportsAsync(LmsActor actor);

		/// <summary>
		/// A download of a completed export before its expiry: a short-lived signed URL of the store, or the bytes kept in the database.
		/// Audited. Expired after <see cref="LmsDataExport.ExpiresOn"/>.
		/// </summary>
		Task<LmsDataExitResult> GetDownloadAsync(LmsActor actor, int lmsDataExportId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 99: requeues stale claims, builds the queued exports, removes expired zips. Due-work queries only, no gate (R-L9).</summary>
		Task<LmsDataExportRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Reads one department's rows for an export (department scoped; the media list is filled by the build).</summary>
		Task<LmsDataExportSnapshot> LoadSnapshotAsync(int departmentId, LmsDataExportScopes scope, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// CE reporting files (§13a "CE and state reporting"): the CAPCE completion file, state CE files and the state-ready roster, with the
	/// department's settings (setting 148) and column maps. Files only — nothing is submitted anywhere.
	/// </summary>
	public interface ILmsCeExportService
	{
		IReadOnlyList<LmsCeExporter> GetExporters();

		Task<LmsCeReportingSettings> GetSettingsAsync(int departmentId);

		/// <summary>Saves the settings (department admins with the add-on active, lapse rule 3). Audited.</summary>
		Task<LmsCeReportingSettings> SaveSettingsAsync(LmsActor actor, LmsCeReportingSettings settings, CancellationToken cancellationToken = default);

		/// <summary>Replaces one exporter's columns, or restores its default columns when <paramref name="columns"/> is null or empty. Audited.</summary>
		Task<LmsCeReportingSettings> SaveColumnsAsync(LmsActor actor, string exporterKey, IEnumerable<LmsCeColumn> columns, CancellationToken cancellationToken = default);

		/// <summary>
		/// Builds one file for a department-local date range. Training officers (department admins and department-wide records managers) while
		/// the add-on is active or lapsed: records stay exportable after a lapse (rule 2). Audited when <paramref name="audit"/> (downloads).
		/// </summary>
		Task<LmsCeExportFile> BuildAsync(LmsActor actor, bool isRecordsManager, LmsCeExportRequest request, bool audit, CancellationToken cancellationToken = default);

		/// <summary>Whether the person may build CE files (officer, and the add-on active or lapsed; never while the LMS is dark).</summary>
		bool CanBuild(LmsActor actor, bool isRecordsManager);

		/// <summary>Whether the person may change the CE settings (department admin with the add-on active).</summary>
		bool CanConfigure(LmsActor actor);
	}
}
