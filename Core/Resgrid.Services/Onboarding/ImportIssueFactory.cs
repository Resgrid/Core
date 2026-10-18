using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Builds ladder issues (wizard plan section 9). Issues carry column names, row numbers and codes, never cell values;
	/// their identity is (dataset, row, column, code), so re-running a level is idempotent and an administrator's decision
	/// on an issue (accepted, excluded) survives the re-run.
	/// </summary>
	public static class ImportIssueFactory
	{
		/// <summary>Codes that block approval whatever the field (file safety, parse, uniqueness, references, accounts).</summary>
		private static readonly HashSet<string> AlwaysBlocking = new HashSet<string>(StringComparer.Ordinal)
		{
			"IMP-PARSE-COLUMNS", "IMP-DUP-IN-FILE", "IMP-DUP-SOURCE-ID", "IMP-HIERARCHY-CYCLE", "IMP-REQUIRED-MISSING", "IMP-REF-UNRESOLVED",
			"IMP-REF-AMBIGUOUS", "IMP-FILE-CONTROL-CHAR", "IMP-TYPE-DATE-AMBIGUOUS", "IMP-ROW-EXPIRES-BEFORE-ISSUED", "IMP-ROW-COORDINATES-PAIR",
			"IMP-PLAN-CAPACITY", "IMP-ENTITLEMENT-MISSING", "IMP-CONSEQUENCE-UNDECIDED", "IMP-MAP-REQUIRED-UNMAPPED", "IMP-DETECT-LOW-CONFIDENCE"
		};

		public static string IdentityKey(string datasetId, int? rowNumber, string column, string code)
		{
			var text = $"{datasetId}|{rowNumber?.ToString() ?? "-"}|{column ?? "-"}|{code}";
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
		}

		public static ImportIssueSeverity SeverityFor(string code, bool requiredField)
		{
			if (AlwaysBlocking.Contains(code) || requiredField)
				return ImportIssueSeverity.Blocking;
			if (code == "IMP-MATCH-EXISTING" || code == "IMP-EXCLUDED-SENSITIVE")
				return ImportIssueSeverity.Info;
			return ImportIssueSeverity.Warning;
		}

		public static ImportIssue Create(ImportDataset dataset, ImportStagedRow row, string column, string fieldKey, string code, ImportLadderLevel level,
			ImportIssueSeverity severity, IDictionary<string, string> parameters = null)
		{
			var now = DateTime.UtcNow;
			return new ImportIssue
			{
				DepartmentId = dataset.DepartmentId,
				WorkspaceId = dataset.WorkspaceId,
				DatasetId = dataset.Id,
				ArtifactId = dataset.ArtifactId,
				StagedRowId = row?.Id,
				RowNumber = row?.RowNumber,
				ColumnName = Truncate(column, 255),
				FieldKey = fieldKey,
				EntityKey = dataset.EntityKey,
				Code = code,
				Severity = (int)severity,
				Level = (int)level,
				Parameters = parameters == null || parameters.Count == 0 ? null : JsonConvert.SerializeObject(parameters),
				IdentityKey = IdentityKey(dataset.Id, row?.RowNumber, column ?? fieldKey, code),
				Status = (int)ImportIssueStatus.Open,
				CreatedOn = now,
				UpdatedOn = now
			};
		}

		/// <summary>Completes an issue a writer raised while planning (L7/L8) with its dataset and stable identity.</summary>
		public static ImportIssue ForPlan(ImportDataset dataset, ImportIssue partial)
		{
			var issue = Create(dataset, null, null, partial.FieldKey, partial.Code, (ImportLadderLevel)partial.Level, (ImportIssueSeverity)partial.Severity);
			issue.StagedRowId = partial.StagedRowId;
			issue.RowNumber = partial.RowNumber;
			issue.Parameters = partial.Parameters;
			issue.IdentityKey = IdentityKey(dataset.Id, partial.RowNumber, partial.FieldKey, partial.Code);
			return issue;
		}

		/// <summary>A file-level (L0) issue on an artifact before any dataset exists.</summary>
		public static ImportIssue ForArtifact(ImportArtifact artifact, string code)
		{
			var now = DateTime.UtcNow;
			return new ImportIssue
			{
				DepartmentId = artifact.DepartmentId,
				WorkspaceId = artifact.WorkspaceId,
				ArtifactId = artifact.Id,
				Code = code,
				Severity = (int)ImportIssueSeverity.Blocking,
				Level = (int)ImportLadderLevel.L0FileSafety,
				IdentityKey = IdentityKey("artifact:" + artifact.Id, null, null, code),
				Status = (int)ImportIssueStatus.Open,
				CreatedOn = now,
				UpdatedOn = now
			};
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
