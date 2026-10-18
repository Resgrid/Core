using Resgrid.Model.Onboarding;

namespace Resgrid.Web.Areas.User.Models.DepartmentImport
{
	/// <summary>
	/// One status chip per lifecycle (wizard plan section 6.6): the Records colour semantics (default = draft, info = in
	/// progress, primary = active, success = complete, warning = attention, danger = blocked) plus an icon and a
	/// localization key, so colour never carries the meaning alone.
	/// </summary>
	public static class ImportBadges
	{
		public static (string Css, string Icon, string Key) Workspace(int status) => (ImportWorkspaceStatus)status switch
		{
			ImportWorkspaceStatus.Draft => ("label-default", "fa-pencil", "WorkspaceStatus.Draft"),
			ImportWorkspaceStatus.Collecting => ("label-info", "fa-upload", "WorkspaceStatus.Collecting"),
			ImportWorkspaceStatus.Reviewing => ("label-warning", "fa-search", "WorkspaceStatus.Reviewing"),
			ImportWorkspaceStatus.Importing => ("label-info", "fa-cog", "WorkspaceStatus.Importing"),
			ImportWorkspaceStatus.Verifying => ("label-info", "fa-check-square-o", "WorkspaceStatus.Verifying"),
			ImportWorkspaceStatus.ReadyToActivate => ("label-primary", "fa-flag-checkered", "WorkspaceStatus.ReadyToActivate"),
			ImportWorkspaceStatus.Active => ("label-success", "fa-check", "WorkspaceStatus.Active"),
			ImportWorkspaceStatus.Closed => ("label-default", "fa-archive", "WorkspaceStatus.Closed"),
			ImportWorkspaceStatus.Abandoned => ("label-default", "fa-clock-o", "WorkspaceStatus.Abandoned"),
			ImportWorkspaceStatus.Cancelled => ("label-default", "fa-ban", "WorkspaceStatus.Cancelled"),
			_ => ("label-default", "fa-question", "Status.Unknown")
		};

		public static (string Css, string Icon, string Key) Dataset(int status) => (ImportDatasetStatus)status switch
		{
			ImportDatasetStatus.Uploading => ("label-info", "fa-upload", "DatasetStatus.Uploading"),
			ImportDatasetStatus.Scanning => ("label-info", "fa-shield", "DatasetStatus.Scanning"),
			ImportDatasetStatus.Analyzing => ("label-info", "fa-cog", "DatasetStatus.Analyzing"),
			ImportDatasetStatus.NeedsMapping => ("label-warning", "fa-columns", "DatasetStatus.NeedsMapping"),
			ImportDatasetStatus.NeedsAttention => ("label-warning", "fa-exclamation-triangle", "DatasetStatus.NeedsAttention"),
			ImportDatasetStatus.Ready => ("label-primary", "fa-thumbs-up", "DatasetStatus.Ready"),
			ImportDatasetStatus.Approved => ("label-primary", "fa-check-circle-o", "DatasetStatus.Approved"),
			ImportDatasetStatus.Importing => ("label-info", "fa-cog", "DatasetStatus.Importing"),
			ImportDatasetStatus.Imported => ("label-success", "fa-check", "DatasetStatus.Imported"),
			ImportDatasetStatus.Verified => ("label-success", "fa-check-square-o", "DatasetStatus.Verified"),
			ImportDatasetStatus.Excluded => ("label-default", "fa-eye-slash", "DatasetStatus.Excluded"),
			ImportDatasetStatus.Superseded => ("label-default", "fa-files-o", "DatasetStatus.Superseded"),
			ImportDatasetStatus.Failed => ("label-danger", "fa-times-circle", "DatasetStatus.Failed"),
			_ => ("label-default", "fa-question", "Status.Unknown")
		};

		public static (string Css, string Icon, string Key) Run(int status) => (ImportRunStatus)status switch
		{
			ImportRunStatus.Queued => ("label-default", "fa-hourglass-start", "RunStatus.Queued"),
			ImportRunStatus.Running => ("label-info", "fa-cog", "RunStatus.Running"),
			ImportRunStatus.Paused => ("label-warning", "fa-pause", "RunStatus.Paused"),
			ImportRunStatus.Reconciling => ("label-info", "fa-check-square-o", "RunStatus.Reconciling"),
			ImportRunStatus.Completed => ("label-success", "fa-check", "RunStatus.Completed"),
			ImportRunStatus.Failed => ("label-danger", "fa-refresh", "RunStatus.Failed"),
			ImportRunStatus.Cancelled => ("label-default", "fa-ban", "RunStatus.Cancelled"),
			ImportRunStatus.AwaitingChecker => ("label-warning", "fa-user-plus", "RunStatus.AwaitingChecker"),
			_ => ("label-default", "fa-question", "Status.Unknown")
		};

		public static (string Css, string Icon, string Key) Severity(int severity) => (ImportIssueSeverity)severity switch
		{
			ImportIssueSeverity.Blocking => ("label-danger", "fa-ban", "Severity.Blocking"),
			ImportIssueSeverity.Warning => ("label-warning", "fa-exclamation-triangle", "Severity.Warning"),
			_ => ("label-info", "fa-info-circle", "Severity.Info")
		};

		public static string RunKindKey(int kind) => "RunKind." + (ImportRunKind)kind;
	}
}
