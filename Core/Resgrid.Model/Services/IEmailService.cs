using System.Threading.Tasks;
using Resgrid.Model.Identity;
using MimeKit;
using Resgrid.Model.Events;
using Stripe;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IEmailService
	/// </summary>
	public interface IEmailService
	{
		/// <summary>
		/// Sends the welcome email.
		/// </summary>
		/// <param name="departmentName">Name of the department.</param>
		/// <param name="name">The name.</param>
		/// <param name="emailAddress">The email address.</param>
		/// <param name="userName">Name of the user.</param>
		/// <param name="password">The password.</param>
		/// <param name="departmentId">The department identifier.</param>
		Task<bool> SendWelcomeEmail(string departmentName, string name, string emailAddress, string userName, string password,
			int departmentId);

		/// <summary>
		/// Sends the password reset email.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <param name="name">The name.</param>
		/// <param name="userName">Name of the user.</param>
		/// <param name="password">The password.</param>
		/// <param name="departmentName">Name of the department.</param>
		Task<bool> SendPasswordResetEmail(string emailAddress, string name, string userName, string password,
			string departmentName);

		/// <summary>
		/// Notifies the specified email.
		/// </summary>
		/// <param name="email">The email.</param>
		Task<bool> Notify(EmailNotification email);

		/// <summary>
		/// Systems the test.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <param name="email">The email.</param>
		Task<bool> SystemTest(string emailAddress, EmailNotification email);

		/// <summary>
		/// Sends the signup email.
		/// </summary>
		/// <param name="departmentName">Name of the department.</param>
		/// <param name="name">The name.</param>
		/// <param name="emailAddress">The email address.</param>
		Task<bool> SendSignupEmail(string departmentName, string name, string emailAddress);

		/// <summary>
		/// Sends the message asynchronous.
		/// </summary>
		/// <param name="message">The message.</param>
		/// <param name="senderName">Name of the sender.</param>
		/// <param name="profile">The profile.</param>
		/// <param name="user">The user.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendMessageAsync(Message message, string senderName, int departmentId, UserProfile profile = null, IdentityUser user = null);

		/// <summary>
		/// Sends the call asynchronous.
		/// </summary>
		/// <param name="call">The call.</param>
		/// <param name="dispatch">The dispatch.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendCallAsync(Call call, CallDispatch dispatch, UserProfile profile = null);

		/// <summary>
		/// Sends the trouble alert.
		/// </summary>
		/// <param name="troubleAlertEvent">The trouble alert event.</param>
		/// <param name="unit">The unit.</param>
		/// <param name="call">The call.</param>
		/// <param name="callAddress">The call address.</param>
		/// <param name="unitAddress">The unit address.</param>
		/// <param name="personnelNames">The personnel names.</param>
		/// <param name="profile">The profile.</param>
		Task<bool> SendTroubleAlert(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string callAddress,
			string unitAddress, string personnelNames, UserProfile profile);

		/// <summary>
		/// Sends the invite asynchronous.
		/// </summary>
		/// <param name="invite">The invite.</param>
		/// <param name="senderName">Name of the sender.</param>
		/// <param name="senderEmail">The sender email.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendInviteAsync(Invite invite, string senderName, string senderEmail);

		/// <summary>
		/// Sends the distribution list email.
		/// </summary>
		/// <param name="message">The message.</param>
		/// <param name="emailAddress">The email address.</param>
		/// <param name="name">The name.</param>
		/// <param name="listUsername">The list username.</param>
		/// <param name="listEmail">The list email.</param>
		Task<bool> SendDistributionListEmail(MimeMessage message, string emailAddress, string name, string listUsername,
			string listEmail);

		/// <summary>
		/// Sends the payment receipt email asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="department">The department.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendPaymentReceiptEmailAsync(Payment payment, Department department);

		/// <summary>
		/// Sends the cancellation email asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="department">The department.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendCancellationEmailAsync(Payment payment, Department department);

		/// <summary>
		/// Sends the charge failed email asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="department">The department.</param>
		/// <param name="plan">The plan.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendChargeFailedEmailAsync(Payment payment, Department department, Resgrid.Model.Plan plan);

		/// <summary>
		/// Sends the cancellation with refund email asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="charge">The charge.</param>
		/// <param name="department">The department.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendCancellationWithRefundEmailAsync(Payment payment, Charge charge, Department department);

		/// <summary>
		/// Sends the user cancellation notification to team asynchronous.
		/// </summary>
		/// <param name="department">The department.</param>
		/// <param name="payment">The payment.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="reason">The reason.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendUserCancellationNotificationToTeamAsync(Department department, Payment payment, string userId,
			string reason);

		/// <summary>
		/// Sends the refund issued notification to team.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="charge">The charge.</param>
		/// <param name="department">The department.</param>
		Task<bool> SendRefundIssuedNotificationToTeam(Payment payment, Charge charge, Department department);

		/// <summary>
		/// Sends the upgrade payment receipt email.
		/// </summary>
		/// <param name="newPayment">The new payment.</param>
		/// <param name="oldPayment">The old payment.</param>
		/// <param name="department">The department.</param>
		Task<bool> SendUpgradePaymentReceiptEmail(Payment newPayment, Payment oldPayment, Department department);

		/// <summary>
		/// Sends the report delivery email.
		/// </summary>
		/// <param name="email">The email.</param>
		Task<bool> SendReportDeliveryEmail(EmailNotification email);

		/// <summary>
		/// Sends the notification asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="message">The message.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendNotificationAsync(string userId, string message, int departmentId, UserProfile profile = null);

		/// <summary>
		/// Sends the new department link mail asynchronous.
		/// </summary>
		/// <param name="link">The link.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendNewDepartmentLinkMailAsync(DepartmentLink link);


		/// <summary>
		/// Sends the calendar notification asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="message">The message.</param>
		/// <param name="profile">The profile.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SendCalendarAsync(string userId, string message, int departmentId, UserProfile profile = null);

		Task<bool> SendDeleteDepartmentEmail(string sendingToEmail, string sendingToName, QueueItem queueItem);

		Task<bool> SendReportDeliveryAsync(EmailNotification email, int departmentId, string reportUrl, string reportName);
	}
}
