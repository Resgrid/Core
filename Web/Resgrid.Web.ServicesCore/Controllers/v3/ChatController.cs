using System;
using System.Collections.Generic;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Chat;
using Resgrid.Model;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Events;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to be performed against the chat system
	/// </summary>
	[Produces("application/json")]
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class ChatController : V3AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUsersService _usersService;
		private readonly ICommunicationService _communicationService;
		private readonly ICqrsProvider _cqrsProvider;

		public ChatController(
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IDepartmentGroupsService departmentGroupsService,
			IUsersService usersService,
			ICommunicationService communicationService,
			ICqrsProvider cqrsProvider
			)
		{
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_departmentGroupsService = departmentGroupsService;
			_usersService = usersService;
			_communicationService = communicationService;
			_cqrsProvider = cqrsProvider;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the department and group information needed to seed the initial standard groups for chat
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetResponderChatInfo")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ResponderChatResult>> GetResponderChatInfo()
		{
			var result = new ResponderChatResult();
			result.UserId = UserId;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			var departmentGroup = new ResponderChatGroupInfo();
			departmentGroup.Id = $"{Config.ChatConfig.DepartmentChatPrefix}{department.Code}";
			departmentGroup.Name = department.Name;
			departmentGroup.Type = 1;
			result.Groups.Add(departmentGroup);

			if (department.IsUserAnAdmin(UserId))
			{
				foreach (var group in groups)
				{
					var newGroup = new ResponderChatGroupInfo() { Id = $"{Config.ChatConfig.DepartmentGroupChatPrefix}{department.Code}_{group.DepartmentGroupId}", Name = group.Name, Count = group.Members.Count };

					if (group.Type.HasValue)
					{
						newGroup.Type = 3;
					}
					else if (group.Type.GetValueOrDefault() == (int)DepartmentGroupTypes.Station)
					{
						newGroup.Type = 2;
					}
					else if (group.Type.GetValueOrDefault() == (int)DepartmentGroupTypes.Orginizational)
					{
						newGroup.Type = 3;
					}

					result.Groups.Add(newGroup);
				}
			}
			else
			{
				var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
				if (group != null)
				{
					var newGroup = new ResponderChatGroupInfo() { Id = $"{Config.ChatConfig.DepartmentGroupChatPrefix}{department.Code}_{group.DepartmentGroupId}", Name = group.Name, Count = group.Members.Count };

					if (group.Type.HasValue)
					{
						newGroup.Type = 3;
					}
					else if (group.Type.GetValueOrDefault() == (int)DepartmentGroupTypes.Station)
					{
						newGroup.Type = 2;
					}
					else if (group.Type.GetValueOrDefault() == (int)DepartmentGroupTypes.Orginizational)
					{
						newGroup.Type = 3;
					}

					result.Groups.Add(newGroup);
				}
			}

			return Ok(result);
		}

		/// <summary>
		/// Gets personnel information for the department to utilize within a chat system
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetPersonnelForChat")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<PersonnelChatResult>>> GetPersonnelForChat()
		{
			var result = new List<PersonnelChatResult>();
			var personnel = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);

			foreach (var person in personnel)
			{
				if (person.UserId != UserId)
				{
					var presult = new PersonnelChatResult();
					presult.UserId = person.UserId;
					presult.Name = person.Name;
					presult.Group = person.DepartmentGroupName;
					presult.Roles = person.RoleNames;


					result.Add(presult);
				}
			}

			return Ok(result);
		}

		/// <summary>
		/// Send a Push Notification out to the personnel involved in a chat
		/// </summary>
		/// <param name="notifyChatInput"></param>
		/// <returns></returns>
		[HttpPost("NotifyNewChat")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult> NotifyNewChat([FromBody] NotifyChatInput notifyChatInput)
		{
			if (notifyChatInput != null && notifyChatInput.RecipientUserIds != null && notifyChatInput.RecipientUserIds.Count > 0)
			{
				var newChatEvent = new NewChatNotificationEvent();
				newChatEvent.Id = notifyChatInput.Id;
				newChatEvent.GroupName = notifyChatInput.GroupName;
				newChatEvent.Message = notifyChatInput.Message;
				newChatEvent.RecipientUserIds = notifyChatInput.RecipientUserIds;
				newChatEvent.SendingUserId = notifyChatInput.SendingUserId;
				newChatEvent.Type = notifyChatInput.Type;
				newChatEvent.DepartmentId = DepartmentId;

				CqrsEvent registerUnitPushEvent = new CqrsEvent();
				registerUnitPushEvent.Type = (int)CqrsEventTypes.NewChatMessage;
				registerUnitPushEvent.Timestamp = DateTime.UtcNow;
				registerUnitPushEvent.Data = ObjectSerialization.Serialize(newChatEvent);

				await _cqrsProvider.EnqueueCqrsEventAsync(registerUnitPushEvent);
			}

			return Ok();
		}
	}
}
