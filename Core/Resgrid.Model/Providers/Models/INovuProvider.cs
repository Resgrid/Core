using System.Threading.Tasks;

namespace Resgrid.Model.Providers;

/// <summary>
/// Defines operations for managing Novu notification subscribers and sending notifications.
/// </summary>
public interface INovuProvider
{
	/// <summary>
	/// Creates a Novu subscriber for a user.
	/// </summary>
	/// <param name="userId">The unique identifier of the user.</param>
	/// <param name="code">The Novu integration code or API key.</param>
	/// <param name="departmentId">The department the user belongs to.</param>
	/// <param name="email">The user's email address.</param>
	/// <param name="firstName">The user's first name.</param>
	/// <param name="lastName">The user's last name.</param>
	/// <returns>True if the subscriber was created successfully; otherwise, false.</returns>
	Task<bool> CreateUserSubscriber(string userId, string code, int departmentId, string email, string firstName, string lastName);

	/// <summary>
	/// Creates a Novu subscriber for a unit (device or group).
	/// </summary>
	/// <param name="unitId">The unique identifier of the unit.</param>
	/// <param name="code">The Novu integration code or API key.</param>
	/// <param name="departmentId">The department the unit belongs to.</param>
	/// <param name="unitName">The name of the unit.</param>
	/// <param name="deviceId">The device identifier associated with the unit.</param>
	/// <returns>True if the subscriber was created successfully; otherwise, false.</returns>
	Task<bool> CreateUnitSubscriber(int unitId, string code, int departmentId, string unitName, string deviceId);

	/// <summary>
	/// Updates the Firebase Cloud Messaging (FCM) token for a user subscriber.
	/// </summary>
	/// <param name="userId">The unique identifier of the user.</param>
	/// <param name="code">The Novu integration code or API key.</param>
	/// <param name="token">The FCM token to associate with the user.</param>
	/// <returns>True if the token was updated successfully; otherwise, false.</returns>
	Task<bool> UpdateUserSubscriberFcm(string userId, string code, string token);

	/// <summary>
	/// Updates the Firebase Cloud Messaging (FCM) token for a unit subscriber.
	/// </summary>
	/// <param name="unitId">The unique identifier of the unit.</param>
	/// <param name="code">The Novu integration code or API key.</param>
	/// <param name="token">The FCM token to associate with the unit.</param>
	/// <returns>True if the token was updated successfully; otherwise, false.</returns>
	Task<bool> UpdateUnitSubscriberFcm(int unitId, string code, string token);

	/// <summary>
	/// Updates the Apple Push Notification Service (APNS) token for a unit subscriber.
	/// </summary>
	/// <param name="unitId">The unique identifier of the unit.</param>
	/// <param name="code">The Novu integration code or API key.</param>
	/// <param name="token">The APNS token to associate with the unit.</param>
	/// <returns>True if the token was updated successfully; otherwise, false.</returns>
	Task<bool> UpdateUnitSubscriberApns(int unitId, string code, string token);

	/// <summary>
	/// Updates the Apple Push Notification Service (APNS) token for a user subscriber.
	/// </summary>
	/// <param name="userId">The unique identifier of the user.</param>
	/// <param name="code">The Novu integration code or API key.</param>
	/// <param name="token">The APNS token to associate with the user.</param>
	/// <returns>True if the token was updated successfully; otherwise, false.</returns>
	Task<bool> UpdateUserSubscriberApns(string userId, string code, string token);

	/// <summary>
	/// Sends a dispatch notification to a unit.
	/// </summary>
	/// <param name="title">The notification title.</param>
	/// <param name="body">The notification body content.</param>
	/// <param name="unitId">The unique identifier of the unit to notify.</param>
	/// <param name="depCode">The department code.</param>
	/// <param name="eventCode">The event code associated with the dispatch.</param>
	/// <param name="type">The type of notification.</param>
	/// <param name="enableCustomSounds">Whether to enable custom notification sounds.</param>
	/// <param name="count">The badge or notification count.</param>
	/// <param name="color">The color code for the notification.</param>
	/// <returns>True if the notification was sent successfully; otherwise, false.</returns>
	Task<bool> SendUnitDispatch(string title, string body, int unitId, string depCode, string eventCode, string type,
		bool enableCustomSounds, int count, string color);

	/// <summary>
	/// Deletes a notification message by its identifier.
	/// </summary>
	/// <param name="messageId">The unique identifier of the message to delete.</param>
	/// <returns>True if the message was deleted successfully; otherwise, false.</returns>
	Task<bool> DeleteMessage(string messageId);

	Task<bool> SendUserDispatch(string title, string body, string userId, string depCode, string eventCode, string type, bool enableCustomSounds, int count, string color);

	Task<bool> SendUserMessage(string title, string body, string userId, string depCode, string eventCode, string type);

	Task<bool> SendUserNotification(string title, string body, string userId, string depCode, string eventCode, string type);
}
