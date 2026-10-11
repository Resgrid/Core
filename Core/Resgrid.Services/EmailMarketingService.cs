using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class EmailMarketingService : IEmailMarketingService
	{
		public const string ManagingMemberUserGroup = "Managing Member";
		public const string DepartmentAdminUserGroup = "Department Admin";

		private readonly IEmailMarketingProvider _emailMarketingProvider;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentMembersRepository _departmentMembersRepository;
		private readonly IDepartmentsRepository _departmentsRepository;

		// Repositories rather than IDepartmentsService: DepartmentsService calls this service when an admin or the
		// managing member changes, so depending back on it would be a construction cycle.
		public EmailMarketingService(IEmailMarketingProvider emailMarketingProvider, IUsersService usersService, IUserProfileService userProfileService,
			IDepartmentMembersRepository departmentMembersRepository, IDepartmentsRepository departmentsRepository)
		{
			_emailMarketingProvider = emailMarketingProvider;
			_usersService = usersService;
			_userProfileService = userProfileService;
			_departmentMembersRepository = departmentMembersRepository;
			_departmentsRepository = departmentsRepository;
		}

		public bool IsEnabled => _emailMarketingProvider.IsConfigured;

		public async Task<bool> SyncDepartmentAdministratorAsync(Department department, string userId, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled || department == null || string.IsNullOrWhiteSpace(userId))
				return false;

			try
			{
				var contact = await BuildAdministratorContactAsync(department, userId);
				if (contact == null)
					return false;

				return await _emailMarketingProvider.UpsertContactAsync(contact, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Email marketing sync failed for administrator {userId} of department {department.DepartmentId}");
				return false;
			}
		}

		public async Task<bool> SyncNewsletterPreferenceAsync(string userId, bool doNotReceiveNewsletters, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled || string.IsNullOrWhiteSpace(userId))
				return false;

			try
			{
				var administeredDepartment = await GetAdministeredDepartmentAsync(userId);
				if (administeredDepartment != null)
				{
					var contact = await BuildAdministratorContactAsync(administeredDepartment, userId);
					if (contact == null)
						return false;

					// The user made this choice, so it may re-subscribe as well as unsubscribe.
					contact.Subscribed = !doNotReceiveNewsletters;
					return await _emailMarketingProvider.UpsertContactAsync(contact, cancellationToken);
				}

				// Not an administrator: honour the choice on a contact the platform already holds (a former admin), but
				// never add a regular member to the audience.
				var user = _usersService.GetUserById(userId);
				if (user == null || string.IsNullOrWhiteSpace(user.Email))
					return false;

				if (!await _emailMarketingProvider.ContactExistsAsync(userId, user.Email, cancellationToken))
					return false;

				return await _emailMarketingProvider.UpsertContactAsync(new EmailMarketingContact
				{
					UserId = userId,
					EmailAddress = user.Email,
					Subscribed = !doNotReceiveNewsletters
				}, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Email marketing newsletter preference sync failed for user {userId}");
				return false;
			}
		}

		private async Task<EmailMarketingContact> BuildAdministratorContactAsync(Department department, string userId)
		{
			var user = _usersService.GetUserById(userId);
			if (user == null || string.IsNullOrWhiteSpace(user.Email))
				return null;

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId, true);
			var isManagingMember = string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);

			var contact = new EmailMarketingContact
			{
				UserId = userId,
				EmailAddress = user.Email,
				FirstName = profile?.FirstName,
				LastName = profile?.LastName,
				// Becoming an admin is not consent, so this never subscribes: an unsubscribe the person made from an email
				// stands. It only carries an opt-out the person set on their Resgrid profile.
				Subscribed = profile != null && profile.DoNotRecieveNewsletters ? false : (bool?)null,
				UserGroup = isManagingMember ? ManagingMemberUserGroup : DepartmentAdminUserGroup,
				DepartmentId = department.DepartmentId,
				DepartmentName = department.Name,
				IsManagingMember = isManagingMember
			};

			if (!string.IsNullOrWhiteSpace(MarketingConfig.LoopsAdminMailingListId))
				contact.MailingListIds = new[] { MarketingConfig.LoopsAdminMailingListId };

			return contact;
		}

		/// <summary>
		/// The department the user manages, otherwise the first one they are an active admin of, otherwise null.
		/// Removed, disabled and hidden memberships do not count (inactive-member automation policy).
		/// </summary>
		private async Task<Department> GetAdministeredDepartmentAsync(string userId)
		{
			var memberships = await _departmentMembersRepository.GetAllDepartmentMemberByUserIdAsync(userId);
			if (memberships == null)
				return null;

			Department administered = null;
			foreach (var membership in memberships)
			{
				if (!DepartmentMemberStateHelper.IsActiveMember(membership, membership.DepartmentId))
					continue;

				var department = await _departmentsRepository.GetByIdAsync(membership.DepartmentId);
				if (department == null)
					continue;

				if (string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase))
					return department;

				if (membership.IsAdmin == true && administered == null)
					administered = department;
			}

			return administered;
		}
	}
}
