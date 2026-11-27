using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Services
{
	/// <summary>
	/// The content hash of a protocol version recomputed from what is stored now, never read from the hash column: the SHA-256 of the
	/// sanitized HTML (<see cref="ProtocolContentHash"/>) for authored content, the SHA-256 of the primary file's bytes for a
	/// document-backed version (P1 R-P5). Used by approvals, publish, review stamps, read-and-sign and signature verification, so a
	/// changed row or a swapped file fails every check the same way.
	/// </summary>
	public static class ProtocolVersionHashing
	{
		/// <summary>The hash as stored content gives it now; null for a document-backed version whose file is gone.</summary>
		public static async Task<string> CurrentHashAsync(ProtocolVersion version, IProtocolVersionFilesRepository files)
		{
			if (version == null)
				return null;

			if (!version.IsDocument)
				return ProtocolContentHash.Compute(version.ContentHtml);

			if (files == null)
				return null;

			var file = await files.GetWithDataForVersionAsync(version.DepartmentId, version.ProtocolVersionId);
			return file?.Data == null ? null : ProtocolDocumentRules.HashFile(file.Data);
		}
	}
}
