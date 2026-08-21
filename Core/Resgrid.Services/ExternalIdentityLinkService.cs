using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ExternalIdentityLinkService : IExternalIdentityLinkService
	{
		private readonly IUserExternalIdentityLinksRepository _linksRepository;
		private readonly IDepartmentSsoConfigRepository _ssoConfigRepository;
		private readonly IDepartmentMembersRepository _departmentMembersRepository;

		public ExternalIdentityLinkService(IUserExternalIdentityLinksRepository linksRepository,
			IDepartmentSsoConfigRepository ssoConfigRepository,
			IDepartmentMembersRepository departmentMembersRepository)
		{
			_linksRepository = linksRepository;
			_ssoConfigRepository = ssoConfigRepository;
			_departmentMembersRepository = departmentMembersRepository;
		}

		public Task<UserExternalIdentityLink> GetBySubjectAsync(string departmentSsoConfigId, string externalSubject,
			CancellationToken cancellationToken = default) =>
			_linksRepository.GetActiveBySubjectAsync(departmentSsoConfigId, externalSubject);

		public async Task<UserExternalIdentityLink> SaveAsync(UserExternalIdentityLink link, CancellationToken cancellationToken = default)
		{
			if (link == null)
				throw new ArgumentNullException(nameof(link));
			if (string.IsNullOrWhiteSpace(link.UserId) || string.IsNullOrWhiteSpace(link.DepartmentSsoConfigId) ||
				string.IsNullOrWhiteSpace(link.ExternalSubject) || string.IsNullOrWhiteSpace(link.Issuer))
				throw new ArgumentException("An external identity link must be scoped to a user, configuration, issuer, and subject.", nameof(link));

			link.UserExternalIdentityLinkId ??= Guid.NewGuid().ToString("N");
			link.LinkedOn = link.LinkedOn == default ? DateTime.UtcNow : link.LinkedOn;
			link.IsActive = true;
			return await _linksRepository.SaveOrUpdateAsync(link, cancellationToken, true);
		}

		public async Task<SsoManagementState> GetSsoManagementStateAsync(string userId, CancellationToken cancellationToken = default)
		{
			var links = await _linksRepository.GetActiveByUserAsync(userId);
			var legacyMembers = (await _departmentMembersRepository.GetAllDepartmentMemberByUserIdAsync(userId))?
				.Where(member => !member.IsDeleted &&
					(!string.IsNullOrWhiteSpace(member.ExternalSsoId) || member.SsoLinkedOn.HasValue))
				.ToList() ?? new System.Collections.Generic.List<DepartmentMember>();
			return new SsoManagementState
			{
				IsSsoManaged = links.Count > 0 || legacyMembers.Count > 0,
				IsScimManaged = links.Any(link => link.LinkMethod == (int)ExternalIdentityLinkMethod.Scim),
				// Legacy links did not record the linking attribute. Treat their email as
				// externally managed until an administrator performs an explicit unlink.
				IsEmailExternallyManaged = legacyMembers.Count > 0 || links.Any(link => link.IsEmailExternallyManaged),
				ProviderNames = links.Select(link => $"{link.ProviderType}:{link.Issuer}")
					.Concat(legacyMembers.Select(_ => "Legacy SSO link")).Distinct().ToList()
			};
		}

		public async Task<bool> IsLocalLoginAllowedAsync(string userId, int departmentId,
			CancellationToken cancellationToken = default)
		{
			var links = (await _linksRepository.GetActiveByUserAsync(userId))
				.Where(link => link.DepartmentId == departmentId)
				.ToList();
			var member = await _departmentMembersRepository.GetDepartmentMemberByDepartmentIdAndUserIdAsync(departmentId, userId);
			var hasLegacyLink = member != null &&
				(!string.IsNullOrWhiteSpace(member.ExternalSsoId) || member.SsoLinkedOn.HasValue);

			if (links.Count == 0 && !hasLegacyLink)
				return true;

			var configs = (await _ssoConfigRepository.GetAllByDepartmentIdAsync(departmentId))?.ToList()
				?? new System.Collections.Generic.List<DepartmentSsoConfig>();
			var linkedConfigIds = links.Select(link => link.DepartmentSsoConfigId).ToHashSet(StringComparer.Ordinal);
			var applicable = configs.Where(config => config.IsEnabled &&
				(hasLegacyLink || linkedConfigIds.Contains(config.DepartmentSsoConfigId))).ToList();

			// A durable SSO link with a missing/disabled config is not a reason to silently
			// re-enable a local password. Administrative repair is required.
			return applicable.Count > 0 && applicable.All(config => config.AllowLocalLogin);
		}

		public async Task<bool> IsLocalLoginAllowedAsync(string userId, CancellationToken cancellationToken = default)
		{
			var links = (await _linksRepository.GetActiveByUserAsync(userId)).ToList();
			if (links.Count == 0)
				return true;

			foreach (var departmentLinks in links.GroupBy(link => link.DepartmentId))
			{
				var configs = (await _ssoConfigRepository.GetAllByDepartmentIdAsync(departmentLinks.Key))?.ToList()
					?? new System.Collections.Generic.List<DepartmentSsoConfig>();
				var linkedConfigIds = departmentLinks.Select(link => link.DepartmentSsoConfigId)
					.ToHashSet(StringComparer.Ordinal);
				var applicable = configs.Where(config => config.IsEnabled &&
					linkedConfigIds.Contains(config.DepartmentSsoConfigId)).ToList();

				if (applicable.Count != linkedConfigIds.Count || applicable.Any(config => !config.AllowLocalLogin))
					return false;
			}

			return true;
		}
	}
}
