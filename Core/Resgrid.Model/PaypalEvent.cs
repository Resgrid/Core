using System;

namespace Resgrid.Model
{
    public class PaypalEvent
    {
        public string ReceiverEmail { get; set; }
        public string ReceiverId { get; set; }
        public string ResidenceCountry { get; set; }
        public string TestIpn { get; set; }
        public string TransactionSubject { get; set; }
        public string TxnId { get; set; }
        public string TxnType { get; set; }
        public string PayerEmail { get; set; }
        public string PayerId { get; set; }
        public string PayerStatus { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string AddressCity { get; set; }
        public string AddressCountry { get; set; }
        public string AddressCountryCode { get; set; }
        public string AddressName { get; set; }
        public string AddressState { get; set; }
        public string AddressStatus { get; set; }
        public string AddressStreet { get; set; }
        public string AddressZip { get; set; }
        public string Custom { get; set; }
        public string HandlingAmount { get; set; }
        public string ItemName { get; set; }
        public string ItemNumber { get; set; }
        public string MCCurrency { get; set; }
        public string MCFee { get; set; }
        public string MCGross { get; set; }
        public string PaymentDate { get; set; }
        public string PaymentFee { get; set; }
        public string PaymentGross { get; set; }
        public string PaymentStatus { get; set; }
        public string PaymentType { get; set; }
        public string ProtectionEligibility { get; set; }
        public string Quantity { get; set; }
        public string Shipping { get; set; }
        public string Tax { get; set; }
        public string NotifyVersion { get; set; }
        public string Charset { get; set; }
        public string VerifySign { get; set; }

        public decimal AmmountPaid
        {
            get
            {
                Decimal amountPaid = 0;
                Decimal.TryParse(MCGross, out amountPaid);

                return amountPaid;
            }
        }

        public int DepartmentId
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(Custom))
                    return int.Parse(Custom);

                return 0;
            }
        }
    }
}
