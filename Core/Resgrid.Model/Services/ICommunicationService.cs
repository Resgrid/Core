using Resgrid.Model.Events;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICommunicationService
	{
		void SendMessage(Message message, string sendersName, string departmentNumber, int departmentId, UserProfile profile = null);
		void SendCall(Call call, CallDispatch dispatch, string departmentNumber, int departmentId, UserProfile profile = null, string address = null);

		void SendTextMessage(string userId, string title, string message, int departmentId, string departmentNumber,
			UserProfile profile = null);

		void SendNotification(string userId, int departmentId, string message, string departmentNumber, 
			string title = "Notification", UserProfile profile = null);
		Task<bool> SendChat(string chatId, string sendingUserId, string group, string message, UserProfile sendingUser, List<UserProfile> recipients);
		void SendTroubleAlert(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string departmentNumber, int departmentId, string callAddress, string unitAddress, List<UserProfile> recipients);
		void SendUnitCall(Call call, CallDispatchUnit dispatch, string departmentNumber, string address = null);
	}
}
