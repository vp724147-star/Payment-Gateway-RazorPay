namespace RazorpayPaymentGateway.DTOs
{
    public class PaymentOrderResponseDto
    {
        public string RazorpayOrderId { get; set; } = string.Empty;
        public string ReceiptId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string Key { get; set; } = string.Empty;
    }
}
