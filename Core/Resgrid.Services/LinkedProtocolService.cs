using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Protocols P5 "Linked departments" (round 10 A3): the other side's published protocols over a Department Link that shares Protocols
	/// toward the viewer, read-only and only the version in force. A protocol is shared when it could be on a public page
	/// (<see cref="ProtocolPublicPages.Eligibility"/>: published, not disabled, not licensed content, no audience rows, an authored version), so
	/// nothing restricted to some of the owner's members ever reaches another department. Every call re-reads the link.
	/// </summary>
	public class LinkedProtocolService : ILinkedProtocolService
	{
		private readonly IDepartmentLinksService _links;
		private readonly IDepartmentsService _departments;
		private readonly IProtocolsService _protocols;
		private readonly IProtocolLibraryService _library;

		public LinkedProtocolService(IDepartmentLinksService links, IDepartmentsService departments, IProtocolsService protocols, IProtocolLibraryService library)
		{
			_links = links;
			_departments = departments;
			_protocols = protocols;
			_library = library;
		}

		private async Task<(DepartmentLink Link, int SharingDepartmentId)> SharingAsync(int viewerDepartmentId, int departmentLinkId)
		{
			if (viewerDepartmentId <= 0 || departmentLinkId <= 0)
				return (null, 0);
			var link = await _links.GetLinkByIdAsync(departmentLinkId);
			var sharing = DepartmentLinkSharing.GetSharedDepartmentId(link, viewerDepartmentId, DepartmentLinkShareTypes.Protocols);
			return sharing.HasValue ? (link, sharing.Value) : (null, 0);
		}

		public async Task<LinkedProtocolList> GetSharedProtocolsAsync(int viewerDepartmentId, int departmentLinkId)
		{
			var (link, sharingId) = await SharingAsync(viewerDepartmentId, departmentLinkId);
			if (link == null)
				return null;

			var result = new LinkedProtocolList { Link = link, SharingDepartmentId = sharingId, SharingDepartmentName = await NameAsync(sharingId) };
			try
			{
				var protocols = (await _protocols.GetProtocolSummariesForDepartmentAsync(sharingId, true) ?? new List<DispatchProtocol>())
					.Where(p => p != null && p.DepartmentId == sharingId && p.IsPublished && !p.IsDeleted).ToList();
				if (protocols.Count == 0)
					return result;

				var versions = (await _library.GetDepartmentVersionHeadersAsync(sharingId) ?? new List<ProtocolVersion>())
					.Where(v => v.DepartmentId == sharingId).ToDictionary(v => v.ProtocolVersionId);
				var audiences = (await _library.GetDepartmentAudiencesAsync(sharingId) ?? new List<ProtocolAudience>()).ToLookup(a => a.DispatchProtocolId);
				var categories = (await _library.GetCategoriesAsync(sharingId) ?? new List<ProtocolCategory>()).ToDictionary(c => c.ProtocolCategoryId, c => c.Name);

				foreach (var protocol in protocols.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
				{
					if (!protocol.CurrentVersionId.HasValue || !versions.TryGetValue(protocol.CurrentVersionId.Value, out var current))
						continue;
					if (ProtocolPublicPages.Eligibility(protocol, current, audiences[protocol.DispatchProtocolId]) != ProtocolPublicEligibility.Eligible)
						continue;
					result.Protocols.Add(Summary(protocol, current, categories));
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Linked department protocols could not be read.");
			}

			return result;
		}

		public async Task<LinkedProtocolPage> GetSharedProtocolAsync(int viewerDepartmentId, int departmentLinkId, int protocolId)
		{
			var (link, sharingId) = await SharingAsync(viewerDepartmentId, departmentLinkId);
			if (link == null || protocolId <= 0)
				return null;

			var protocol = await _protocols.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != sharingId || !protocol.CurrentVersionId.HasValue)
				return null;

			var current = await _library.GetVersionAsync(sharingId, protocol.CurrentVersionId.Value);
			var audience = await _library.GetAudienceAsync(sharingId, protocolId);
			if (ProtocolPublicPages.Eligibility(protocol, current, audience) != ProtocolPublicEligibility.Eligible)
				return null;

			var categories = (await _library.GetCategoriesAsync(sharingId) ?? new List<ProtocolCategory>()).ToDictionary(c => c.ProtocolCategoryId, c => c.Name);
			return new LinkedProtocolPage
			{
				Summary = Summary(protocol, current, categories),
				SharingDepartmentName = await NameAsync(sharingId),
				// Stored sanitized at the version boundary; the page renders it through Html.ProtocolContent like every protocol page.
				BodyHtml = current.ContentHtml ?? string.Empty,
				ChangeSummary = current.ChangeSummary,
				ContentShortCode = SignaturePolicy.ShortCode(current.ContentHash),
				DepartmentLinkId = link.DepartmentLinkId
			};
		}

		private static LinkedProtocolSummary Summary(DispatchProtocol protocol, ProtocolVersion current, IReadOnlyDictionary<int, string> categories) => new LinkedProtocolSummary
		{
			ProtocolId = protocol.DispatchProtocolId,
			Name = protocol.Name,
			Number = string.IsNullOrWhiteSpace(protocol.Number) ? protocol.Code : protocol.Number,
			Kind = protocol.Kind,
			CategoryName = protocol.CategoryId.HasValue && categories.TryGetValue(protocol.CategoryId.Value, out var name) ? name : null,
			VersionLabel = current?.Label,
			EffectiveOn = current?.EffectiveOn
		};

		private async Task<string> NameAsync(int departmentId)
		{
			try
			{
				return (await _departments.GetDepartmentByIdAsync(departmentId, false))?.Name;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}
	}

	/// <summary>The edit lock on managed set copies (round 10 A3): one repository read, fail closed is not wanted here (a read error lets the edit through).</summary>
	public class ContentSetCopyLocks : IContentSetCopyLocks
	{
		private readonly Model.Repositories.IContentSetCopiesRepository _copies;

		public ContentSetCopyLocks(Model.Repositories.IContentSetCopiesRepository copies)
		{
			_copies = copies;
		}

		public async Task<bool> IsManagedCopyAsync(int departmentId, ContentSetItemKinds kind, int localItemId)
		{
			if (departmentId <= 0 || localItemId <= 0)
				return false;
			try
			{
				return await _copies.GetManagedForLocalItemAsync(departmentId, kind, localItemId) != null;
			}
			catch (Exception ex)
			{
				// A missing table (an install that has not run M0415) or a read error must not stop the department editing its own content.
				Logging.LogException(ex, "The set copy lock could not be read.");
				return false;
			}
		}
	}
}
