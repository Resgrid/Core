using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.Services.Controllers.Version3.Models.Messages;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Collection of methods to perform operations against messages
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class MessagesController : V3AuthenticatedApiControllerbase
	{
		#region Private Members and Constructors
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
		#endregion Private Members and Constructors

		/// <summary>
		/// Returns all inbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[HttpGet("GetMessages")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IEnumerable<MessageResult>>> GetMessages()
		{
			var result = new List<MessageResult>();
			var messages = (await _messageService.GetInboxMessagesByUserIdAsync(UserId)).OrderBy(x => x.SentOn).OrderByDescending(x => x.SentOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var m in messages)
			{
				var message = new MessageResult();

				message.Mid = m.MessageId;
				message.Sub = m.Subject;
				message.Bdy = StringHelpers.StripHtmlTagsCharArray(m.Body).Truncate(100);
				message.Son = m.SentOn.TimeConverter(department);
				message.SUtc = m.SentOn;
				message.Typ = m.Type;

				if (!String.IsNullOrWhiteSpace(m.SendingUserId))
					message.Uid = m.SendingUserId;

				var respose = m.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

				if (respose != null)
				{
					if (String.IsNullOrWhiteSpace(respose.Response))
						message.Rsp = true;

					message.Ron = respose.ReadOn;
				}
				else
				{
					message.Ron = m.ReadOn;
				}

				result.Add(message);
			}

			return Ok(result);
		}

		/// <summary>
		/// Returns all inbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[HttpGet("GetMessagesPaged")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IEnumerable<MessageResult>>> GetMessagesPaged(int page)
		{
			var result = new List<MessageResult>();
			var messages = (await _messageService.GetInboxMessagesByUserIdAsync(UserId)).OrderByDescending(x => x.SentOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var m in messages)
			{
				var message = new MessageResult();

				message.Mid = m.MessageId;
				message.Sub = m.Subject;
				message.Bdy = StringHelpers.StripHtmlTagsCharArray(m.Body).Truncate(100);
				message.Son = m.SentOn.TimeConverter(department);
				message.SUtc = m.SentOn;
				message.Typ = m.Type;

				if (!String.IsNullOrWhiteSpace(m.SendingUserId))
					message.Uid = m.SendingUserId;

				var respose = m.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

				if (respose != null)
				{
					if (String.IsNullOrWhiteSpace(respose.Response))
						message.Rsp = true;

					message.Ron = respose.ReadOn;
				}
				else
				{
					message.Ron = m.ReadOn;
				}

				result.Add(message);
			}

			return Ok(result.Skip(25 * page).Take(25));
		}

		/// <summary>
		/// Returns all the outbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[HttpGet("GetOutboxMessages")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IEnumerable<MessageResult>>> GetOutboxMessages()
		{
			var result = new List<MessageResult>();
			var messages = (await _messageService.GetSentMessagesByUserIdAsync(UserId)).OrderBy(x => x.SentOn).OrderByDescending(x => x.SentOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var m in messages)
			{
				var message = new MessageResult();

				message.Mid = m.MessageId;
				message.Sub = m.Subject;
				message.Bdy = StringHelpers.StripHtmlTagsCharArray(m.Body).Truncate(100);
				message.Son = m.SentOn.TimeConverter(department);
				message.SUtc = m.SentOn;
				message.Typ = m.Type;

				if (!String.IsNullOrWhiteSpace(m.SendingUserId))
					message.Uid = m.SendingUserId;

				var respose = m.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

				if (respose != null)
				{
					if (String.IsNullOrWhiteSpace(respose.Response))
						message.Rsp = true;

					message.Ron = respose.ReadOn;
				}
				else
				{
					message.Ron = m.ReadOn;
				}

				result.Add(message);
			}

			return Ok(result);
		}

		/// <summary>
		/// Returns all the outbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[HttpGet("GetOutboxMessagesPaged")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<IEnumerable<MessageResult>>> GetOutboxMessagesPaged(int page)
		{
			var result = new List<MessageResult>();
			var messages = (await _messageService.GetSentMessagesByUserIdAsync(UserId)).OrderBy(x => x.SentOn).OrderByDescending(x => x.SentOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var m in messages)
			{
				var message = new MessageResult();

				message.Mid = m.MessageId;
				message.Sub = m.Subject;
				message.Bdy = StringHelpers.StripHtmlTagsCharArray(m.Body).Truncate(100);
				message.Son = m.SentOn.TimeConverter(department);
				message.SUtc = m.SentOn;
				message.Typ = m.Type;

				if (!String.IsNullOrWhiteSpace(m.SendingUserId))
					message.Uid = m.SendingUserId;

				var respose = m.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

				if (respose != null)
				{
					if (String.IsNullOrWhiteSpace(respose.Response))
						message.Rsp = true;

					message.Ron = respose.ReadOn;
				}
				else
				{
					message.Ron = m.ReadOn;
				}

				result.Add(message);
			}

			return Ok(result.Skip(25 * page).Take(25));
		}

		/// <summary>
		/// Gets a specific message by it's Id.
		/// </summary>
		/// <param name="messageId">Integer message Identifier</param>
		/// <returns>MessageResult object populated with message information from the system.</returns>
		[HttpGet("GetMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<MessageResult>> GetMessage(int messageId, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var savedMessage = await _messageService.GetMessageByIdAsync(messageId);
			bool outboxMessage = false;

			if (savedMessage != null)
			{
				if (!_authorizationService.CanUserViewMessage(UserId, savedMessage))
					return Unauthorized();

				if (savedMessage.SendingUserId == UserId)
					outboxMessage = true;

				await _messageService.ReadMessageRecipientAsync(messageId, UserId, cancellationToken);

				var message = new MessageResult();
				message.Mid = savedMessage.MessageId;
				message.Sub = savedMessage.Subject;
				message.Sys = savedMessage.SystemGenerated;

				if (!String.IsNullOrEmpty(savedMessage.Body))
					message.Bdy = HtmlToTextHelper.ConvertHtml(savedMessage.Body);

				message.Son = savedMessage.SentOn.TimeConverter(department);
				message.SUtc = savedMessage.SentOn;
				message.Typ = savedMessage.Type;
				message.Exp = savedMessage.ExpireOn;

				if (!String.IsNullOrWhiteSpace(savedMessage.SendingUserId))
					message.Uid = savedMessage.SendingUserId;

				if (!outboxMessage)
				{
					var respose = savedMessage.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

					if (respose != null)
					{
						if (!String.IsNullOrWhiteSpace(respose.Response))
						{
							message.Rsp = true;
							message.Rty = respose.Response;
						}

						message.Not = respose.Note;
						message.Ron = respose.ReadOn;
					}
					else
					{
						message.Ron = savedMessage.ReadOn;
					}

					message.Rcpts = new List<MessageRecipientResult>();

					foreach (var recipient in savedMessage.MessageRecipients)
					{
						var recipResult = new MessageRecipientResult();
						recipResult.Mid = savedMessage.MessageId;
						recipResult.Uid = recipient.UserId;
						recipResult.Rty = recipient.Response;
						recipResult.Not = recipient.Note;
						recipResult.Ron = recipient.ReadOn;

						message.Rcpts.Add(recipResult);
					}
				}
				else
				{
					message.Rcpts = new List<MessageRecipientResult>();

					foreach (var recipient in savedMessage.MessageRecipients)
					{
						var recipResult = new MessageRecipientResult();
						recipResult.Mid = savedMessage.MessageId;
						recipResult.Uid = recipient.UserId;
						recipResult.Rty = recipient.Response;
						recipResult.Not = recipient.Note;
						recipResult.Ron = recipient.ReadOn;

						message.Rcpts.Add(recipResult);
					}
				}

				return message;
			}

			return null;
		}

		/// <summary>
		/// Sends a new message to users in the system
		/// </summary>
		/// <param name="newMessageInput">Input data to send a new message</param>
		/// <returns>Created result if the message was sent</returns>
		[HttpPost("SendMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> SendMessage([FromBody] NewMessageInput newMessageInput, CancellationToken cancellationToken)
		{
			if (newMessageInput.Rcps == null || newMessageInput.Rcps.Count <= 0)
				return BadRequest();

			Message savedMessage = null;

			try
			{
				var message = new Message();
				message.Subject = newMessageInput.Ttl;
				message.Body = System.Net.WebUtility.HtmlDecode(newMessageInput.Bdy);
				message.IsBroadcast = true;
				message.SendingUserId = UserId;
				message.Type = newMessageInput.Typ;
				message.SentOn = DateTime.UtcNow;

				var usersToSendTo = new List<string>();

				if (newMessageInput.Rcps.Any(x => x.Nme == "Everyone"))
				{
					var departmentUsers = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);

					foreach (var departmentMember in departmentUsers)
					{
						message.AddRecipient(departmentMember.UserId);
					}
				}
				else
				{
					// Add all the explict people
					foreach (var person in newMessageInput.Rcps.Where(x => x.Typ == 1))
					{
						if (usersToSendTo.All(x => x != person.Id) && person.Id != UserId)
						{
							usersToSendTo.Add(person.Id);
							message.AddRecipient(person.Id);
						}
					}

					// Add all memebers of the group
					foreach (var group in newMessageInput.Rcps.Where(x => x.Typ == 2))
					{
						if (!String.IsNullOrWhiteSpace(group.Id))
						{
							int groupId = 0;
							if (int.TryParse(group.Id.Trim(), out groupId))
							{
								var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);

								foreach (var member in members)
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

					// Add all the users of a specific role
					foreach (var role in newMessageInput.Rcps.Where(x => x.Typ == 3))
					{
						var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(role.Id));

						foreach (var member in roleMembers)
						{
							if (usersToSendTo.All(x => x != member.UserId) && member.UserId != UserId)
							{
								usersToSendTo.Add(member.UserId);
								message.AddRecipient(member.UserId);
							}
						}
					}
				}

				savedMessage = await _messageService.SaveMessageAsync(message, cancellationToken);
				await _messageService.SendMessageAsync(savedMessage, "", DepartmentId, false, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest();
			}

			return CreatedAtAction(nameof(SendMessage), new { id = savedMessage.MessageId }, savedMessage);
		}

		/// <summary>
		/// Deletes a messsage from the system
		/// </summary>
		/// <returns>Returns OK status code if successful</returns>
		[HttpPut("RespondToMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> RespondToMessage([FromBody] MessageResponseInput responseInput, CancellationToken cancellationToken)
		{
			var message = await _messageService.GetMessageByIdAsync(responseInput.Id);
			
			if (message == null)
				return NotFound();

			var response = message.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

			if (response == null)
				return NotFound();

			response.Response = responseInput.Typ.ToString();
			response.ReadOn = DateTime.UtcNow;
			response.Note = responseInput.Not;

			await _messageService.SaveMessageRecipientAsync(response, cancellationToken);

			return Ok();
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
		public async Task<ActionResult> DeleteMessage(int messageId, CancellationToken cancellationToken)
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
	}
}
