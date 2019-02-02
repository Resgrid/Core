using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Messages;
using Resgrid.Web.Helpers;
using RestSharp.Extensions.MonoHttp;
using Microsoft.AspNetCore.Authorization;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class MessagesController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IMessageService _messageService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly ICommunicationService _communicationService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IShiftsService _shiftsService;

		public MessagesController(IMessageService messageService, IDepartmentsService departmentsService, IUsersService usersService,
			ICommunicationService communicationService, Model.Services.IAuthorizationService authorizationService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService, IShiftsService shiftsService)
		{
			_messageService = messageService;
			_departmentsService = departmentsService;
			_usersService = usersService;
			_communicationService = communicationService;
			_authorizationService = authorizationService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_shiftsService = shiftsService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Messages_View)]
		public IActionResult Inbox()
		{
			MessagesInboxModel model = new MessagesInboxModel();
			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			
			model.User = _usersService.GetUserById(UserId);
			model.Messages = _messageService.GetInboxMessagesByUserId(UserId);
			model.UnreadMessages = _messageService.GetUnreadMessagesCountByUserId(UserId);
			
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public IActionResult Outbox()
		{
			MessagesOutboxModel model = new MessagesOutboxModel();
			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			
			model.User = _usersService.GetUserById(UserId);
			model.Messages = _messageService.GetSentMessagesByUserId(UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		public IActionResult Compose()
		{
			ComposeMessageModel model = new ComposeMessageModel();
			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(UserId);
			model.Types = model.MessageType.ToSelectList();

			var shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);
			model.Shifts = new SelectList(shifts, "ShiftId", "Name");

			model.Message = new Message();

			return View(FillComposeMessageModel(model));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		public IActionResult Compose(ComposeMessageModel model, IFormCollection collection)
		{
			var roles = new List<string>();
			var groups = new List<string>();
			var users = new List<string>();
			var shifts = new List<string>();

			if (collection.ContainsKey("roles"))
				roles.AddRange(collection["roles"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("groups"))
				groups.AddRange(collection["groups"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("users"))
				users.AddRange(collection["users"].ToString().Split(char.Parse(",")));

			if (collection.ContainsKey("exludedShifts"))
				shifts.AddRange(collection["exludedShifts"].ToString().Split(char.Parse(",")));

			if (!model.SendToAll && (roles.Count + groups.Count + users.Count) == 0)
				ModelState.AddModelError("", "You must specify at least one Recipient.");

			if (model.SendToMatchOnly && (roles.Count + groups.Count) == 0)
				ModelState.AddModelError("", "You must specify at least one Group or Role to send to.");

			if (ModelState.IsValid)
			{
				var excludedUsers = new List<string>();

				if (shifts.Any())
				{
					foreach (var shiftId in shifts)
					{
						var shift = _shiftsService.GetShiftById(int.Parse(shiftId));
						excludedUsers.AddRange(shift.Personnel.Select(x => x.UserId));
					}
				}
				
				model.Message.Type = (int)model.MessageType;
				if (model.SendToAll)
				{
					var allUsers = _departmentsService.GetAllUsersForDepartment(DepartmentId);
					foreach (var user in allUsers)
					{
						if (user.UserId != UserId && (!excludedUsers.Any() || !excludedUsers.Contains(user.UserId)))
							model.Message.AddRecipient(user.UserId);
					}
				}
				else if (model.SendToMatchOnly)
				{
					var usersInRoles = new Dictionary<int, List<string>>();

					foreach (var role in roles)
					{
						var roleMembers = _personnelRolesService.GetAllMembersOfRole(int.Parse(role));
						usersInRoles.Add(int.Parse(role), roleMembers.Select(x => x.UserId).ToList());
					}

					foreach (var group in groups)
					{
						var members = _departmentGroupsService.GetAllMembersForGroup(int.Parse(group));

						foreach (var member in members)
						{
							bool isInRoles = false;
							if (model.Message.GetRecipients().All(x => x != member.UserId) && member.UserId != UserId &&
							    (!excludedUsers.Any() || !excludedUsers.Contains(member.UserId)))
							{
								foreach (var key in usersInRoles)
								{
									if (key.Value.Any(x => x == member.UserId))
									{
										isInRoles = true;
										break;
									}
								}

								if (isInRoles)
									model.Message.AddRecipient(member.UserId);
							}
								
						}
					}
				}
				else
				{
					foreach (var user in users)
					{
						model.Message.AddRecipient(user);
					}

					// Add all members of the group
					foreach (var group in groups)
					{
						var members = _departmentGroupsService.GetAllMembersForGroup(int.Parse(group));

						foreach (var member in members)
						{
							if (model.Message.GetRecipients().All(x => x != member.UserId) && member.UserId != UserId && (!excludedUsers.Any() || !excludedUsers.Contains(member.UserId)))
								model.Message.AddRecipient(member.UserId);
						}
					}

					// Add all the users of a specific role
					foreach (var role in roles)
					{
						var roleMembers = _personnelRolesService.GetAllMembersOfRole(int.Parse(role));

						foreach (var member in roleMembers)
						{
							if (model.Message.GetRecipients().All(x => x != member.UserId) && member.UserId != UserId && (!excludedUsers.Any() || !excludedUsers.Contains(member.UserId)))
								model.Message.AddRecipient(member.UserId);
						}
					}
				}
				
				model.Message.SentOn = DateTime.UtcNow;
				model.Message.SendingUserId = UserId;
				model.Message.Body = System.Net.WebUtility.HtmlDecode(model.Message.Body);
				model.Message.IsBroadcast = true;

				var savedMessage = _messageService.SaveMessage(model.Message);
				_messageService.SendMessage(savedMessage, "", DepartmentId, false);

				return RedirectToAction("Inbox");
			}

			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.User = _usersService.GetUserById(UserId);
			model.Types = model.MessageType.ToSelectList();

			var savedShifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);
			model.Shifts = new SelectList(savedShifts, "ShiftId", "Name");
			model.Message.Body = System.Net.WebUtility.HtmlDecode(model.Message.Body);

			return View(FillComposeMessageModel(model));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public IActionResult ViewMessage(int messageId)
		{
			if (!_authorizationService.CanUserViewMessage(UserId, messageId))
				Unauthorized();

			ViewMessageView model = new ViewMessageView();
			model.User = _usersService.GetUserById(UserId);
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.Message = _messageService.GetMessageById(messageId);
			model.UnreadMessages = _messageService.GetUnreadMessagesCountByUserId(UserId);
			model.UserGroupsAndRoles = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, true, true, true);
			_messageService.ReadMessageRecipient(messageId, UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public IActionResult MessageResponse(int recipientId, int response, string note)
		{
			var messageRecipient = _messageService.GetMessageRecipientById(recipientId);

			if (messageRecipient != null)
			{
				messageRecipient.Response = response.ToString();
				messageRecipient.ReadOn = DateTime.UtcNow;
				messageRecipient.Note = HttpUtility.UrlDecode(note);

				_messageService.SaveMessageRecipient(messageRecipient);
			}

			return RedirectToAction("Inbox");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public IActionResult DeleteMessage(int messageId)
		{
			if (!_authorizationService.CanUserViewMessage(UserId, messageId))
				Unauthorized();

			var message = _messageService.GetMessageByIdForEditing(messageId);

			if (!String.IsNullOrWhiteSpace(message.ReceivingUserId))
				_messageService.MarkMessageAsDeleted(messageId);
			else
				_messageService.MarkMessageAsDeleted(messageId, UserId);
			
			return RedirectToAction("Inbox");
		}

		[HttpDelete]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public IActionResult DeleteMessages(string messageIds)
		{
			var messages = messageIds.Split(char.Parse(","));

			foreach (var messageId in messages)
			{
				var id = int.Parse(messageId);

				if (!_authorizationService.CanUserViewMessage(UserId, id))
					Unauthorized();

				var message = _messageService.GetMessageById(id);

				if (!String.IsNullOrWhiteSpace(message.ReceivingUserId))
					_messageService.MarkMessageAsDeleted(id);
				else
					_messageService.MarkMessageAsDeleted(id, UserId);
			}

			return RedirectToAction("Inbox");
		}

		[HttpPut]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		public IActionResult MarkMessagesAsRead(string messageIds)
		{
			var messages = messageIds.Split(char.Parse(","));

			foreach (var messageId in messages)
			{
				var id = int.Parse(messageId);

				if (!_authorizationService.CanUserViewMessage(UserId, id))
					Unauthorized();

				_messageService.ReadMessageRecipient(id, UserId);
			}

			return RedirectToAction("Inbox");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public IActionResult DeleteOutboxMessage(int messageId)
		{
			if (!_authorizationService.CanUserViewMessage(UserId, messageId))
				Unauthorized();

			_messageService.MarkMessageAsDeleted(messageId);

			return RedirectToAction("Outbox");
		}

		[HttpDelete]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public IActionResult DeleteOutboxMessages(string messageIds)
		{
			var messages = messageIds.Split(char.Parse(","));

			foreach (var messageId in messages)
			{
				var id = int.Parse(messageId);

				if (!_authorizationService.CanUserViewMessage(UserId, id))
					Unauthorized();

				_messageService.MarkMessageAsDeleted(id);
			}

			return RedirectToAction("Outbox");
		}

		public IActionResult GetTopUnreadMessages()
		{
			var model = new TopUnreadMessagesView();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.UnreadMessages = _messageService.GetUnreadInboxMessagesByUserId(UserId);

			return PartialView("_UnreadTopMessagesPartial", model);
		}

		#region Private Helpers
		private ComposeMessageModel FillComposeMessageModel(ComposeMessageModel model)
		{
			model.Department = _departmentsService.GetDepartmentByUserId(UserId);
			model.User = _usersService.GetUserById(UserId);
			
			//model.Users = _departmentsService.GetAllUsersForDepartment(model.Department.DepartmentId);

			return model;
		}

		[HttpGet]
		
		public IActionResult GetInboxMessageList()
		{
			var messagesJson = new List<MessageJson>();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var messages = _messageService.GetInboxMessagesByUserId(UserId);

			foreach (var message in messages)
			{
				var json = new MessageJson();
				json.MessageId = message.MessageId;
				json.Subject = message.Subject;
				json.SystemGenerated = message.SystemGenerated;
				json.Body = message.Body;
				json.SentOn = message.SentOn.FormatForDepartment(department);
				json.IsDeleted = message.IsDeleted;
				json.ReadOn = message.ReadOn;
				json.Type = message.Type;
				json.ExpireOn = message.ExpireOn;
				json.Read = message.HasUserRead(UserId);

				if (message.SystemGenerated)
					json.SentBy = "System";
				else if (!String.IsNullOrWhiteSpace(message.SendingUserId))
					json.SentBy = UserHelper.GetFullNameForUser(message.SendingUserId);
				else
					json.SentBy = "System";

				messagesJson.Add(json);
			}

			return Json(messagesJson);
		}

		[HttpGet]
		
		public IActionResult GetOutboxMessageList()
		{
			var messagesJson = new List<MessageJson>();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var messages = _messageService.GetSentMessagesByUserId(UserId);

			foreach (var message in messages)
			{
				var json = new MessageJson();
				json.MessageId = message.MessageId;
				json.Subject = message.Subject;
				json.SystemGenerated = message.SystemGenerated;
				json.Body = message.Body;
				json.SentOn = message.SentOn.FormatForDepartment(department);
				json.IsDeleted = message.IsDeleted;
				json.ReadOn = message.ReadOn;
				json.Type = message.Type;
				json.ExpireOn = message.ExpireOn;

				messagesJson.Add(json);
			}

			return Json(messagesJson);
		}
		#endregion Private Helpers
	}
}
