using System;
using System.Collections.Generic;
using Resgrid.Model.Services;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Controllers.Version3.Models.Chat;
using Resgrid.Model;
using System.Net.Http;
using System.Net;
using System.Web.Http;
using Resgrid.Model.Events;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to be performed against the chat system
	/// </summary>
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
		public ResponderChatResult GetResponderChatInfo()
		{
			var result = new ResponderChatResult();
			result.UserId = UserId;

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

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
				var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);
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

			return result;
		}

		/// <summary>
		/// Gets personnel information for the department to utilize within a chat system
		/// </summary>
		/// <returns></returns>
		public List<PersonnelChatResult> GetPersonnelForChat()
		{
			var result = new List<PersonnelChatResult>();
			var personnel = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, false, false, false);

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

			return result;
		}

		/// <summary>
		/// Send a Push Notification out to the personnel involved in a chat
		/// </summary>
		/// <param name="notifyChatInput"></param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage NotifyNewChat([FromBody] NotifyChatInput notifyChatInput)
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

				CqrsEvent registerUnitPushEvent = new CqrsEvent();
				registerUnitPushEvent.Type = (int)CqrsEventTypes.NewChatMessage;
				registerUnitPushEvent.Timestamp = DateTime.UtcNow;
				registerUnitPushEvent.Data = ObjectSerialization.Serialize(newChatEvent);

				_cqrsProvider.EnqueueCqrsEvent(registerUnitPushEvent);
			}

			return Request.CreateResponse(HttpStatusCode.OK);
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
