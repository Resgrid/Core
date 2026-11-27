using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>
	/// The stored lesson files of a course set copy (Training T5, round 10 A3). Hosted video and SCORM/cmi5 packages are shared, not copied:
	/// the subscriber gets its own Ready row (so every lookup, launch, runtime record and transcript stays in its department) over the
	/// publisher's stored objects, marked with <see cref="LmsMediaAsset.SharedFromDepartmentId"/> so worker 88 never archives it for the
	/// subscriber's own lapse and a future store purge never deletes objects another department's rows still name. Rows are reused across
	/// updates (same stored source). An LTI tool cannot be shared: the vendor must trust the subscriber's own issuer, keys, client id and
	/// deployment id, so the subscriber gets (or reuses) its own registration with the same endpoints, inactive until its managers register
	/// with the vendor and turn it on.
	/// </summary>
	public class LmsSetFiles : ILmsSetFiles
	{
		private readonly ILmsMediaAssetsRepository _media;
		private readonly ILmsContentPackagesRepository _packages;
		private readonly ILmsLtiToolsRepository _tools;

		public LmsSetFiles(ILmsMediaAssetsRepository media, ILmsContentPackagesRepository packages, ILmsLtiToolsRepository tools)
		{
			_media = media;
			_packages = packages;
			_tools = tools;
		}

		public async Task<string> GetProblemAsync(int publisherDepartmentId, IEnumerable<LmsLesson> lessons)
		{
			foreach (var lesson in lessons ?? Enumerable.Empty<LmsLesson>())
			{
				if (lesson.LmsMediaAssetId.HasValue)
				{
					var asset = await _media.GetByIdForDepartmentAsync(publisherDepartmentId, lesson.LmsMediaAssetId.Value);
					if (asset == null || asset.DepartmentId != publisherDepartmentId || asset.Status != (int)LmsMediaStatuses.Ready || string.IsNullOrWhiteSpace(asset.HlsPrefix))
						return LmsSetFileProblems.MediaNotReady;
				}

				if (lesson.LmsContentPackageId.HasValue)
				{
					var package = await _packages.GetByIdForDepartmentAsync(publisherDepartmentId, lesson.LmsContentPackageId.Value);
					if (package == null || package.DepartmentId != publisherDepartmentId || package.IsDeleted || package.Status != (int)LmsContentPackageStatuses.Ready)
						return LmsSetFileProblems.PackageNotReady;
				}

				if (lesson.LmsLtiToolId.HasValue)
				{
					var tool = await _tools.GetByIdForDepartmentAsync(publisherDepartmentId, lesson.LmsLtiToolId.Value);
					if (tool == null || tool.DepartmentId != publisherDepartmentId || tool.IsDeleted)
						return LmsSetFileProblems.ToolMissing;
				}
			}

			return null;
		}

		public async Task<int?> ShareVideoAsync(int publisherDepartmentId, int subscriberDepartmentId, int sourceAssetId, string userId, CancellationToken cancellationToken = default)
		{
			var source = await _media.GetByIdForDepartmentAsync(publisherDepartmentId, sourceAssetId);
			if (source == null || source.DepartmentId != publisherDepartmentId || source.Status != (int)LmsMediaStatuses.Ready || publisherDepartmentId == subscriberDepartmentId)
				return null;

			// The origin of the stored objects: a publisher's own upload, or the department it shares them from in turn.
			var origin = source.SharedFromDepartmentId ?? publisherDepartmentId;
			var existing = (await _media.GetForDepartmentAsync(subscriberDepartmentId) ?? new List<LmsMediaAsset>()).FirstOrDefault(a =>
				a.DepartmentId == subscriberDepartmentId && a.SharedFromDepartmentId == origin && a.Status == (int)LmsMediaStatuses.Ready &&
				string.Equals(a.SourceKey, source.SourceKey, StringComparison.Ordinal) && string.Equals(a.HlsPrefix, source.HlsPrefix, StringComparison.Ordinal));
			if (existing != null)
				return existing.LmsMediaAssetId;

			var now = DateTime.UtcNow;
			var shared = await _media.InsertAsync(new LmsMediaAsset
			{
				DepartmentId = subscriberDepartmentId,
				SharedFromDepartmentId = origin,
				Kind = source.Kind,
				FileName = source.FileName,
				ContentType = source.ContentType,
				SourceKey = source.SourceKey,
				SourceBytes = source.SourceBytes,
				HlsPrefix = source.HlsPrefix,
				Mp4Key = source.Mp4Key,
				OutputBytes = source.OutputBytes,
				DurationSeconds = source.DurationSeconds,
				Status = (int)LmsMediaStatuses.Ready,
				CreatedOn = now,
				CreatedByUserId = userId,
				ReadyOn = now
			}, cancellationToken, true);
			return shared?.LmsMediaAssetId;
		}

		public async Task<int?> SharePackageAsync(int publisherDepartmentId, int subscriberDepartmentId, int sourcePackageId, string userId, CancellationToken cancellationToken = default)
		{
			var source = await _packages.GetByIdForDepartmentAsync(publisherDepartmentId, sourcePackageId);
			if (source == null || source.DepartmentId != publisherDepartmentId || source.IsDeleted || source.Status != (int)LmsContentPackageStatuses.Ready ||
				publisherDepartmentId == subscriberDepartmentId)
				return null;

			var origin = source.SharedFromDepartmentId ?? publisherDepartmentId;
			var existing = (await _packages.GetForDepartmentAsync(subscriberDepartmentId) ?? new List<LmsContentPackage>()).FirstOrDefault(p =>
				p.DepartmentId == subscriberDepartmentId && p.SharedFromDepartmentId == origin && !p.IsDeleted && p.Status == (int)LmsContentPackageStatuses.Ready &&
				string.Equals(p.Sha256, source.Sha256, StringComparison.OrdinalIgnoreCase) && string.Equals(p.ContentPrefix, source.ContentPrefix, StringComparison.Ordinal));
			if (existing != null)
				return existing.LmsContentPackageId;

			var now = DateTime.UtcNow;
			var shared = await _packages.InsertAsync(new LmsContentPackage
			{
				DepartmentId = subscriberDepartmentId,
				SharedFromDepartmentId = origin,
				Standard = source.Standard,
				Title = source.Title,
				Identifier = source.Identifier,
				SchemaVersion = source.SchemaVersion,
				FileName = source.FileName,
				Sha256 = source.Sha256,
				Status = (int)LmsContentPackageStatuses.Ready,
				SourceKey = source.SourceKey,
				SourceBytes = source.SourceBytes,
				ContentPrefix = source.ContentPrefix,
				FileCount = source.FileCount,
				ContentBytes = source.ContentBytes,
				ManifestJson = source.ManifestJson,
				CreatedOn = now,
				CreatedByUserId = userId,
				ReadyOn = now
			}, cancellationToken, true);
			return shared?.LmsContentPackageId;
		}

		public async Task<LmsSetTool> PrepareToolAsync(int publisherDepartmentId, int subscriberDepartmentId, int sourceToolId, string userId, CancellationToken cancellationToken = default)
		{
			var source = await _tools.GetByIdForDepartmentAsync(publisherDepartmentId, sourceToolId);
			if (source == null || source.DepartmentId != publisherDepartmentId || source.IsDeleted || publisherDepartmentId == subscriberDepartmentId)
				return null;

			var existing = (await _tools.GetForDepartmentAsync(subscriberDepartmentId) ?? new List<LmsLtiTool>()).FirstOrDefault(t =>
				t.DepartmentId == subscriberDepartmentId && !t.IsDeleted &&
				string.Equals(t.OidcLoginUrl, source.OidcLoginUrl, StringComparison.OrdinalIgnoreCase) && string.Equals(t.LaunchUrl, source.LaunchUrl, StringComparison.OrdinalIgnoreCase));
			if (existing != null)
				return new LmsSetTool { LmsLtiToolId = existing.LmsLtiToolId, Name = existing.Name, IsActive = existing.IsActive, AllowGradePassback = existing.AllowGradePassback };

			// Fresh identifiers of the subscriber's own (the client id is unique across Resgrid); sharing a learner's name or email with the
			// vendor is the subscriber's own privacy choice, so both start off.
			var now = DateTime.UtcNow;
			var created = await _tools.InsertAsync(new LmsLtiTool
			{
				DepartmentId = subscriberDepartmentId,
				Name = source.Name,
				ClientId = Guid.NewGuid().ToString(),
				DeploymentId = Guid.NewGuid().ToString("N"),
				OidcLoginUrl = source.OidcLoginUrl,
				LaunchUrl = source.LaunchUrl,
				RedirectUris = source.RedirectUris,
				JwksUrl = source.JwksUrl,
				PublicJwksJson = source.PublicJwksJson,
				CustomParameters = source.CustomParameters,
				AllowGradePassback = source.AllowGradePassback,
				ShareName = false,
				ShareEmail = false,
				IsActive = false,
				CreatedOn = now,
				CreatedByUserId = userId
			}, cancellationToken, true);
			return created == null ? null : new LmsSetTool { LmsLtiToolId = created.LmsLtiToolId, Name = created.Name, IsActive = false, AllowGradePassback = created.AllowGradePassback, Created = true };
		}
	}
}
