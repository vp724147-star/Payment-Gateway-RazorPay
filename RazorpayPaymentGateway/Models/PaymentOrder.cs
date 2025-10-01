namespace RazorpayPaymentGateway.Models
{
    public class PaymentOrder
    {
        public int Id { get; set; }
        public string OrderId { get; set; }
        public string RazorpayOrderId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
