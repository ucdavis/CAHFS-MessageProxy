namespace MessageProxyApi.Models
{
    public class CProxyMessage
    {
        public const string NahlnMessageType = "NAHLN";
        public const string CdfaMessageType = "CDFA";

        public int MessageId { get; set; }
        public string? MessageContent { get; set; }
        public DateTime Received { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResponseStatus { get; set; }
        public string? ResponseContent { get; set; }
        public string? MessageType { get; set; }
    }
}
