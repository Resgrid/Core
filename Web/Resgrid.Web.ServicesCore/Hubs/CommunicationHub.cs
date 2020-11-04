//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.SignalR;
//using Resgrid.Web.Services.Hubs.Models;

//namespace Resgrid.Web.Services.Hubs
//{
//	//[HubName("communicationHub")]
//	public class CommunicationHub : Hub
//	{
//		private List<ConnectedUser> Users { get; set; }
//		private Dictionary<int,List<Message>> Messages { get; set; }

//		public CommunicationHub()
//		{
//			Users = new List<ConnectedUser>();
//			Messages = new Dictionary<int, List<Message>>();
//		}

//		public void Connect(string id, int departmentId, int type, string name, string data)
//		{
//			string newName = name;
//			if (type == 1)
//				newName = "[D]" + name;
//			else if (type == 2)
//				newName = name + $"[{data}]";

//			Users.Add(new ConnectedUser()
//			{
//				ConnectionId = Context.ConnectionId,
//				DepartmentId = departmentId,
//				Identifier = id,
//				Name = newName,
//				Type = type,
//				Data = data
//			});

//			Groups.Add(Context.ConnectionId, departmentId.ToString());
//			Clients.Caller.onConnected(Context.ConnectionId, newName, Users.Where(x => x.DepartmentId == departmentId).ToList(), GetMessagesForDepartment(departmentId));
//			Clients.Group(departmentId.ToString()).AllExcept(Context.ConnectionId).Clients.AllExcept(Context.ConnectionId).onNewUserConnected(Context.ConnectionId, id, type, newName);
//		}

//		public void SendAll(int departmentId, string name, string message)
//		{
//			AddMessageinCache(departmentId, name, message);
//			Clients.Group(departmentId.ToString()).messageReceived(name, message);
//		}

//		public void SendPrivate(string toId, string message)
//		{
//			var toUser = Users.FirstOrDefault(x => x.ConnectionId == toId);
//			var fromUser = Users.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);

//			if (toUser != null && fromUser != null)
//			{
//				Clients.Client(toId).sendPrivateMessage(Context.ConnectionId, fromUser.Name, message);
//				Clients.Caller.sendPrivateMessage(toId, fromUser.Name, message);
//			}

//		}

//		public override async Task OnDisconnectedAsync(Exception exception)
//		{
//			var item = Users.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
//			if (item != null)
//			{
//				Users.Remove(item);

//				var id = Context.ConnectionId;
//				Clients.Group(item.DepartmentId.ToString()).onUserDisconnected(id, item.Name);
//			}

//			//await Groups.RemoveFromGroupAsync(Context.ConnectionId, "SignalR Users");
//			await base.OnDisconnectedAsync(exception);
//		}

//		#region Private Helpers
//		private List<Message> GetMessagesForDepartment(int departmentId)
//		{
//			if (Messages == null)
//				Messages = new Dictionary<int, List<Message>>();


//			if (Messages.ContainsKey(departmentId))
//				return Messages[departmentId];

//			var newList = new List<Message>();
//			Messages.Add(departmentId, newList);

//			return newList;
//		}

//		private void AddMessageinCache(int departmentId, string name, string message)
//		{
//			if (Messages == null)
//				Messages = new Dictionary<int, List<Message>>();

//			if (!Messages.ContainsKey(departmentId))
//				Messages.Add(departmentId, new List<Message>());

//			Messages[departmentId].Add(new Message { DepartmentId = departmentId, Timestamp = DateTime.UtcNow, Name = name, Body = message });

//			if (Messages[departmentId].Count > 100)
//				Messages[departmentId].RemoveAt(0);
//		}
//		#endregion Private Helpers
//	}
//}
