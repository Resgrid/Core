using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using System.Collections.Generic;
using Resgrid.Web.Services.Controllers.Version3.Models.Chat;
using Microsoft.Extensions.Options;
using Resgrid.Web.Options;
using System.Threading.Tasks;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ChatController : SecureBaseController
	{
		#region Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IOptions<AppOptions> _appOptionsAccessor;


		public ChatController(
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IDepartmentGroupsService departmentGroupsService,
			IOptions<AppOptions> appOptionsAccessor
			)
		{
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_departmentGroupsService = departmentGroupsService;
			_appOptionsAccessor = appOptionsAccessor;
		}
		#endregion Members and Constructors

		[HttpGet]
		public async Task<ChatDataResult> GetResponderChatSettings()
		{
			var result = new ChatDataResult();

			//// Load Twilio configuration from Web.config
			//var accountSid = _appOptionsAccessor.Value.TwilioAccountSid;
			//var apiKey = _appOptionsAccessor.Value.TwilioApiKey;
			//var apiSecret = _appOptionsAccessor.Value.TwilioApiSecret;
			//var ipmServiceSid = _appOptionsAccessor.Value.TwilioIpmServiceSid;

			//// Create an Access Token generator
			//var token = new AccessToken(accountSid, apiKey, apiSecret);
			//token.Identity = UserId.ToString();

			//// Create an IP messaging grant for this token
			//var grant = new IpMessagingGrant();
			//grant.EndpointId = $"ResponderDepChat:{UserId}:ResponderApp";
			//grant.ServiceSid = ipmServiceSid;
			//token.AddGrant(grant);

			//var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			//var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			//result.Channels = SetupTwilioChatForDepartment(department, groups);

			//result.Did = department.DepartmentId;
			//result.Name = department.Name;

			//result.Groups = new List<GroupInfoResult>();
			//if (department.IsUserAnAdmin(UserId))
			//{
			//	foreach (var group in groups)
			//	{
			//		result.Groups.Add(new GroupInfoResult() { Gid = group.DepartmentGroupId, Nme = group.Name });
			//	}
			//}
			//else
			//{
			//	var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			//	if (group != null)
			//	{
			//		result.Groups.Add(new GroupInfoResult() { Gid = group.DepartmentGroupId, Nme = group.Name });
			//	}
			//}

			////result.Token = token.ToJWT();

			return result;
		}

		private List<ChannelData> SetupTwilioChatForDepartment(Department department, List<DepartmentGroup> groups)
		{
			var result = new List<ChannelData>();

			//var accountSid = _appOptionsAccessor.Value.TwilioAccountSid;
			//var apiKey = _appOptionsAccessor.Value.TwilioApiKey;
			//var apiSecret = _appOptionsAccessor.Value.TwilioApiSecret;
			//var ipmServiceSid = _appOptionsAccessor.Value.TwilioIpmServiceSid;
			//var authToken = _appOptionsAccessor.Value.TwilioAuthToken;

			//var client = new Twilio.IpMessaging.IpMessagingClient(accountSid, authToken);

			//var departmentChannelData = new ChannelData();

			//var members = _departmentsService.GetAllUsersForDepartment(department.DepartmentId);
			//var admins = _departmentsService.GetAllAdminsForDepartment(department.DepartmentId);

			//bool isNewDepartmentChannel = false;
			//var departmentChannel = GetOrCreateChannel(client, ipmServiceSid, $"Department:{department.DepartmentId}", department.Name, members, out isNewDepartmentChannel);

			//if (departmentChannel != null && !String.IsNullOrWhiteSpace(departmentChannel.Sid) && !isNewDepartmentChannel)
			//{
			//	departmentChannelData.Sid = departmentChannel.Sid;
			//	departmentChannelData.Name = departmentChannel.FriendlyName;
			//	departmentChannelData.LastUpdated = departmentChannel.DateUpdated.ToString("O");

			//	var messageResult = client.ListMessages(departmentChannel.ServiceSid, departmentChannel.Sid);

			//	departmentChannelData.MessageCount = messageResult.Messages.Count;
			//}
			//else if (isNewDepartmentChannel)
			//{
			//	departmentChannelData.Sid = departmentChannel.Sid;
			//	departmentChannelData.Name = departmentChannel.FriendlyName;
			//	departmentChannelData.LastUpdated = DateTime.UtcNow.ToString("O");
			//}

			//result.Add(departmentChannelData);

			//if (groups != null && groups.Any())
			//{
			//	foreach (var group in groups)
			//	{
			//		var groupChannelData = new ChannelData();

			//		var groupUsers = _departmentGroupsService.GetAllUsersForGroup(group.DepartmentGroupId);

			//		bool isNewChannel = false;
			//		var channel = GetOrCreateChannel(client, ipmServiceSid, $"Group:{group.DepartmentGroupId}", department.Name, groupUsers.Concat(admins).ToList(), out isNewChannel);

			//		if (channel != null && !String.IsNullOrWhiteSpace(channel.Sid) && !isNewChannel)
			//		{
			//			groupChannelData.Sid = channel.Sid;
			//			groupChannelData.Name = channel.FriendlyName;
			//			groupChannelData.LastUpdated = channel.DateUpdated.ToString("O");

			//			var messageResult = client.ListMessages(channel.ServiceSid, channel.Sid);

			//			groupChannelData.MessageCount = messageResult.Messages.Count;
			//		}
			//		else if (isNewChannel)
			//		{
			//			groupChannelData.Sid = channel.Sid;
			//			groupChannelData.Name = group.Name;
			//			groupChannelData.LastUpdated = DateTime.UtcNow.ToString("O");
			//		}

			//		result.Add(groupChannelData);
			//	}
			//}

			return result;
		}

		//private Channel GetOrCreateChannel(Twilio.IpMessaging.IpMessagingClient client, string sid, string id, string name, List<IdentityUser> users, out bool isNew)
		//{
		//	isNew = false;
		//	//var channel = client.GetChannel(sid, id);

		//	//if (channel != null && !String.IsNullOrWhiteSpace(channel.Sid))
		//	//{
		//	//	foreach (var user in users)
		//	//	{
		//	//		Member channelMember = client.GetMember(sid, channel.Sid, user.UserId);

		//	//		if (channelMember == null)
		//	//			client.CreateMember(sid, channel.Sid, user.UserId, null);
		//	//	}

		//	//	return channel;
		//	//}

		//	//channel = client.CreateChannel(sid, "private", name, id, "");
		//	//isNew = true;

		//	//if (users != null && users.Any())
		//	//{
		//	//	foreach (var user in users)
		//	//	{
		//	//		client.CreateMember(sid, channel.Sid, user.UserId, null);
		//	//	}
		//	//}

		//	//return channel;

		//	return null;
		//}
	}
}
