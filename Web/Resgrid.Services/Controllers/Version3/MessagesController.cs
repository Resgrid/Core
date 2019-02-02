using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System.Net;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Controllers.Version3.Models.Messages;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Collection of methods to perform operations against messages
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
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
		[AcceptVerbs("GET")]
		public IEnumerable<MessageResult> GetMessages()
		{
			var result = new List<MessageResult>();
			var messages = _messageService.GetInboxMessagesByUserId(UserId).OrderBy(x => x.SentOn);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

			return result;
		}

		/// <summary>
		/// Returns all the outbox messages for a user.
		/// </summary>
		/// <remarks>Note that the body of these messages is truncated to 100 characters.</remarks>
		/// <returns>Array of MessageResult objects for all the messages in the users Inbox</returns>
		[AcceptVerbs("GET")]
		public IEnumerable<MessageResult> GetOutboxMessages()
		{
			var result = new List<MessageResult>();
			var messages = _messageService.GetSentMessagesByUserId(UserId).OrderBy(x => x.SentOn);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

			return result;
		}

		/// <summary>
		/// Gets a specific message by it's Id.
		/// </summary>
		/// <param name="messageId">Integer message Identifier</param>
		/// <returns>MessageResult object populated with message information from the system.</returns>
		[AcceptVerbs("GET")]
		public MessageResult GetMessage(int messageId)
		{
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var savedMessage = _messageService.GetMessageById(messageId);
			bool outboxMessage = false;

			if (savedMessage != null)
			{
				if (!_authorizationService.CanUserViewMessage(UserId, savedMessage))
					Unauthorized();

				if (savedMessage.SendingUserId == UserId)
					outboxMessage = true;

				_messageService.ReadMessageRecipient(messageId, UserId);

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
		[AcceptVerbs("POST")]
		public HttpResponseMessage SendMessage([FromBody] NewMessageInput newMessageInput)
		{
			if (newMessageInput.Rcps == null || newMessageInput.Rcps.Count <= 0)
				throw HttpStatusCode.BadRequest.AsException();

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
					var departmentUsers = _departmentsService.GetAllMembersForDepartment(DepartmentId);

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
								var members = _departmentGroupsService.GetAllMembersForGroup(groupId);

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
						var roleMembers = _personnelRolesService.GetAllMembersOfRole(int.Parse(role.Id));

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

				var savedMessage = _messageService.SaveMessage(message);
				_messageService.SendMessage(savedMessage, "", DepartmentId, false);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw HttpStatusCode.InternalServerError.AsException();
			}

			return Request.CreateResponse(HttpStatusCode.Created);
		}

		/// <summary>
		/// Deletes a messsage from the system
		/// </summary>
		/// <returns>Returns OK status code if successful</returns>
		[AcceptVerbs("PUT")]
		public HttpResponseMessage RespondToMessage([FromBody] MessageResponseInput responseInput)
		{
			var message = _messageService.GetMessageById(responseInput.Id);
			
			if (message == null)
				throw HttpStatusCode.NotFound.AsException();

			var response = message.MessageRecipients.FirstOrDefault(x => x.UserId == UserId);

			if (response == null)
				throw HttpStatusCode.NotFound.AsException();

			response.Response = responseInput.Typ.ToString();
			response.ReadOn = DateTime.UtcNow;
			response.Note = responseInput.Not;

			_messageService.SaveMessageRecipient(response);

			return Request.CreateResponse(HttpStatusCode.OK);
		}

		/// <summary>
		/// Deletes a messsage from the system
		/// </summary>
		/// <param name="messageId">MessageId of the message to delete</param>
		/// <returns>Returns OK status code if successful</returns>
		[AcceptVerbs("DELETE")]
		public HttpResponseMessage DeleteMessage(int messageId)
		{
			var user = _usersService.GetUserByName(UserName);
			var message = _messageService.GetMessageById(messageId);

			if (user == null)
				throw HttpStatusCode.NotFound.AsException();

			if (message == null)
				throw HttpStatusCode.NotFound.AsException();

			if (!_authorizationService.CanUserViewMessage(user.UserId, messageId))
				throw HttpStatusCode.Unauthorized.AsException();

			if (!String.IsNullOrWhiteSpace(message.ReceivingUserId))
				_messageService.MarkMessageAsDeleted(messageId);
			else
				_messageService.MarkMessageAsDeleted(messageId, UserId);

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
