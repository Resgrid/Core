using System;

namespace Resgrid.Model.Services
{
	public interface ISmsService
	{
		void SendMessage(Message message, string departmentNumber, int departmentId, UserProfile profile = null);
		void SendCall(Call call, CallDispatch dispatch, string departmentNumber, int departmentId, UserProfile profile = null, string address = null);
		void SendText(string userId, string title, string message, int departmentId, string departmentNumber, UserProfile profile = null);
		void SendNotification(string userId, int departmentId, string message, string departmentNumber, UserProfile profile = null);
		void SendTroubleAlert(Unit unit, Call call, string unitAddress, string departmentNumber, int departmentId, UserProfile profile);
	}
}
