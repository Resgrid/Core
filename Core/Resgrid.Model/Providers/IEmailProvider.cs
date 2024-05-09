using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IEmailProvider
	{
		void Configure(object sender, string fromAddress);

		Task<bool> SendWelcomeMail(string name, string departmentName, string userName, string password, string email, int departmentId);
		Task<bool> SendPasswordResetMail(string name, string password, string userName, string email, string departmentName);
		Task<bool> SendSignupMail(string name, string departmentName, string email);
		Task<bool> SendMessageMail(string email, string subject, string messageSubject, string messageBody, string senderEmail, string senderName, string sentOn, int messageId);
		Task<bool> SendCallMail(string email, string subject, string title, string priority, string natureOfCall, string mapPage,
		            string address, string dispatchedOn, int callId, string userId, string coordinates, string shortenedAudioUrl);
		Task<bool> SendInviteMail(string code, string departmentName, string email, string senderName, string senderEmail);

		Task<bool> SendPaymentReciept(string departmentName, string name, string processDate, string amount, string email, string processor,
			string transactionId, string planName, string effectiveDates, string nextBillingDate, int paymentId);

		Task<bool> SendCancellationReciept(string name, string email, string endDate, string departmentName);

		Task<bool> SendRefundReciept(string name, string email, string departmentName, string processDate, string amount,
			string processor, string transactionId, string originalPaymentId);

		Task<bool> TEAM_SendNofifySubCancelled(string name, string email, string departmentName, string departmentId, string reason,
			string processedOn, string planName, string refundIssued);

		Task<bool> TEAM_SendNotifyRefundIssued(string departmentId, string departmentName, string processDate, string amount,
			string processor, string transactionId, string originalPaymentId);

		Task<bool> SendUpgradePaymentReciept(string departmentName, string processDate, string amount, string email,
			string processor, string transactionId,
			string planName, string newPlanName, string effectiveDates, string nextBillingDate);

		void SendAffiliateRegister(string email, string affiliateCode);
		void SendAffiliateRejection(string email, string rejectionReason);
		void SendAffiliateWelcomeMail(string name, string email);
		Task<bool> SendChargeFailed(string name, string email, string endDate, string departmentName, string planName);
		Task<bool> SendNewDepartmentLinkMail(string name, string departmentName, string data, string email, int departmentId);
		Task<bool> SendTroubleAlertMail(string email, string unitName, string gpsLocation, string personnel, string callAddress, string unitAddress, string dispatchedOn, string callName);
		Task<bool> SendReportDeliveryMail(string email, string subject, string messageBody, string sentOn,
			string reportName, string attachmentFilename, byte[] attachmentData, string reportUrl);


		// Internal Template Only Emails
		Task<bool> SendDeleteDepartmentEmail(string requesterName, string departmentName, DateTime localCompletedOn, string sendingToPersonName, string email);
	}
}
