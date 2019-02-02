using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class InvitesService : IInvitesService
	{
		private readonly IGenericDataRepository<Invite> _invitesRepository;
		private readonly IEmailService _emailService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IUserProfileService _userProfileService;

		public InvitesService(IGenericDataRepository<Invite> invitesRepository, IEmailService emailService, IDepartmentsService departmentsService,
			IUsersService usersService, IUserProfileService userProfileService)
		{
			_invitesRepository = invitesRepository;
			_emailService = emailService;
			_departmentsService = departmentsService;
			_usersService = usersService;
			_userProfileService = userProfileService;
		}

		public List<Invite> GetAllInvitesForDepartment(int departmentId)
		{
			var invites = from i in _invitesRepository.GetAll()
			              where i.DepartmentId == departmentId
			              select i;

			return invites.ToList();
		}

		public void CreateInvites(Department department, string addingUserId, List<string> emailAddresses)
		{
			var sendingUser = _usersService.GetUserById(addingUserId);
			var sendingProfile = _userProfileService.GetProfileByUserId(addingUserId);

			for (int i = 0; i < emailAddresses.Count; i++)
			{

				Invite invite = new Invite();
				invite.Code = Guid.NewGuid();
				invite.DepartmentId = department.DepartmentId;
				invite.EmailAddress = emailAddresses[i];
				invite.SendingUserId = addingUserId;
				invite.SentOn = DateTime.Now.ToUniversalTime();

				_invitesRepository.SaveOrUpdate(invite);

				if (invite.Department == null)
					invite.Department = _departmentsService.GetDepartmentById(department.DepartmentId);

				

				_emailService.SendInvite(invite, sendingProfile.FullName.AsFirstNameLastName, sendingUser.Email);
			}

			//foreach (var email in emailAddresses)
			//{
			//	Invite invite = new Invite();
			//	invite.Code = Guid.NewGuid();
			//	invite.DepartmentId = department.DepartmentId;
			//	invite.EmailAddress = email;
			//	invite.SendingUserId = addingUserId;
			//	invite.SentOn = DateTime.Now.ToUniversalTime();

			//	_invitesRepository.SaveOrUpdate(invite);

			//	if (invite.Department == null)
			//		invite.Department = _departmentsService.GetDepartmentById(department.DepartmentId);

			//	_emailService.SendInvite(invite);
			//}
		}

		public Invite GetInviteByCode(Guid inviteCode)
		{
			var invite = from i in _invitesRepository.GetAll()
			             where i.Code == inviteCode
			             select i;

			return invite.FirstOrDefault();
		}

		public void CompleteInvite(Guid inviteCode, string userId)
		{
			var invite = GetInviteByCode(inviteCode);
			invite.CompletedOn = DateTime.UtcNow;
			invite.CompletedUserId = userId;

			_invitesRepository.SaveOrUpdate(invite);
		}

		public void ResendInvite(int inviteId)
		{
			var invite = GetInviteById(inviteId);

			if (invite != null)
			{
				var sendingUser = _usersService.GetUserById(invite.SendingUserId);
				var sendingProfile = _userProfileService.GetProfileByUserId(invite.SendingUserId);

				invite.SentOn = DateTime.UtcNow;
				_invitesRepository.SaveOrUpdate(invite);
				_emailService.SendInvite(invite, sendingProfile.FullName.AsFirstNameLastName, sendingUser.Email);
			}
		}

		public void DeleteInvite(int inviteId)
		{
			var invite = GetInviteById(inviteId);

			if (invite != null)
			{
				_invitesRepository.DeleteOnSubmit(invite);
			}
		}

		public Invite GetInviteById(int inviteId)
		{
			return _invitesRepository.GetAll().FirstOrDefault(x => x.InviteId == inviteId);
		}

		public Invite GetInviteByEmail(string emailAddress)
		{
			var invite = from i in _invitesRepository.GetAll()
									 where i.EmailAddress == emailAddress
									 select i;

			return invite.FirstOrDefault();
		}
	}
}