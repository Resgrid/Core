using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Offline and staleness (P3): what an offline client holds, by hash, and whether the version a device shows is still in force. Reads
	/// only headers and hashes: no content and no file bytes (attachment hashes are computed by the database).
	/// </summary>
	public class ProtocolOfflineService : IProtocolOfflineService
	{
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IDispatchProtocolAttachmentRepository _attachments;
		private readonly IProtocolVersionFilesRepository _files;
		private readonly IProtocolVersionTranslationsRepository _translations;

		public ProtocolOfflineService(IProtocolsService protocolsService, IProtocolAccessService access, IProtocolVersionsRepository versions,
			IDispatchProtocolAttachmentRepository attachments, IProtocolVersionFilesRepository files, IProtocolVersionTranslationsRepository translations)
		{
			_protocolsService = protocolsService;
			_access = access;
			_versions = versions;
			_attachments = attachments;
			_files = files;
			_translations = translations;
		}

		public async Task<ProtocolOfflineManifest> GetManifestAsync(int departmentId, string userId)
		{
			var manifest = new ProtocolOfflineManifest { DepartmentId = departmentId, GeneratedOn = DateTime.UtcNow };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return manifest;

			var reader = await _access.GetReaderAsync(departmentId, userId);
			var all = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId, true) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == departmentId && !p.IsDeleted && p.CurrentVersionId.HasValue && p.Status == (int)ProtocolStatuses.Published).ToList();
			var readable = await _access.FilterReadableAsync(reader, all) ?? new List<DispatchProtocol>();
			if (readable.Count == 0)
				return manifest;

			var headers = (await _versions.GetHeadersForDepartmentAsync(departmentId) ?? new List<ProtocolVersion>()).Where(v => v.DepartmentId == departmentId).ToList();
			var byId = headers.ToDictionary(v => v.ProtocolVersionId);
			var scheduled = headers.Where(v => v.Status == (int)ProtocolVersionStatuses.Approved && v.EffectiveOn.HasValue)
				.GroupBy(v => v.DispatchProtocolId).ToDictionary(g => g.Key, g => g.OrderBy(v => v.EffectiveOn).First());

			var currentIds = readable.Select(p => p.CurrentVersionId.Value).ToList();
			var documents = (await _files.GetMetadataForVersionsAsync(departmentId, currentIds) ?? new List<ProtocolVersionFile>())
				.Where(f => f.DepartmentId == departmentId).GroupBy(f => f.ProtocolVersionId).ToDictionary(g => g.Key, g => g.First());
			var attachments = (await _attachments.GetAttachmentHashesByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocolAttachment>())
				.ToLookup(a => a.DispatchProtocolId);
			var translations = (await _translations.GetApprovedHeadersForVersionsAsync(departmentId, currentIds) ?? new List<ProtocolVersionTranslation>())
				.Where(t => t.DepartmentId == departmentId).ToLookup(t => t.ProtocolVersionId);

			// Whether a version has steps needs its content; only Procedure and Dispatch versions are read for it.
			var stepCandidates = readable.Where(p => ProtocolStepRules.Applies(p.Kind)).Select(p => p.CurrentVersionId.Value).ToList();
			var withSteps = new HashSet<int>();
			if (stepCandidates.Count > 0)
			{
				foreach (var version in await _versions.GetByIdsAsync(departmentId, stepCandidates, true) ?? new List<ProtocolVersion>())
				{
					if (!version.IsDocument && ProtocolSteps.Extract(version.ContentJson, version.ContentHtml).Count > 0)
						withSteps.Add(version.ProtocolVersionId);
				}
			}

			foreach (var protocol in readable.OrderBy(p => p.DispatchProtocolId))
			{
				if (!byId.TryGetValue(protocol.CurrentVersionId.Value, out var version))
					continue;

				var item = new ProtocolOfflineItem
				{
					ProtocolId = protocol.DispatchProtocolId,
					Code = protocol.Code,
					Name = protocol.Name,
					Kind = protocol.Kind,
					VersionId = version.ProtocolVersionId,
					VersionLabel = version.Label,
					EffectiveOn = version.EffectiveOn,
					ExpiresOn = version.ExpiresOn,
					ContentHash = version.ContentHash,
					ContentKind = version.ContentKind,
					Locale = version.Locale,
					HasSteps = withSteps.Contains(version.ProtocolVersionId)
				};

				if (version.IsDocument && documents.TryGetValue(version.ProtocolVersionId, out var file))
					item.Files.Add(new ProtocolOfflineFile { FileKind = "document", FileId = file.ProtocolVersionFileId, FileName = file.FileName, ContentType = file.ContentType, Size = file.Size, Sha256 = file.Sha256 });

				foreach (var attachment in attachments[protocol.DispatchProtocolId])
					item.Files.Add(new ProtocolOfflineFile
					{
						FileKind = "attachment",
						FileId = attachment.DispatchProtocolAttachmentId,
						FileName = attachment.FileName,
						ContentType = attachment.FileType,
						Size = attachment.Size,
						Sha256 = attachment.Sha256
					});

				foreach (var translation in translations[version.ProtocolVersionId].Where(t => string.Equals(t.SourceContentHash, version.ContentHash, StringComparison.OrdinalIgnoreCase)))
					item.Translations.Add(new ProtocolOfflineTranslation { TranslationId = translation.ProtocolVersionTranslationId, Locale = translation.Locale, ContentHash = translation.ContentHash });

				if (scheduled.TryGetValue(protocol.DispatchProtocolId, out var next) && next.ProtocolVersionId != version.ProtocolVersionId)
					item.Scheduled = new ProtocolOfflineScheduled { VersionId = next.ProtocolVersionId, VersionLabel = next.Label, EffectiveOn = next.EffectiveOn };

				manifest.Items.Add(item);
			}

			return manifest;
		}

		public async Task<ProtocolStaleness> GetStalenessAsync(int departmentId, string userId, int protocolId, int? viewedVersionId, string viewedContentHash = null)
		{
			var protocol = await _access.GetReadableAsync(departmentId, userId, protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return null;

			var versions = (await _versions.GetForProtocolAsync(departmentId, protocolId) ?? new List<ProtocolVersion>())
				.Where(v => v.DepartmentId == departmentId && v.DispatchProtocolId == protocolId).ToList();
			var viewed = viewedVersionId.HasValue ? versions.FirstOrDefault(v => v.ProtocolVersionId == viewedVersionId.Value) : null;
			var current = protocol.CurrentVersionId.HasValue ? versions.FirstOrDefault(v => v.ProtocolVersionId == protocol.CurrentVersionId.Value) : null;
			var inForce = current != null && current.Status == (int)ProtocolVersionStatuses.Published && protocol.Status == (int)ProtocolStatuses.Published && !protocol.IsDeleted;
			var next = versions.Where(v => v.Status == (int)ProtocolVersionStatuses.Approved && v.EffectiveOn.HasValue).OrderBy(v => v.EffectiveOn).FirstOrDefault();

			var answer = new ProtocolStaleness
			{
				ProtocolId = protocolId,
				ViewedVersionId = viewed?.ProtocolVersionId ?? viewedVersionId,
				ViewedLabel = viewed?.Label,
				CurrentVersionId = inForce ? current.ProtocolVersionId : (int?)null,
				CurrentLabel = inForce ? current.Label : null,
				CurrentEffectiveOn = inForce ? current.EffectiveOn : null,
				CurrentContentHash = inForce ? current.ContentHash : null,
				IsRetired = !inForce,
				Scheduled = next == null ? null : new ProtocolOfflineScheduled { VersionId = next.ProtocolVersionId, VersionLabel = next.Label, EffectiveOn = next.EffectiveOn }
			};

			if (!inForce)
			{
				answer.IsStale = true;
				return answer;
			}

			var differentVersion = viewedVersionId.HasValue && viewedVersionId.Value != current.ProtocolVersionId;
			var differentHash = !string.IsNullOrWhiteSpace(viewedContentHash) && !string.Equals(viewedContentHash.Trim(), current.ContentHash, StringComparison.OrdinalIgnoreCase);
			answer.IsStale = differentVersion || differentHash;
			return answer;
		}
	}
}
