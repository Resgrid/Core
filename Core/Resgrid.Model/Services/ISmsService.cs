using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface ISmsService
	/// </summary>
	public interface ISmsService
	{
		/// <summary>
		/// Sends the message asynchronous.
		/// </summary>
		/// <param name="message">The message.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendMessageAsync(Message message, string departmentNumber, int departmentId,
			UserProfile profile = null, Payment payment = null);

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
			UserProfile profile = null, string address = null, Payment payment = null);

		/// <summary>
		/// Sends the trouble alert.
		/// </summary>
		/// <param name="unit">The unit.</param>
		/// <param name="call">The call.</param>
		/// <param name="unitAddress">The unit address.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="profile">The profile.</param>
		void SendTroubleAlert(Unit unit, Call call, string unitAddress, string departmentNumber, int departmentId,
			UserProfile profile);

		/// <summary>
		/// Sends the text asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="title">The title.</param>
		/// <param name="message">The message.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendTextAsync(string userId, string title, string message, int departmentId, string departmentNumber,
			UserProfile profile = null);

		/// <summary>
		/// Sends the notification asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="message">The message.</param>
		/// <param name="departmentNumber">The department number.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendNotificationAsync(string userId, int departmentId, string message, string departmentNumber,
			UserProfile profile = null);
	}
}
