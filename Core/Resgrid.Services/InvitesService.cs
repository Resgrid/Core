using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class InvitesService : IInvitesService
	{
		private readonly IInvitesRepository _invitesRepository;
		private readonly IEmailService _emailService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;

		public InvitesService(IInvitesRepository invitesRepository, IEmailService emailService, IDepartmentsService departmentsService,
			IUsersService usersService, IUserProfileService userProfileService)
		{
			_invitesRepository = invitesRepository;
			_emailService = emailService;
			_departmentsService = departmentsService;
			_usersService = usersService;
			_userProfileService = userProfileService;
		}

		public async Task<List<Invite>> GetAllInvitesForDepartmentAsync(int departmentId)
		{
			var invites = await _invitesRepository.GetAllByDepartmentIdAsync(departmentId);

			return invites.ToList();
		}

		public async Task<bool> CreateInvitesAsync(Department department, string addingUserId, List<string> emailAddresses, CancellationToken cancellationToken = default(CancellationToken))
		{
			var sendingUser = _usersService.GetUserById(addingUserId);
			var sendingProfile = await _userProfileService.GetProfileByUserIdAsync(addingUserId);

			for (int i = 0; i < emailAddresses.Count; i++)
			{

				Invite invite = new Invite();
				invite.Code = Guid.NewGuid();
				invite.DepartmentId = department.DepartmentId;
				invite.EmailAddress = emailAddresses[i];
				invite.SendingUserId = addingUserId;
				invite.SentOn = DateTime.UtcNow;

				await _invitesRepository.SaveOrUpdateAsync(invite, cancellationToken);

				if (invite.Department == null && department != null)
					invite.Department = department;
				else if (invite.Department == null)
					invite.Department = await _departmentsService.GetDepartmentByIdAsync(department.DepartmentId);

				
				await _emailService.SendInviteAsync(invite, sendingProfile.FullName.AsFirstNameLastName, sendingUser.Email);
			}

			return true;
		}

		public async Task<Invite> GetInviteByCodeAsync(Guid inviteCode)
		{
			return await _invitesRepository.GetInviteByCodeAsync(inviteCode);
		}

		public async Task<Invite> CompleteInviteAsync(Guid inviteCode, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var invite = await GetInviteByCodeAsync(inviteCode);
			invite.CompletedOn = DateTime.UtcNow;
			invite.CompletedUserId = userId;

			return await _invitesRepository.SaveOrUpdateAsync(invite, cancellationToken);
		}

		public async Task<Invite> ResendInviteAsync(int inviteId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var invite = await GetInviteByIdAsync(inviteId);

			if (invite != null)
			{
				var sendingUser = _usersService.GetUserById(invite.SendingUserId);
				var sendingProfile = await _userProfileService.GetProfileByUserIdAsync(invite.SendingUserId);

				invite.SentOn = DateTime.UtcNow;
				invite = await _invitesRepository.SaveOrUpdateAsync(invite, cancellationToken);
				await _emailService.SendInviteAsync(invite, sendingProfile.FullName.AsFirstNameLastName, sendingUser.Email);
			}

			return invite;
		}

		public async Task<bool> DeleteInviteAsync(int inviteId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var invite = await GetInviteByIdAsync(inviteId);

			if (invite != null)
			{
				return await _invitesRepository.DeleteAsync(invite, cancellationToken);
			}

			return false;
		}

		public async Task<Invite> GetInviteByIdAsync(int inviteId)
		{
			return await _invitesRepository.GetByIdAsync(inviteId);
		}

		public async Task<Invite> GetInviteByEmailAsync(string emailAddress)
		{
			return await _invitesRepository.GetInviteByEmailAsync(emailAddress);
		}
	}
}
