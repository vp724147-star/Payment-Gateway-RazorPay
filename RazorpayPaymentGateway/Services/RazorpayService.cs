using Newtonsoft.Json.Linq;
using Razorpay.Api;
using RazorpayPaymentGateway.Controllers;
using RazorpayPaymentGateway.DTOs;
using System.Security.Cryptography;
using System.Text;

namespace RazorpayPaymentGateway.Services
{
    public class RazorpayService:IRazorpayService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<RazorpayService> _logger;
        private readonly RazorpayClient _client; // 👈 Razorpay client


        public RazorpayService(IConfiguration config, ILogger<RazorpayService> logger)
        {
            _config = config;
            _logger = logger;
            _client = new RazorpayClient(_config["Razorpay:Key"], _config["Razorpay:Secret"]);
        }
        public string GetKey() => _config["Razorpay:Key"];

        public PaymentOrderResponseDto CreateOrder(decimal amount, string currency, string receipt)
        {
            var order = _client.Order.Create(new Dictionary<string, object>
            {
                { "amount", (int)(amount * 100) },// Razorpay expects amount in paise
                { "currency", currency },
                { "receipt", receipt },
                { "payment_capture", 1 }
            });

            return new PaymentOrderResponseDto
            {
                RazorpayOrderId = order["id"].ToString(),
                ReceiptId = receipt,
                Amount = amount,
                Currency = currency,
                Key = _config["Razorpay:Key"]
            };
        }


        public bool VerifyWebhookSignature(string payload, string signature)
        {
            var secret = _config["Razorpay:WebhookSecret"];
            if (string.IsNullOrEmpty(secret))
                throw new Exception("Webhook secret missing in configuration.");

            try
            {
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                {
                    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

                    // ✅ Razorpay signature is HEX, not Base64
                    var generatedSignature = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                    _logger.LogInformation("Razorpay Signature (from header): {Signature}", signature);
                    _logger.LogInformation("Generated Signature: {GeneratedSignature}", generatedSignature);

                    bool isMatch = string.Equals(generatedSignature, signature.Trim(), StringComparison.OrdinalIgnoreCase);

                    _logger.LogInformation("Signature match result: {IsMatch}", isMatch);

                    return isMatch;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying webhook signature");
                return false;
            }
        }

    }
}
