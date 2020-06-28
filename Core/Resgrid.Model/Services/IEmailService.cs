using Resgrid.Model.Identity;
using MimeKit;
using Resgrid.Model.Events;

namespace Resgrid.Model.Services
{
	public interface IEmailService
	{
		void SendWelcomeEmail(string departmentName, string name, string emailAddress, string userName, string password, int departmentId);
		void SendPasswordResetEmail(string emailAddress, string name, string userName, string password, string departmentName);
		void Notify(EmailNotification email);
		void SendMessage(Message message, string senderName, UserProfile profile = null, IdentityUser user = null);
		void SendCall(Call call, CallDispatch dispatch, UserProfile profile = null);
		void SendInvite(Invite invite, string senderName, string senderEmail);
		void SendDistributionListEmail(MimeMessage message, string emailAddress, string name, string listUsername, string listEmail);
		void SendPaymentRecieptEmail(Payment payment, Department department);
		void SendCancellationEmail(Payment payment, Department department);
		//void SendCancellationWithRefundEmail(Payment payment, StripeCharge charge, Department department);
		//void SendUserCancellationNotificationToTeam(Department department, Payment payment, string userId, string reason);
		//void SendRefundIssuedNotificationToTeam(Payment payment, StripeCharge charge, Department department);
		//void SendUpgradePaymentRecieptEmail(Payment newPayment, Payment oldPayment, Department department);
		void SystemTest(string emailAddresss, EmailNotification email);
		void SendAffiliateApprovalEmail(Affiliate affiliate);
		void SendAffiliateRejectionEmail(Affiliate affiliate);
		void SendAffiliateSignupEmail(string name, string emailAddress);
		//string TestEmailSettings(DepartmentCallEmail emailSettings);
		void SendReportDeliveryEmail(EmailNotification email);
		void SendNotification(string userId, string message, UserProfile profile = null);
		void SendChargeFailedEmail(Payment payment, Department department, Plan plan);
		//bool IsStatusEmail(PostmarkInboundMessage message);
		void SendNewDepartmentLinkMail(DepartmentLink link);
		void SendTroubleAlert(TroubleAlertEvent troubleAlertEvent, Unit unit, Call call, string callAddress, string unitAddress, string personnelNames, UserProfile profile);
	}
}
