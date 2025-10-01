namespace RazorpayPaymentGateway.Models
{
    public class PaymentWebhook
    {
        public int Id { get; set; }
        public string EventType { get; set; }
        public string Payload { get; set; }
        public DateTime ReceivedAt { get; set; }
    }
}
