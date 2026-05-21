namespace MessageProxyApi.Models
{
    public class CProxyMessage
    {
        public int MessageId { get; set; }
        public string? MessageContent { get; set; }
        public DateTime Received { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ResponseStatus { get; set; }
        public string? ResponseContent { get; set; }
    }
}
