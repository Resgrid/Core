using Resgrid.Model.Events;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface ICommunicationService
	/// </summary>
	public interface ICommunicationService
	{
		/// <summary>
		/// Sends the message asynchronous.
		/// </summary>
		/// <param name="message">The message.</param>
		/// <param name="sendersName">Name of the senders.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendMessageAsync(Message message, string sendersName, string departmentNumber, int departmentId,
			UserProfile profile = null);

		/// <summary>
		/// Sends the call asynchronous.
		/// </summary>
		/// <param name="call">The call.</param>
		/// <param name="dispatch">The dispatch.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="profile">The profile.</param>
		/// <param name="address">The address.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendCallAsync(Call call, CallDispatch dispatch, string departmentNumber, int departmentId,
			UserProfile profile = null, string address = null);

		/// <summary>
		/// Sends the unit call asynchronous.
		/// </summary>
		/// <param name="call">The call.</param>
		/// <param name="dispatch">The dispatch.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="address">The address.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendUnitCallAsync(Call call, CallDispatchUnit dispatch, string departmentNumber,
			string address = null);

		/// <summary>
		/// Sends the notification asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="message">The message.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="title">The title.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendNotificationAsync(string userId, int departmentId, string message, string departmentNumber,
			string title = "Notification", UserProfile profile = null);

		/// <summary>
		/// Sends the chat.
		/// </summary>
		/// <param name="chatId">The chat identifier.</param>
		/// <param name="sendingUserId">The sending user identifier.</param>
		/// <param name="group">The group.</param>
		/// <param name="message">The message.</param>
		/// <param name="sendingUser">The sending user.</param>
		/// <param name="recipients">The recipients.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendChat(string chatId, int departmentId, string sendingUserId, string group, string message, UserProfile sendingUser, List<UserProfile> recipients);

		/// <summary>
		/// Sends the trouble alert asynchronous.
		/// </summary>
		/// <param name="troubleAlertEvent">The trouble alert event.</param>
		/// <param name="unit">The unit.</param>
		/// <param name="call">The call.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="callAddress">The call address.</param>
		/// <param name="unitAddress">The unit address.</param>
		/// <param name="recipients">The recipients.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendTroubleAlertAsync(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call,
			string departmentNumber, int departmentId, string callAddress, string unitAddress,
			List<UserProfile> recipients);

		/// <summary>
		/// Sends the text message asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="title">The title.</param>
		/// <param name="message">The message.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendTextMessageAsync(string userId, string title, string message, int departmentId,
			string departmentNumber, UserProfile profile = null);

		/// <summary>
		/// Sends the calendar notification asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="message">The message.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="title">The title.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendCalendarAsync(string userId, int departmentId, string message, string departmentNumber,
			string title = "Notification", UserProfile profile = null);
	}
}
