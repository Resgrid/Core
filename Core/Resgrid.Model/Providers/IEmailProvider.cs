namespace Resgrid.Model.Providers
{
	public interface IEmailProvider
	{
		void Configure(object sender, string fromAddress);

		void SendWelcomeMail(string name, string departmentName, string userName, string password, string email, int departmentId);
		void SendPasswordResetMail(string name, string password, string userName, string email, string departmentName);
		void SendSignupMail(string name, string departmentName, string email);
		void SendMessageMail(string email, string subject, string messageSubject, string messageBody, string senderEmail, string senderName, string sentOn, int messageId);
		void SendCallMail(string email, string subject, string title, string priority, string natureOfCall, string mapPage,
		                  string address, string dispatchedOn, int callId, string userId, string coordinates, string shortenedAudioUrl);
		void SendInviteMail(string code, string departmentName, string email, string senderName, string senderEmail);

		void SendPaymentReciept(string departmentName, string name, string processDate, string amount, string email, string processor,
			string transactionId, string planName, string effectiveDates, string nextBillingDate, int paymentId);

		void SendCancellationReciept(string name, string email, string endDate, string departmentName);

		void SendRefundReciept(string name, string email, string departmentName, string processDate, string amount,
			string processor, string transactionId, string originalPaymentId);

		void TEAM_SendNofifySubCancelled(string name, string email, string departmentName, string departmentId, string reason,
			string processedOn, string planName, string refundIssued);

		void TEAM_SendNotifyRefundIssued(string departmentId, string departmentName, string processDate, string amount,
			string processor, string transactionId, string originalPaymentId);

		void SendUpgradePaymentReciept(string departmentName, string processDate, string amount, string email,
			string processor, string transactionId,
			string planName, string newPlanName, string effectiveDates, string nextBillingDate);

		void SendAffiliateRegister(string email, string affiliateCode);
		void SendAffiliateRejection(string email, string rejectionReason);
		void SendAffiliateWelcomeMail(string name, string email);
		void SendChargeFailed(string name, string email, string endDate, string departmentName, string planName);
		void SendNewDepartmentLinkMail(string name, string departmentName, string data, string email, int departmentId);
		void SendTroubleAlertMail(string email, string unitName, string gpsLocation, string personnel, string callAddress, string unitAddress, string dispatchedOn, string callName);
	}
}
