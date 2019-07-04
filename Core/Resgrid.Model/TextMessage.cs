namespace Resgrid.Model
{
    public class TextMessage
    {
        public string Type { get; set; }
        public string To { get; set; }
        public string Msisdn { get; set; }
        public string NetworkCode { get; set; }
        public string MessageId { get; set; }
        public string Timestamp { get; set; }
        public string Concat { get; set; }
        public string ConcatRef { get; set; }
        public string ConcatTotal { get; set; }
        public string ConcatPart { get; set; }
        public string Data { get; set; }
        public string Udh { get; set; }
        public string Text { get; set; }
    }
}
