using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>Shared text helpers for the authoring aid sources.</summary>
	public static class AuthoringAidText
	{
		/// <summary>Stored rich text as plain lines (block tags start a new line).</summary>
		public static string FromHtml(string html) => string.IsNullOrWhiteSpace(html) ? string.Empty : StringHelpers.HtmlToMessageText(html);

		/// <summary>Plain lines as paragraphs, so the version diff splits them as it splits stored HTML.</summary>
		public static string AsParagraphs(string text) =>
			string.Concat((text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => "<p>" + WebUtility.HtmlEncode(l) + "</p>"));
	}

	/// <summary>
	/// Protocol versions (P1) as an authoring aid source: the member must author protocols (department admin or Author Protocols, 128); a
	/// protocol marked "no AI" or licensed is excluded (decision 14). The grounding is the version's text, or its extracted file text for
	/// a document-backed version; a change summary gets only the diff against the version before it (P1's redline).
	/// </summary>
	public sealed class ProtocolAuthoringAidSource : IAuthoringAidSource
	{
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IKnowledgePageTextSource _pages;

		public ProtocolAuthoringAidSource(IProtocolAccessService access, IProtocolLibraryService library, IKnowledgePageTextSource pages)
		{
			_access = access;
			_library = library;
			_pages = pages;
		}

		public AuthoringAidSourceKinds Kind => AuthoringAidSourceKinds.ProtocolVersion;

		public async Task<AuthoringAidSourceResult> LoadAsync(AuthoringAidRequest request, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(request.DepartmentId, request.UserId);
			if (reader == null || !reader.MayAuthor)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotAllowed);

			var version = await _library.GetVersionAsync(request.DepartmentId, request.SourceId);
			if (version == null || version.DepartmentId != request.DepartmentId)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotFound);
			var protocol = await _access.GetReadableAsync(request.DepartmentId, request.UserId, version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != request.DepartmentId)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotFound);

			var source = new AuthoringAidSource
			{
				Title = protocol.Name,
				VersionLabel = version.Label,
				ItemKey = "protocol-version:" + version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
				ExcludedFromAi = protocol.ExcludeFromAi || protocol.IsLicensedContent
			};
			if (source.ExcludedFromAi)
				return AuthoringAidSourceResult.Ok(source);

			source.Text = await TextOfAsync(request.DepartmentId, version, cancellationToken);
			if (request.Kind == (int)AuthoringAidKinds.ChangeSummary)
			{
				var previous = await PreviousAsync(request.DepartmentId, version);
				if (previous != null)
				{
					source.PreviousVersionLabel = previous.Label;
					var before = version.IsDocument || previous.IsDocument
						? AuthoringAidText.AsParagraphs(await TextOfAsync(request.DepartmentId, previous, cancellationToken)) : previous.ContentHtml;
					var after = version.IsDocument || previous.IsDocument ? AuthoringAidText.AsParagraphs(source.Text) : version.ContentHtml;
					source.DiffText = AuthoringAidDiffText.From(ProtocolVersionDiff.Compare(before, after));
				}
			}

			return AuthoringAidSourceResult.Ok(source);
		}

		private async Task<string> TextOfAsync(int departmentId, ProtocolVersion version, CancellationToken cancellationToken)
		{
			if (!version.IsDocument)
				return AuthoringAidText.FromHtml(version.ContentHtml);

			var pages = await _pages.GetPagesAsync(departmentId, KnowledgeSourceKinds.ProtocolFile, version.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
				version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), cancellationToken) ?? new List<KnowledgePageText>();
			var text = new StringBuilder();
			foreach (var page in pages.Where(p => p.AttachmentId != null && p.AttachmentId.StartsWith("file-", StringComparison.Ordinal)).OrderBy(p => p.PageNumber))
				text.Append(page.Text).Append('\n');
			return text.ToString().Trim();
		}

		/// <summary>
		/// The version a change summary compares against: for a version not yet in force, the one in force (else the last superseded); for a
		/// published or superseded version, the latest one in force before it.
		/// </summary>
		private async Task<ProtocolVersion> PreviousAsync(int departmentId, ProtocolVersion version)
		{
			var headers = (await _library.GetVersionsAsync(departmentId, version.DispatchProtocolId) ?? new List<ProtocolVersion>())
				.Where(v => v.ProtocolVersionId != version.ProtocolVersionId && v.DepartmentId == departmentId &&
					(v.Status == (int)ProtocolVersionStatuses.Published || v.Status == (int)ProtocolVersionStatuses.Superseded))
				.ToList();
			var inForce = version.Status == (int)ProtocolVersionStatuses.Published || version.Status == (int)ProtocolVersionStatuses.Superseded;
			var candidate = headers
				.Where(v => !inForce || v.Major < version.Major || v.Major == version.Major && v.Minor < version.Minor)
				.OrderByDescending(v => v.Status == (int)ProtocolVersionStatuses.Published && !inForce)
				.ThenByDescending(v => v.Major).ThenByDescending(v => v.Minor).ThenByDescending(v => v.ProtocolVersionId)
				.FirstOrDefault();
			return candidate == null ? null : await _library.GetVersionAsync(departmentId, candidate.ProtocolVersionId);
		}
	}

	/// <summary>
	/// Baseline trainings as an authoring aid source: an existing training the member may author (its stored text, or the editor's unsaved
	/// text), or the New Training editor's unsaved text for a member who may create trainings. A training marked "no AI" (stored or in the
	/// editor) is excluded. Trainings have no versions, so there is no change summary.
	/// </summary>
	public sealed class TrainingAuthoringAidSource : IAuthoringAidSource
	{
		private readonly ITrainingService _trainings;
		private readonly IAuthorizationService _authorization;

		public TrainingAuthoringAidSource(ITrainingService trainings, IAuthorizationService authorization)
		{
			_trainings = trainings;
			_authorization = authorization;
		}

		public AuthoringAidSourceKinds Kind => AuthoringAidSourceKinds.Training;

		public async Task<AuthoringAidSourceResult> LoadAsync(AuthoringAidRequest request, CancellationToken cancellationToken = default)
		{
			if (request.Kind == (int)AuthoringAidKinds.ChangeSummary)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NoChanges);

			var scope = await _authorization.GetTrainingManagementScopeAsync(request.UserId, request.DepartmentId);
			if (scope == null)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotAllowed);

			var unsaved = !string.IsNullOrWhiteSpace(request.ContentHtml);
			if (request.SourceId <= 0)
			{
				if (!scope.CanCreate || !unsaved)
					return AuthoringAidSourceResult.Fail(scope.CanCreate ? AuthoringAidOutcomes.NoContent : AuthoringAidOutcomes.NotAllowed);
				return AuthoringAidSourceResult.Ok(new AuthoringAidSource
				{
					Title = request.ContentTitle,
					Text = AuthoringAidText.FromHtml(request.ContentHtml),
					ItemKey = "training:new",
					ExcludedFromAi = request.ExcludeFromAi
				});
			}

			var training = await _trainings.GetTrainingByIdAsync(request.SourceId);
			if (training == null || training.DepartmentId != request.DepartmentId)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotFound);
			if (!scope.CanAuthor(training))
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotAllowed);

			return AuthoringAidSourceResult.Ok(new AuthoringAidSource
			{
				Title = unsaved && !string.IsNullOrWhiteSpace(request.ContentTitle) ? request.ContentTitle : training.Name,
				Text = AuthoringAidText.FromHtml(unsaved ? request.ContentHtml : training.TrainingText),
				ItemKey = "training:" + training.TrainingId.ToString(CultureInfo.InvariantCulture),
				ExcludedFromAi = training.ExcludeFromAi || request.ExcludeFromAi
			});
		}
	}
}
