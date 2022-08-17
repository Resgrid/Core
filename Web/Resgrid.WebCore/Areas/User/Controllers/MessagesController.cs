using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
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
using Microsoft.AspNetCore.Authorization;
using System.Threading;

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
		public async Task<IActionResult> Inbox()
		{
			MessagesInboxModel model = new MessagesInboxModel();
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			
			model.User = _usersService.GetUserById(UserId);
			model.Messages = await _messageService.GetInboxMessagesByUserIdAsync(UserId);
			model.UnreadMessages = await _messageService.GetUnreadMessagesCountByUserIdAsync(UserId);
			
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<IActionResult> Outbox()
		{
			MessagesOutboxModel model = new MessagesOutboxModel();
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			
			model.User = _usersService.GetUserById(UserId);
			model.Messages = await _messageService.GetSentMessagesByUserIdAsync(UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		public async Task<IActionResult> Compose()
		{
			ComposeMessageModel model = new ComposeMessageModel();
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);
			model.Types = model.MessageType.ToSelectList();

			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);
			model.Shifts = new SelectList(shifts, "ShiftId", "Name");

			model.Message = new Message();

			return View(await FillComposeMessageModel(model));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		public async Task<IActionResult> Compose(ComposeMessageModel model, IFormCollection collection, CancellationToken cancellationToken)
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
						var shift = await _shiftsService.GetShiftByIdAsync(int.Parse(shiftId));
						excludedUsers.AddRange(shift.Personnel.Select(x => x.UserId));
					}
				}
				
				model.Message.Type = (int)model.MessageType;
				if (model.SendToAll)
				{
					var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
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
						var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(role));
						usersInRoles.Add(int.Parse(role), roleMembers.Select(x => x.UserId).ToList());
					}

					foreach (var group in groups)
					{
						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(int.Parse(group));

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
						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(int.Parse(group));

						foreach (var member in members)
						{
							if (model.Message.GetRecipients().All(x => x != member.UserId) && member.UserId != UserId && (!excludedUsers.Any() || !excludedUsers.Contains(member.UserId)))
								model.Message.AddRecipient(member.UserId);
						}
					}

					// Add all the users of a specific role
					foreach (var role in roles)
					{
						var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(role));

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

				var savedMessage = await _messageService.SaveMessageAsync(model.Message, cancellationToken);
				await _messageService.SendMessageAsync(savedMessage, "", DepartmentId, false, cancellationToken);

				return RedirectToAction("Inbox");
			}

			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.User = _usersService.GetUserById(UserId);
			model.Types = model.MessageType.ToSelectList();

			var savedShifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);
			model.Shifts = new SelectList(savedShifts, "ShiftId", "Name");
			model.Message.Body = System.Net.WebUtility.HtmlDecode(model.Message.Body);

			return View(await FillComposeMessageModel(model));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<IActionResult> ViewMessage(int messageId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewMessageAsync(UserId, messageId))
				Unauthorized();

			ViewMessageView model = new ViewMessageView();
			model.User = _usersService.GetUserById(UserId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Message = await _messageService.GetMessageByIdAsync(messageId);
			model.UnreadMessages = await _messageService.GetUnreadMessagesCountByUserIdAsync(UserId);
			model.UserGroupsAndRoles = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true);
			await _messageService.ReadMessageRecipientAsync(messageId, UserId, cancellationToken);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<IActionResult> MessageResponse(int recipientId, int response, string note, CancellationToken cancellationToken)
		{
			var messageRecipient = await _messageService.GetMessageRecipientByIdAsync(recipientId);

			if (messageRecipient != null)
			{
				messageRecipient.Response = response.ToString();
				messageRecipient.ReadOn = DateTime.UtcNow;
				messageRecipient.Note = HttpUtility.UrlDecode(note);

				await _messageService.SaveMessageRecipientAsync(messageRecipient, cancellationToken);
			}

			return RedirectToAction("Inbox");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<IActionResult> DeleteMessage(int messageId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewMessageAsync(UserId, messageId))
				Unauthorized();

			await _messageService.MarkMessagesAsDeletedAsync(UserId, new List<string>() { messageId.ToString() }, cancellationToken);

			return RedirectToAction("Inbox");
		}

		[HttpDelete]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<IActionResult> DeleteMessages(string messageIds, CancellationToken cancellationToken)
		{
			var messages = messageIds.Split(char.Parse(","));

			foreach (var messageId in messages)
			{
				var id = int.Parse(messageId);

				if (!await _authorizationService.CanUserViewMessageAsync(UserId, id))
					Unauthorized();
			}

			await _messageService.MarkMessagesAsDeletedAsync(UserId, messages.ToList(), cancellationToken);

			return RedirectToAction("Inbox");
		}

		[HttpPut]
		[Authorize(Policy = ResgridResources.Messages_Update)]
		public async Task<IActionResult> MarkMessagesAsRead(string messageIds, CancellationToken cancellationToken)
		{
			var messages = messageIds.Split(char.Parse(","));

			foreach (var messageId in messages)
			{
				var id = int.Parse(messageId);

				if (!await _authorizationService.CanUserViewMessageAsync(UserId, id))
					Unauthorized();
			}

			await _messageService.MarkMessagesAsReadAsync(UserId, messages.ToList(), cancellationToken);

			return RedirectToAction("Inbox");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<IActionResult> DeleteOutboxMessage(int messageId)
		{
			if (!await _authorizationService.CanUserViewMessageAsync(UserId, messageId))
				Unauthorized();

			await _messageService.MarkMessageAsDeletedAsync(messageId);

			return RedirectToAction("Outbox");
		}

		[HttpDelete]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<IActionResult> DeleteOutboxMessages(string messageIds, CancellationToken cancellationToken)
		{
			var messages = messageIds.Split(char.Parse(","));

			foreach (var messageId in messages)
			{
				var id = int.Parse(messageId);

				if (!await _authorizationService.CanUserViewMessageAsync(UserId, id))
					Unauthorized();

				await _messageService.MarkMessageAsDeletedAsync(id, cancellationToken);
			}

			return RedirectToAction("Outbox");
		}

		public async Task<IActionResult> GetTopUnreadMessages()
		{
			var model = new TopUnreadMessagesView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.UnreadMessages = await _messageService.GetUnreadInboxMessagesByUserIdAsync(UserId);

			return PartialView("_UnreadTopMessagesPartial", model);
		}

		private async Task<ComposeMessageModel> FillComposeMessageModel(ComposeMessageModel model)
		{
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);
			
			//model.Users = _departmentsService.GetAllUsersForDepartment(model.Department.DepartmentId);

			return model;
		}

		[HttpGet]
		
		public async Task<IActionResult> GetInboxMessageList()
		{
			var messagesJson = new List<MessageJson>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var messages = await _messageService.GetInboxMessagesByUserIdAsync(UserId);

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
					json.SentBy = await UserHelper.GetFullNameForUser(message.SendingUserId);
				else
					json.SentBy = "System";

				messagesJson.Add(json);
			}

			return Json(messagesJson);
		}

		[HttpGet]
		
		public async Task<IActionResult> GetOutboxMessageList()
		{
			var messagesJson = new List<MessageJson>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var messages = await _messageService.GetSentMessagesByUserIdAsync(UserId);

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
	}
}
