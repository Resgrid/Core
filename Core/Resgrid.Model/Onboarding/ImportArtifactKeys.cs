namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Object keys in the import artifact store (wizard plan section 8.7): {S3Prefix}/{departmentId}/{workspaceId}/{artifactId}.
	/// A key never contains the original file name.
	/// </summary>
	public static class ImportArtifactKeys
	{
		public static string Root
		{
			get
			{
				var prefix = (Config.ImportConfig.S3Prefix ?? string.Empty).Trim('/');
				return string.IsNullOrEmpty(prefix) ? string.Empty : prefix + "/";
			}
		}

		public static string ForArtifact(int departmentId, string workspaceId, string artifactId) =>
			$"{Root}{departmentId}/{workspaceId}/{artifactId}";

		/// <summary>Snapshot pages written by an API extraction run (section 8.8).</summary>
		public static string ForSnapshotPage(int departmentId, string workspaceId, string artifactId, int page) =>
			$"{Root}{departmentId}/{workspaceId}/{artifactId}.p{page:D6}";

		public static string ForWorkspace(int departmentId, string workspaceId) => $"{Root}{departmentId}/{workspaceId}/";

		public static string ForDepartment(int departmentId) => $"{Root}{departmentId}/";
	}

	/// <summary>Markers shared by the template writer and the analysis that recognizes a Resgrid starter workbook.</summary>
	public static class ImportTemplateMarkers
	{
		/// <summary>Trailing column of every template data sheet; rows with "x" there are examples the importer skips.</summary>
		public const string ExampleColumn = "resgrid:example";

		public const string InstructionsSheet = "Instructions";

		public const string ListsSheet = "Lists";

		/// <summary>The profile key analysis records for a sheet recognized as a Resgrid template.</summary>
		public const string TemplateProfileKey = "resgrid-template";
	}
}
