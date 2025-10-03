using Newtonsoft.Json.Linq;
using Razorpay.Api;
using RazorpayPaymentGateway.DTOs;

namespace RazorpayPaymentGateway.Services
{
    public interface IRazorpayService
    {
        string GetKey();
        PaymentOrderResponseDto CreateOrder(decimal amount, string currency, string receipt);
        bool VerifyWebhookSignature(string payload, string signature);
    }
}
