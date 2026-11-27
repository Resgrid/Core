using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Members as the exports name them: every member the department ever had (current, former and hidden), plus anyone a record names.</summary>
	public static class LmsExportMembers
	{
		/// <summary>
		/// The members, with the DEPARTMENT-SCOPED identification number (ADP plan 5.1; never the legacy global profile column). It goes through
		/// the protected read pipeline: a protected department's number is REDACTED unless the caller holds a grant, and an unattended export
		/// (worker 99) never holds one.
		/// </summary>
		public static async Task<List<LmsExportMember>> LoadAsync(IDepartmentsService departments, IUserProfileService profiles, IDepartmentMemberSensitiveDataService sensitiveData,
			int departmentId, IEnumerable<string> alsoNamed, string grantToken, string actingUserId)
		{
			var memberships = (await departments.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>())
				.Where(m => m?.UserId != null && m.DepartmentId == departmentId)
				.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase)
				// A member removed and added again has two rows: the current one decides.
				.ToDictionary(g => g.Key, g => g.OrderByDescending(m => DepartmentMemberStateHelper.IsCurrentMember(m, departmentId)).First(), StringComparer.OrdinalIgnoreCase);

			var byId = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
			foreach (var (userId, profile) in await profiles.GetAllProfilesForDepartmentIncDisabledDeletedAsync(departmentId) ?? new Dictionary<string, UserProfile>())
				if (userId != null && profile != null)
					byId[userId] = profile;

			var emails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var user in await departments.GetAllUsersForDepartmentUnlimitedAsync(departmentId) ?? new List<IdentityUser>())
				if (user?.Id != null && !string.IsNullOrWhiteSpace(user.Email))
					emails[user.Id] = user.Email;

			var ids = new HashSet<string>(memberships.Keys, StringComparer.OrdinalIgnoreCase);
			foreach (var id in alsoNamed ?? Enumerable.Empty<string>())
				if (!string.IsNullOrWhiteSpace(id))
					ids.Add(id.Trim());

			var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();
			if (missing.Count > 0)
				foreach (var profile in await profiles.GetSelectedUserProfilesAsync(missing) ?? new List<UserProfile>())
					if (profile?.UserId != null)
						byId[profile.UserId] = profile;

			// The number is stamped from the department's own row (resolved); with no service the column is left empty rather than read raw.
			if (sensitiveData != null)
				await sensitiveData.ApplyIdentificationNumbersAsync(departmentId, byId.Values.ToList(), grantToken, actingUserId);
			else
				foreach (var profile in byId.Values)
					profile.IdentificationNumber = null;

			return ids.Select(id =>
			{
				byId.TryGetValue(id, out var profile);
				memberships.TryGetValue(id, out var membership);
				emails.TryGetValue(id, out var email);
				return new LmsExportMember
				{
					UserId = id,
					FirstName = profile?.FirstName,
					LastName = profile?.LastName,
					Email = string.IsNullOrWhiteSpace(profile?.MembershipEmail) ? email : profile.MembershipEmail,
					IdentificationNumber = profile?.IdentificationNumber,
					IsCurrent = DepartmentMemberStateHelper.IsCurrentMember(membership, departmentId),
					IsActive = DepartmentMemberStateHelper.IsActiveMember(membership, departmentId)
				};
			}).ToList();
		}
	}

	/// <summary>
	/// The department data exit (Training T5, R-C12): owners and admins request an export (audited), worker 99 builds the zip from the
	/// department's rows (<see cref="LmsDataExportWriter"/>) into the LMS store (or the database on an install without one), and the
	/// download link expires after <see cref="Config.LmsDataExitConfig.DownloadLinkDays"/> days, when worker 99 removes the zip. The add-on
	/// gate is never asked: a lapsed department exports exactly as an active one (§17.3 rules 1–2), and worker 99 only runs due-work queries.
	/// </summary>
	public class LmsDataExportService : ILmsDataExportService
	{
		private const int BuildBatch = 5;

		private readonly ILmsDataExportsRepository _exports;
		private readonly ILmsDataExportBlobsRepository _blobs;
		private readonly ILmsDataExportSourceRepository _source;
		private readonly ILmsMediaStore _store;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IEventAggregator _events;

		private readonly IDepartmentMemberSensitiveDataService _sensitiveData;

		public LmsDataExportService(ILmsDataExportsRepository exports, ILmsDataExportBlobsRepository blobs, ILmsDataExportSourceRepository source, ILmsMediaStore store,
			IDepartmentsService departments, IUserProfileService profiles, IEventAggregator events, IDepartmentMemberSensitiveDataService sensitiveData)
		{
			_sensitiveData = sensitiveData;
			_exports = exports;
			_blobs = blobs;
			_source = source;
			_store = store;
			_departments = departments;
			_profiles = profiles;
			_events = events;
		}

		private bool StoreConfigured => _store?.IsConfigured == true;

		private void Audit(int departmentId, string userId, AuditLogTypes type, object after) =>
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = after == null ? null : JsonConvert.SerializeObject(after),
				Successful = true
			});

		public async Task<LmsDataExitResult> RequestAsync(LmsActor actor, LmsDataExportScopes scope, bool includeMedia, CancellationToken cancellationToken = default)
		{
			if (!LmsDataExitRules.CanExport(actor))
				return LmsDataExitResult.Fail(LmsDataExitErrors.NotAuthorized);
			if (!Enum.IsDefined(typeof(LmsDataExportScopes), scope))
				scope = LmsDataExportScopes.Full;

			var existing = await _exports.GetForDepartmentAsync(actor.DepartmentId, 20) ?? new List<LmsDataExport>();
			if (existing.Count(LmsDataExitRules.IsActive) >= LmsDataExitRules.MaxActivePerDepartment)
				return LmsDataExitResult.Fail(LmsDataExitErrors.TooManyActive);

			var export = await _exports.InsertAsync(new LmsDataExport
			{
				DepartmentId = actor.DepartmentId,
				Scope = (int)scope,
				IncludeMedia = includeMedia && scope == LmsDataExportScopes.Full,
				Status = (int)LmsDataExportStatuses.Queued,
				RequestedByUserId = actor.UserId,
				RequestedOn = DateTime.UtcNow
			}, cancellationToken, true);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsDataExportRequested, new { export.LmsDataExportId, Scope = scope.ToString(), export.IncludeMedia });
			return new LmsDataExitResult { Export = export };
		}

		public async Task<List<LmsDataExport>> GetExportsAsync(LmsActor actor)
		{
			if (!LmsDataExitRules.CanExport(actor))
				return new List<LmsDataExport>();
			return await _exports.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsDataExport>();
		}

		public async Task<LmsDataExitResult> GetDownloadAsync(LmsActor actor, int lmsDataExportId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (!LmsDataExitRules.CanExport(actor))
				return LmsDataExitResult.Fail(LmsDataExitErrors.NotAuthorized);

			var export = await _exports.GetByIdForDepartmentAsync(actor.DepartmentId, lmsDataExportId);
			var refusal = LmsDataExitRules.DownloadRefusal(export, utcNow);
			if (refusal != LmsDataExitErrors.None)
				return LmsDataExitResult.Fail(refusal);

			var result = new LmsDataExitResult { Export = export, FileName = export.FileName ?? LmsDataExitRules.FileName(export, export.CompletedOn ?? utcNow) };
			if (export.StoredInDatabase)
			{
				result.Bytes = await _blobs.GetDataAsync(actor.DepartmentId, export.LmsDataExportId);
				if (result.Bytes == null)
					return LmsDataExitResult.Fail(LmsDataExitErrors.Expired);
			}
			else
			{
				if (!StoreConfigured || string.IsNullOrWhiteSpace(export.ObjectKey))
					return LmsDataExitResult.Fail(LmsDataExitErrors.NotReady);
				var until = utcNow.Add(LmsDataExitRules.DownloadUrlLifetime);
				result.DownloadUrl = _store.GetSignedUrl(export.ObjectKey, until < export.ExpiresOn.Value ? until : export.ExpiresOn.Value);
			}

			export.DownloadCount++;
			export.LastDownloadedOn = utcNow;
			await _exports.UpdateAsync(export, cancellationToken, true);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.LmsDataExportDownloaded, new { export.LmsDataExportId, Scope = ((LmsDataExportScopes)export.Scope).ToString(), export.FileName });
			return result;
		}

		public async Task<LmsDataExportRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new LmsDataExportRunResult { Requeued = await _exports.RequeueStaleAsync(utcNow - LmsDataExitRules.StaleClaim, cancellationToken) };

			foreach (var export in await _exports.GetQueuedAsync(BuildBatch) ?? new List<LmsDataExport>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (export.Attempts >= LmsDataExitRules.MaxAttempts)
				{
					export.Status = (int)LmsDataExportStatuses.Failed;
					export.CompletedOn = utcNow;
					await _exports.UpdateAsync(export, cancellationToken, true);
					result.Failed++;
					continue;
				}

				if (!await _exports.TryClaimAsync(export.LmsDataExportId, utcNow, cancellationToken))
					continue;
				export.Status = (int)LmsDataExportStatuses.Running;
				export.ClaimedOn = utcNow;
				export.Attempts++;

				try
				{
					await BuildAsync(export, cancellationToken);
					result.Completed++;
					// Workflow trigger 269, once: this run claimed the export before building it.
					ContentWorkflowContext.Publish(_events, LmsWorkflowEvents.DataExportReady(export, DateTime.UtcNow));
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Training data export " + export.LmsDataExportId.ToString(CultureInfo.InvariantCulture) + " failed.");
					var final = export.Attempts >= LmsDataExitRules.MaxAttempts;
					export.Status = (int)(final ? LmsDataExportStatuses.Failed : LmsDataExportStatuses.Queued);
					export.ClaimedOn = null;
					export.CompletedOn = final ? DateTime.UtcNow : (DateTime?)null;
					export.Error = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;
					await _exports.UpdateAsync(export, CancellationToken.None, true);
					if (final)
						result.Failed++;
				}
			}

			foreach (var expired in await _exports.GetExpiredUnpurgedAsync(utcNow, 50) ?? new List<LmsDataExport>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (expired.StoredInDatabase)
						await _blobs.DeleteForExportAsync(expired.DepartmentId, expired.LmsDataExportId, cancellationToken);
					else if (StoreConfigured && !string.IsNullOrWhiteSpace(expired.ObjectKey))
						await _store.DeletePrefixAsync(expired.ObjectKey, cancellationToken);

					expired.Status = (int)LmsDataExportStatuses.Expired;
					expired.PurgedOn = utcNow;
					await _exports.UpdateAsync(expired, cancellationToken, true);
					result.Purged++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Training data export " + expired.LmsDataExportId.ToString(CultureInfo.InvariantCulture) + " could not be removed; the next run retries.");
				}
			}

			return result;
		}

		public async Task<LmsDataExportSnapshot> LoadSnapshotAsync(int departmentId, LmsDataExportScopes scope, CancellationToken cancellationToken = default)
		{
			var full = scope == LmsDataExportScopes.Full;
			async Task<List<T>> Rows<T>() where T : class, IEntity, new() => await _source.GetDepartmentRowsAsync<T>(departmentId, cancellationToken) ?? new List<T>();
			async Task<List<T>> FullRows<T>() where T : class, IEntity, new() => full ? await Rows<T>() : new List<T>();

			var snapshot = new LmsDataExportSnapshot
			{
				DepartmentId = departmentId,
				Scope = scope,
				LearningRecords = await Rows<LearningRecord>(),
				Categories = await Rows<TrainingCategory>(),
				Attempts = await Rows<LmsAssessmentAttempt>(),
				Enrollments = await Rows<LmsEnrollment>(),
				// Titles for the records files in both scopes; the content only in the full scope.
				Courses = await Rows<LmsCourse>(),
				Lessons = await Rows<LmsLesson>(),
				Versions = await FullRows<LmsCourseVersion>(),
				Modules = await FullRows<LmsCourseModule>(),
				CourseAudiences = await FullRows<LmsCourseAudience>(),
				MediaAssets = await FullRows<LmsMediaAsset>(),
				Banks = await FullRows<LmsQuestionBank>(),
				Questions = await FullRows<LmsQuestion>(),
				LessonPlans = await FullRows<LmsLessonPlan>(),
				LessonPlanVersions = await FullRows<LmsLessonPlanVersion>(),
				SkillSheets = await FullRows<LmsSkillSheet>(),
				SkillSheetVersions = await FullRows<LmsSkillSheetVersion>(),
				TaskBookDefinitions = await FullRows<LmsTaskBookDefinition>(),
				Requirements = await FullRows<LmsRequirement>(),
				RequirementAudiences = await FullRows<LmsRequirementAudience>(),
				TrainingPlans = await FullRows<LmsTrainingPlan>(),
				TrainingPlanItems = await FullRows<LmsTrainingPlanItem>(),
				Paths = await FullRows<LmsPath>(),
				PathSteps = await FullRows<LmsPathStep>()
			};

			var named = snapshot.LearningRecords.Select(r => r.UserId).Concat(snapshot.Attempts.Select(a => a.UserId)).Concat(snapshot.Enrollments.Select(e => e.UserId));
			// Worker 99 is unattended: no grant, so a protected department's identification numbers leave as REDACTED.
			snapshot.Members = await LmsExportMembers.LoadAsync(_departments, _profiles, _sensitiveData, departmentId, named, null, null);
			return snapshot;
		}

		/// <summary>Plans the hosted media of a full export: copied within the cap when asked for and a store exists, otherwise a signed link.</summary>
		public static Dictionary<int, LmsExportMediaFile> PlanMedia(IEnumerable<LmsMediaAsset> assets, int departmentId, bool includeMedia, ILmsMediaStore store,
			Func<string, long?> sizeOf, DateTime linkExpiresOn, long maxMediaBytes)
		{
			var plan = new Dictionary<int, LmsExportMediaFile>();
			if (store == null || !store.IsConfigured)
				return plan;

			long copied = 0;
			foreach (var asset in (assets ?? Enumerable.Empty<LmsMediaAsset>())
				.Where(a => a != null && a.DepartmentId == departmentId && a.Kind == (int)LmsMediaKinds.Video &&
				            (a.Status == (int)LmsMediaStatuses.Ready || a.Status == (int)LmsMediaStatuses.Archived))
				.OrderBy(a => a.LmsMediaAssetId))
			{
				var key = string.IsNullOrWhiteSpace(asset.Mp4Key) ? asset.SourceKey : asset.Mp4Key;
				if (string.IsNullOrWhiteSpace(key))
					continue;
				var file = new LmsExportMediaFile
				{
					LmsMediaAssetId = asset.LmsMediaAssetId,
					ObjectKey = key,
					ContentType = key == asset.Mp4Key ? "video/mp4" : asset.ContentType,
					SignedUrl = store.GetSignedUrl(key, linkExpiresOn)
				};
				if (includeMedia)
				{
					file.SizeBytes = sizeOf?.Invoke(key);
					if (file.SizeBytes.HasValue && copied + file.SizeBytes.Value <= maxMediaBytes)
					{
						copied += file.SizeBytes.Value;
						var extension = Path.GetExtension(key);
						file.ZipPath = "courses/media/video-" + asset.LmsMediaAssetId.ToString(CultureInfo.InvariantCulture) +
						               (string.IsNullOrWhiteSpace(extension) || extension.Length > 6 ? ".mp4" : extension.ToLowerInvariant());
					}
				}

				plan[asset.LmsMediaAssetId] = file;
			}

			return plan;
		}

		private async Task BuildAsync(LmsDataExport export, CancellationToken cancellationToken)
		{
			var scope = Enum.IsDefined(typeof(LmsDataExportScopes), export.Scope) ? (LmsDataExportScopes)export.Scope : LmsDataExportScopes.Full;
			var snapshot = await LoadSnapshotAsync(export.DepartmentId, scope, cancellationToken);
			var department = await _departments.GetDepartmentByIdAsync(export.DepartmentId, false);
			var generatedOn = DateTime.UtcNow;
			snapshot.ExportId = export.LmsDataExportId;
			snapshot.DepartmentName = department?.Name;
			snapshot.RequestedByUserId = export.RequestedByUserId;
			snapshot.GeneratedOnUtc = generatedOn;
			snapshot.BaseUrl = Config.SystemBehaviorConfig.ResgridBaseUrl;

			var expiresOn = LmsDataExitRules.ExpiresOn(generatedOn, Config.LmsDataExitConfig.DownloadLinkDays);
			if (scope == LmsDataExportScopes.Full)
			{
				var sizes = new Dictionary<string, long?>(StringComparer.Ordinal);
				if (export.IncludeMedia && StoreConfigured)
					foreach (var asset in snapshot.MediaAssets.Where(a => a.DepartmentId == export.DepartmentId))
					{
						var key = string.IsNullOrWhiteSpace(asset.Mp4Key) ? asset.SourceKey : asset.Mp4Key;
						if (!string.IsNullOrWhiteSpace(key) && !sizes.ContainsKey(key))
							sizes[key] = await _store.GetSizeAsync(key, cancellationToken);
					}

				// S3 presigned links live at most seven days; the export may outlive them, so the link expiry is the earlier of the two.
				var linkExpiry = expiresOn < generatedOn.AddDays(7) ? expiresOn : generatedOn.AddDays(7);
				snapshot.Media = PlanMedia(snapshot.MediaAssets, export.DepartmentId, export.IncludeMedia, _store, k => sizes.TryGetValue(k, out var s) ? s : null, linkExpiry,
					Config.LmsDataExitConfig.MaxMediaBytes);
			}

			var folder = string.IsNullOrWhiteSpace(Config.LmsDataExitConfig.WorkDirectory) ? Path.GetTempPath() : Config.LmsDataExitConfig.WorkDirectory;
			var temp = Path.Combine(folder, "rg-training-export-" + export.LmsDataExportId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".zip");
			try
			{
				LmsDataExportManifest manifest;
				await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
					manifest = await LmsDataExportWriter.WriteAsync(file, snapshot, StoreConfigured ? (key, ct) => _store.OpenReadAsync(key, ct) : null, cancellationToken);

				var fileName = LmsDataExitRules.FileName(export, generatedOn);
				string sha;
				long size;
				await using (var read = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
				{
					size = read.Length;
					sha = Convert.ToHexString(await SHA256.HashDataAsync(read, cancellationToken)).ToLowerInvariant();
					read.Position = 0;
					if (StoreConfigured)
					{
						export.ObjectKey = _store.KeyFor(export.DepartmentId, 0, "exports/" + export.LmsDataExportId.ToString(CultureInfo.InvariantCulture) + "/" + fileName);
						export.StoredInDatabase = false;
						await _store.PutAsync(export.ObjectKey, read, "application/zip", cancellationToken);
					}
					else
					{
						if (size > Config.LmsDataExitConfig.MaxDatabaseZipBytes)
							throw new InvalidOperationException("The export is larger than the database limit; configure the LMS store (LmsConfig.S3Endpoint) to export this department.");
						var bytes = new byte[size];
						await read.ReadExactlyAsync(bytes, cancellationToken);
						await _blobs.DeleteForExportAsync(export.DepartmentId, export.LmsDataExportId, cancellationToken);
						await _blobs.InsertAsync(new LmsDataExportBlob { DepartmentId = export.DepartmentId, LmsDataExportId = export.LmsDataExportId, Data = bytes, CreatedOn = generatedOn },
							cancellationToken, true);
						export.ObjectKey = null;
						export.StoredInDatabase = true;
					}
				}

				export.Status = (int)LmsDataExportStatuses.Completed;
				export.CompletedOn = generatedOn;
				export.ExpiresOn = expiresOn;
				export.FileName = fileName;
				export.SizeBytes = size;
				export.Sha256 = sha;
				export.CountsJson = JsonConvert.SerializeObject(manifest.Counts);
				export.Error = null;
				await _exports.UpdateAsync(export, cancellationToken, true);
			}
			finally
			{
				try
				{
					if (System.IO.File.Exists(temp))
						System.IO.File.Delete(temp);
				}
				catch (IOException ex)
				{
					Logging.LogException(ex, "A training export work file could not be removed: " + temp);
				}
			}
		}
	}
}
