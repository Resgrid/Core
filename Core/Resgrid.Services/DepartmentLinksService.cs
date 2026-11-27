using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DepartmentLinksService: IDepartmentLinksService
	{
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		/// <summary>
		/// Sliding lifetime of a department's link-share access epoch. Every subscribe refreshes it (publishers only read it),
		/// so it only lapses for a department nobody subscribed to for this long; the lapse strands any still-subscribed
		/// connection until it subscribes again, so keep this well beyond a connection's lifetime.
		/// </summary>
		private static readonly TimeSpan ShareAccessVersionSlidingExpiration = TimeSpan.FromDays(30);

		private readonly IDepartmentLinksRepository _departmentLinksRepository;
		private readonly IDepartmentsRepository _departmentsRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEnumerable<IDepartmentLinkAccessListener> _accessListeners;

		public DepartmentLinksService(IDepartmentLinksRepository departmentLinksRepository, IDepartmentsRepository departmentsRepository, ICacheProvider cacheProvider,
			IEnumerable<IDepartmentLinkAccessListener> accessListeners)
		{
			_departmentLinksRepository = departmentLinksRepository;
			_departmentsRepository = departmentsRepository;
			_cacheProvider = cacheProvider;
			_accessListeners = accessListeners ?? Enumerable.Empty<IDepartmentLinkAccessListener>();
		}

		public async Task<List<DepartmentLink>> GetAllLinksForDepartmentAsync(int departmentId)
		{
			var links = await _departmentLinksRepository.GetAllLinksForDepartmentAsync(departmentId);

			if (links != null && links.Any())
			{
				foreach (var link in links)
				{
					link.Department = await _departmentsRepository.GetByIdAsync(link.DepartmentId);
					link.LinkedDepartment = await _departmentsRepository.GetByIdAsync(link.LinkedDepartmentId);
				}

				return links.ToList();
			}

			return new List<DepartmentLink>();
		}

		public async Task<DepartmentLink> GetLinkByIdAsync(int linkId)
		{
			var link = await _departmentLinksRepository.GetByIdAsync(linkId);

			if (link != null)
			{
				link.Department = await _departmentsRepository.GetByIdAsync(link.DepartmentId);
				link.LinkedDepartment = await _departmentsRepository.GetByIdAsync(link.LinkedDepartmentId);

				return link;
			}

			return null;
		}

		public async Task<DepartmentLink> SaveAsync(DepartmentLink link, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The stored row, read fresh before the write, says whether this save takes anything away from either party.
			DepartmentLink stored = null;
			if (link != null && link.DepartmentLinkId > 0)
				stored = await _departmentLinksRepository.GetByIdAsync(link.DepartmentLinkId);

			var saved = await _departmentLinksRepository.SaveOrUpdateAsync(link, cancellationToken);

			// Rotate only after the write. A subscriber reads the epoch before it re-reads the link, so one that sees the new
			// epoch also sees the reduced link and is refused; one that read the old epoch joins a group nobody sends to.
			if (DepartmentLinkSharing.ReducesAccess(stored, link))
			{
				await RevokeShareAccessForPartiesAsync(stored);
				await NotifyAccessReducedAsync(stored, link, cancellationToken);
			}

			return saved;
		}

		public async Task<bool> DeleteLinkAsync(DepartmentLink link, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (link == null)
				return false;

			// Nothing references a DepartmentLinks row, and the repository delete removes this row by its id only.
			var deleted = await _departmentLinksRepository.DeleteAsync(link, cancellationToken);

			// Revoke even when nothing was deleted (the row may already be gone): a stale subscription must not outlive it.
			await RevokeShareAccessForPartiesAsync(link);
			await NotifyAccessReducedAsync(link, null, cancellationToken);

			return deleted;
		}

		/// <summary>
		/// Round 10 A3: set subscriptions, LMS sponsorships and countersignature requests made over the link stop with it. Never fatal to the
		/// link change: every read re-checks the link and worker 100 catches up.
		/// </summary>
		private async Task NotifyAccessReducedAsync(DepartmentLink stored, DepartmentLink updated, CancellationToken cancellationToken)
		{
			if (stored == null)
				return;

			foreach (var listener in _accessListeners)
			{
				try
				{
					await listener.OnLinkAccessReducedAsync(stored, updated, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Department link {stored.DepartmentLinkId} access reduction could not be applied to its subscribers; worker 100 will.");
				}
			}
		}

		public async Task<string> GetShareAccessVersionAsync(int departmentId)
		{
			if (departmentId <= 0)
				return null;

			// Null means the cache is unavailable (GetOrAddStringAsync never returns null for an absent key): callers stop
			// realtime link fan-out rather than fall back to a fixed group a revoked connection may still be in.
			return await _cacheProvider.GetOrAddStringAsync(GetShareAccessVersionKey(departmentId), NewShareAccessVersion(), ShareAccessVersionSlidingExpiration);
		}

		public async Task<List<string>> GetShareGroupsForSubscriberAsync(int linkId, int viewerDepartmentId)
		{
			var groups = new List<string>();

			var link = await _departmentLinksRepository.GetByIdAsync(linkId);
			var otherDepartmentId = DepartmentLinkSharing.GetOtherDepartmentId(link, viewerDepartmentId);
			if (!otherDepartmentId.HasValue || !SharesAnyEventedKind(link, viewerDepartmentId))
				return groups;

			// Epoch first, then the authoritative read. Revocation writes the link before it rotates the epoch, so a caller
			// that gets the new epoch also gets the reduced link; one that got the old epoch joins a group nobody sends to.
			var version = await GetShareAccessVersionAsync(otherDepartmentId.Value);
			if (version == null)
				return null;

			link = await _departmentLinksRepository.GetByIdAsync(linkId);
			foreach (var shareType in DepartmentLinkSharing.EventedShareTypes)
			{
				// Only the department whose epoch was read: a link whose parties changed in between joins nothing.
				if (DepartmentLinkSharing.GetSharedDepartmentId(link, viewerDepartmentId, shareType) == otherDepartmentId)
					groups.Add(DepartmentLinkSharing.GetShareGroupName(otherDepartmentId.Value, shareType, version));
			}

			return groups;
		}

		public async Task<string> GetShareGroupNameAsync(int departmentId, DepartmentLinkShareTypes shareType)
		{
			if (departmentId <= 0)
				return null;

			// A plain read: an absent epoch means no connection can be in any of this department's share groups.
			return DepartmentLinkSharing.GetShareGroupName(departmentId, shareType, await _cacheProvider.GetStringAsync(GetShareAccessVersionKey(departmentId)));
		}

		private static bool SharesAnyEventedKind(DepartmentLink link, int viewerDepartmentId)
		{
			foreach (var shareType in DepartmentLinkSharing.EventedShareTypes)
			{
				if (DepartmentLinkSharing.GetSharedDepartmentId(link, viewerDepartmentId, shareType).HasValue)
					return true;
			}

			return false;
		}

		public async Task<bool> RevokeShareAccessAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			// A fresh random epoch rather than an increment: once a key lapses, a counter would restart at a value an
			// obsolete group (still holding a revoked connection) may already carry.
			var stored = await _cacheProvider.SetStringAsync(GetShareAccessVersionKey(departmentId), NewShareAccessVersion(), ShareAccessVersionSlidingExpiration);

			if (!stored && Config.SystemBehaviorConfig.CacheEnabled)
				Framework.Logging.LogError($"Unable to rotate the department link share access epoch for department {departmentId}; existing link subscriptions keep receiving its events until they reconnect.");

			return stored;
		}

		private async Task RevokeShareAccessForPartiesAsync(DepartmentLink link)
		{
			if (link == null)
				return;

			// Each side may share toward the other, so both sides' share groups are rotated.
			await RevokeShareAccessAsync(link.DepartmentId);

			if (link.LinkedDepartmentId != link.DepartmentId)
				await RevokeShareAccessAsync(link.LinkedDepartmentId);
		}

		private static string GetShareAccessVersionKey(int departmentId) => $"deptlinkshareaccessver:{departmentId}";

		private static string NewShareAccessVersion() => Guid.NewGuid().ToString("N");

		public async Task<bool> DoesLinkExistAsync(int departmentId, int departmentLinkId)
		{
			return (from dl in await _departmentLinksRepository.GetAllLinksForDepartmentAndIdAsync(departmentId, departmentLinkId)
				select dl).Any();
		}

		public async Task<Department> GetDepartmentByLinkCodeAsync(string code)
		{
			return await _departmentsRepository.GetByLinkCodeAsync(code);
		}
	}
}
