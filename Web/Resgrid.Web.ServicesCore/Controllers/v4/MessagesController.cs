using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using System.Linq;
using System.Threading;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Models.v4.Messages;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Messaging system interaction
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class MessagesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors

		private ICallsService _callsService;
		private IDepartmentsService _departmentsService;
		private IUserProfileService _userProfileService;
		private IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IMessageService _messageService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public MessagesController(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IGeoLocationProvider geoLocationProvider,
			IAuthorizationService authorizationService,
			IMessageService messageService,
			IUsersService usersService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_authorizationService = authorizationService;
			_messageService = messageService;
			_usersService = usersService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
		}

		#endregion Members and Constructors

		/// <summary>
		/// Returns all inbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[HttpGet("GetMessages")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<ActionResult<GetMessagesResult>> GetInboxMessages([FromHeader(Name = "X-RESGRID-Page")]int? page, [FromHeader(Name = "X-RESGRID-PageSize")]int? pageSize)
		{
			var result = new GetMessagesResult();
			var messages =
				(await _messageService.GetInboxMessagesByUserIdAsync(UserId)).OrderByDescending(x => x.SentOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var currentMessages = new List<MessageResultData>();
			if (messages != null && messages.Any())
			{
				foreach (var m in messages)
				{
					currentMessages.Add(ConvertMessageResultData(m, department, UserId));
				}

				if (pageSize.HasValue && page.HasValue)
				{
					result.Data = currentMessages.Skip(pageSize.Value * page.Value).Take(pageSize.Value).ToList();
					result.Page = page.Value;
				}
				else
				{
					result.Data = currentMessages;
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns all the outbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[HttpGet("GetOutboxMessages")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<ActionResult<GetMessagesResult>> GetOutboxMessages([FromHeader(Name = "X-RESGRID-Page")]int? page, [FromHeader(Name = "X-RESGRID-PageSize")]int? pageSize)
		{
			var result = new GetMessagesResult();
			var messages = (await _messageService.GetSentMessagesByUserIdAsync(UserId)).OrderBy(x => x.SentOn)
				.OrderByDescending(x => x.SentOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var currentMessages = new List<MessageResultData>();
			if (messages != null && messages.Any())
			{
				foreach (var m in messages)
				{
					currentMessages.Add(ConvertMessageResultData(m, department, UserId));
				}

				if (pageSize.HasValue && page.HasValue)
				{
					result.Data = currentMessages.Skip(pageSize.Value * page.Value).Take(pageSize.Value).ToList();
					result.Page = page.Value;
				}
				else
				{
					result.Data = currentMessages;
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets a specific message by it's Id.
		/// </summary>
		/// <param name="messageId">Integer message Identifier</param>
		/// <returns>MessageResult object populated with message information from the system.</returns>
		[HttpGet("GetMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<ActionResult<GetMessageResult>> GetMessage(int messageId, CancellationToken cancellationToken)
		{
			var result = new GetMessageResult();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var savedMessage = await _messageService.GetMessageByIdAsync(messageId);

			if (savedMessage != null)
			{
				if (!_authorizationService.CanUserViewMessage(UserId, savedMessage))
					return Unauthorized();

				await _messageService.ReadMessageRecipientAsync(messageId, UserId, cancellationToken);

				result.Data = ConvertMessageResultData(savedMessage, department, UserId);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Sends a new message to users in the system
		/// </summary>
		/// <param name="newMessageInput">Input data to send a new message</param>
		/// <returns>Created result if the message was sent</returns>
		[HttpPost("SendMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Messages_Create)]
		public async Task<ActionResult<SendMessageResult>> SendMessage([FromBody] NewMessageInput newMessageInput,
			CancellationToken cancellationToken)
		{
			if (newMessageInput.Recipients == null || newMessageInput.Recipients.Count <= 0)
				return BadRequest();

			var result = new SendMessageResult();
			Message savedMessage = null;

			try
			{
				var departmentUsers = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
				var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
				var departmentRoles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);

				var message = new Message();
				message.Subject = newMessageInput.Title;
				message.Body = System.Net.WebUtility.HtmlDecode(newMessageInput.Body);
				message.IsBroadcast = true;
				message.SendingUserId = UserId;
				message.Type = newMessageInput.Type;
				message.SentOn = DateTime.UtcNow;

				var usersToSendTo = new List<string>();

				if (newMessageInput.Recipients.Any(x => x.Name == "Everyone"))
				{
					foreach (var departmentMember in departmentUsers)
					{
						message.AddRecipient(departmentMember.UserId);
					}
				}
				else
				{
					// Add all the explict people
					foreach (var person in newMessageInput.Recipients.Where(x => x.Type == 1))
					{
						if (usersToSendTo.All(x => x != person.Id) && person.Id != UserId)
						{
							// Ensure the user is in the same department
							if (departmentUsers.Any(x => x.UserId == person.Id))
							{
								usersToSendTo.Add(person.Id);
								message.AddRecipient(person.Id);
							}
						}
					}

					// Add all memebers of the group
					foreach (var group in newMessageInput.Recipients.Where(x => x.Type == 2))
					{
						if (!String.IsNullOrWhiteSpace(group.Id))
						{
							int groupId = 0;
							if (int.TryParse(group.Id.Trim(), out groupId))
							{
								if (departmentGroups.Any(x => x.DepartmentGroupId == groupId))
								{
									var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);

									foreach (var member in members)
									{
										if (departmentUsers.Any(x => x.UserId == member.UserId))
										{
											if (usersToSendTo.All(x => x != member.UserId) && member.UserId != UserId)
											{
												usersToSendTo.Add(member.UserId);
												message.AddRecipient(member.UserId);
											}
										}
									}
								}
							}
						}
					}

					// Add all the users of a specific role
					foreach (var role in newMessageInput.Recipients.Where(x => x.Type == 3))
					{
						if (!String.IsNullOrWhiteSpace(role.Id))
						{
							int roleId = 0;
							if (int.TryParse(role.Id.Trim(), out roleId))
							{
								if (departmentRoles.Any(x => x.PersonnelRoleId == roleId))
								{
									var roleMembers =
										await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(role.Id));

									foreach (var member in roleMembers)
									{
										if (departmentUsers.Any(x => x.UserId == member.UserId))
										{
											if (usersToSendTo.All(x => x != member.UserId) && member.UserId != UserId)
											{
												usersToSendTo.Add(member.UserId);
												message.AddRecipient(member.UserId);
											}
										}
									}
								}
							}
						}
					}
				}

				savedMessage = await _messageService.SaveMessageAsync(message, cancellationToken);
				await _messageService.SendMessageAsync(savedMessage, "", DepartmentId, false, cancellationToken);

				result.Id = savedMessage.MessageId.ToString();
				result.PageSize = 0;
				result.Status = ResponseHelper.Created;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Deletes a message from the system
		/// </summary>
		/// <returns>Returns OK status code if successful</returns>
		[HttpPut("RespondToMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Messages_View)]
		public async Task<ActionResult<RespondToMessageResult>> RespondToMessage([FromBody] RespondToMessageInput responseInput,
			CancellationToken cancellationToken)
		{
			var result = new RespondToMessageResult();
			var message = await _messageService.GetMessageByIdAsync(responseInput.Id);

			if (message != null)
			{

				var response = message.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

				if (response == null)
					return NotFound();

				response.Response = responseInput.Type.ToString();
				response.ReadOn = DateTime.UtcNow;
				response.Note = responseInput.Note;

				var respondResult = await _messageService.SaveMessageRecipientAsync(response, cancellationToken);

				if (respondResult != null)
				{
					result.Id = respondResult.MessageRecipientId.ToString();
					result.PageSize = 0;
					result.Status = ResponseHelper.Created;
				}
				else
				{
					result.Id = "";
					result.PageSize = 0;
					result.Status = ResponseHelper.Failure;
				}
			}
			else
			{
				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Deletes a messsage from the system
		/// </summary>
		/// <param name="messageId">MessageId of the message to delete</param>
		/// <returns>Returns OK status code if successful</returns>
		[HttpDelete("DeleteMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Messages_Delete)]
		public async Task<ActionResult<DeleteMessageResult>> DeleteMessage(int messageId, CancellationToken cancellationToken)
		{
			var user = await _usersService.GetUserByNameAsync(UserName);
			var message = await _messageService.GetMessageByIdAsync(messageId);

			if (user == null)
				return NotFound();

			if (message == null)
				return NotFound();

			if (!await _authorizationService.CanUserViewMessageAsync(user.UserId, messageId))
				return Unauthorized();

			if (message.SendingUserId == UserId)
				await _messageService.MarkMessageAsDeletedAsync(messageId, cancellationToken);
			else
				await _messageService.MarkMessageRecipientAsDeletedAsync(messageId, UserId, cancellationToken);

			return Ok();
		}

		public static MessageResultData ConvertMessageResultData(Message savedMessage, Department department, string currentUserId)
		{
			var message = new MessageResultData();
			message.MessageId = savedMessage.MessageId.ToString();
			message.Subject = savedMessage.Subject;
			message.IsSystem = savedMessage.SystemGenerated;

			if (!String.IsNullOrEmpty(savedMessage.Body))
				message.Body = HtmlToTextHelper.ConvertHtml(savedMessage.Body);

			message.SentOn = savedMessage.SentOn.TimeConverter(department);
			message.SentOnUtc = savedMessage.SentOn;
			message.Type = savedMessage.Type;
			message.ExpiredOn = savedMessage.ExpireOn;

			if (!String.IsNullOrWhiteSpace(savedMessage.SendingUserId))
				message.SendingUserId = savedMessage.SendingUserId;

			bool outboxMessage = savedMessage.SendingUserId == currentUserId;

			if (!outboxMessage)
			{
				var respose = savedMessage.MessageRecipients.FirstOrDefault(x => x.UserId == currentUserId);

				if (respose != null)
				{
					if (!String.IsNullOrWhiteSpace(respose.Response))
					{
						message.Responded = true;
						message.ResponseType = respose.Response;
					}

					message.Note = respose.Note;
					message.RespondedOn = respose.ReadOn;
				}
				else
				{
					message.RespondedOn = savedMessage.ReadOn;
				}

				message.Recipients = new List<MessageRecipientResultData>();

				foreach (var recipient in savedMessage.MessageRecipients)
				{
					var recipResult = new MessageRecipientResultData();
					recipResult.MessageId = savedMessage.MessageId.ToString();
					recipResult.UserId = recipient.UserId;
					recipResult.Response = recipient.Response;
					recipResult.Note = recipient.Note;
					recipResult.RespondedOn = recipient.ReadOn;

					message.Recipients.Add(recipResult);
				}
			}
			else
			{
				message.Recipients = new List<MessageRecipientResultData>();

				foreach (var recipient in savedMessage.MessageRecipients)
				{
					var recipResult = new MessageRecipientResultData();
					recipResult.MessageId = savedMessage.MessageId.ToString();
					recipResult.UserId = recipient.UserId;
					recipResult.Response = recipient.Response;
					recipResult.Note = recipient.Note;
					recipResult.RespondedOn = recipient.ReadOn;

					message.Recipients.Add(recipResult);
				}
			}

			return message;
		}
	}
}
