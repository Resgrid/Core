using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Connect
{
	public class MessagesView
	{
		public Department Department { get; set; }
		public DepartmentProfile Profile { get; set; }
		public List<MessageCategory> Messages { get; set; }
	}

	public class MessageCategory
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string LastMessageText { get; set; }
		public DateTime LastMessage { get; set; }
	}
}