using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The page text of protocol files for the knowledge index: the extracted pages (ProtocolFileTexts, M0347) of the cited version's
	/// primary file and of the protocol's attachments. Only finished extractions are read; a file still queued, marked "no text"
	/// (encrypted, scanned) or failed contributes nothing. Other source kinds (training files, documents) have no extraction store yet
	/// and yield no pages, as before.
	/// </summary>
	public sealed class ProtocolKnowledgePageTextSource : IKnowledgePageTextSource
	{
		private readonly IProtocolFileTextsRepository _texts;
		private readonly IProtocolVersionFilesRepository _files;

		public ProtocolKnowledgePageTextSource(IProtocolFileTextsRepository texts, IProtocolVersionFilesRepository files)
		{
			_texts = texts;
			_files = files;
		}

		public async Task<IReadOnlyList<KnowledgePageText>> GetPagesAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, string versionId,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || kind != KnowledgeSourceKinds.ProtocolFile
				|| !int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var protocolId)
				|| !int.TryParse(versionId, NumberStyles.None, CultureInfo.InvariantCulture, out var protocolVersionId))
				return Array.Empty<KnowledgePageText>();

			var extractions = (await _texts.GetForProtocolAsync(departmentId, protocolId) ?? new List<ProtocolFileText>())
				.Where(t => t != null && t.DepartmentId == departmentId && t.DispatchProtocolId == protocolId
					&& t.Status == (int)ProtocolFileTextStatuses.Extracted
					&& ((t.SourceKind == (int)ProtocolFileSourceKinds.VersionFile && t.ProtocolVersionId == protocolVersionId)
						|| t.SourceKind == (int)ProtocolFileSourceKinds.Attachment))
				.OrderBy(t => t.SourceKind).ThenBy(t => t.SourceId)
				.ToList();
			if (extractions.Count == 0)
				return Array.Empty<KnowledgePageText>();

			string versionFileName = null;
			if (extractions.Any(t => t.SourceKind == (int)ProtocolFileSourceKinds.VersionFile))
				versionFileName = (await _files.GetMetadataForVersionAsync(departmentId, protocolVersionId))?.FileName;

			var result = new List<KnowledgePageText>();
			foreach (var extraction in extractions)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var isVersionFile = extraction.SourceKind == (int)ProtocolFileSourceKinds.VersionFile;
				// Version files and attachments are numbered separately, so the prefix keeps their page anchors apart.
				var attachmentId = (isVersionFile ? "file-" : "attachment-") + extraction.SourceId.ToString(CultureInfo.InvariantCulture);
				foreach (var page in await _texts.GetPagesAsync(departmentId, extraction.ProtocolFileTextId) ?? new List<ProtocolFileTextPage>())
				{
					if (page == null || page.DepartmentId != departmentId || page.ProtocolFileTextId != extraction.ProtocolFileTextId)
						continue;
					result.Add(new KnowledgePageText
					{
						AttachmentId = attachmentId,
						FileName = isVersionFile ? versionFileName : null,
						PageNumber = page.PageNumber,
						Text = page.Text
					});
				}
			}

			return result;
		}
	}
}
