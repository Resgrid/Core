using Autofac;
using System;
using System.Collections;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models
{
	public class TopIconsModel
	{
		private static IMessageService _messageService;
		private static ICallsService _callsService;

		public TopIconsModel()
		{
			if (_messageService == null)
				_messageService = WebBootstrapper.GetKernel().Resolve<IMessageService>();

			if (_callsService == null)
				_callsService = WebBootstrapper.GetKernel().Resolve<ICallsService>();
		}

		public int NewCalls { get; set; }
		public int NewMessages { get; set; }

		public void SetMessages(string userId, int departmentId)
		{
			// Trying to speed this up (may need to ditch it all together) but at times
			// this is a big hit on NewRelic, most likely because it's constructing the 
			// message and call service every time. It's not an ideal solution, but hopefully
			// it will help for a bit.
			try
			{
				NewMessages = _messageService.GetUnreadMessagesCountByUserId(userId);
				NewCalls = _callsService.GetActiveCallsForDepartment(departmentId);
			}
			catch (Exception)
			{
				_messageService = WebBootstrapper.GetKernel().Resolve<IMessageService>();
				_callsService = WebBootstrapper.GetKernel().Resolve<ICallsService>();

				NewMessages = _messageService.GetUnreadMessagesCountByUserId(userId);
				NewCalls = _callsService.GetActiveCallsForDepartment(departmentId);
			}
		}
	}
}