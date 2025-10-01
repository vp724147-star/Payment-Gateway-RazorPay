using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RazorpayPaymentGateway.Data;
using RazorpayPaymentGateway.Models;
using RazorpayPaymentGateway.Services;
using System.Text;

namespace RazorpayPaymentGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentController : ControllerBase
    {
        private readonly RazorpayService _razorpayService;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            RazorpayService razorpayService,
            ApplicationDbContext dbContext,
            ILogger<PaymentController> logger)
        {
            _razorpayService = razorpayService;
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Create a Razorpay Order
        /// </summary>
        /// <param name="amount">Amount in INR</param>
        [HttpPost("create-order")]
        public IActionResult CreateOrder([FromBody] decimal amount)
        {
            // ✅ Generate unique receipt (this will be used for mapping later)
            var receipt = Guid.NewGuid().ToString();

            // ✅ Create order on Razorpay with receipt
            var order = _razorpayService.CreateOrder(amount, "INR", receipt);

            // ✅ Save order in DB
            var paymentOrder = new PaymentOrder
            {
                OrderId = receipt,  // internal mapping (our referenceId)
                RazorpayOrderId = order["id"].ToString(),  // Razorpay ka order id
                Amount = amount,
                Currency = "INR",
                Status = "created",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PaymentOrders.Add(paymentOrder);
            _dbContext.SaveChanges();

            // ✅ Return both ids to frontend (for checkout if needed)
            return Ok(new
            {
                razorpayOrderId = order["id"].ToString(),
                receiptId = receipt,
                amount,
                currency = "INR",
                key = _razorpayService.GetKey()
            });
        }


        /// <summary>
        /// Razorpay Webhook endpoint to handle payment events
        /// </summary>
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                Request.EnableBuffering();

                using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                Request.Body.Position = 0;

                var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
                var eventType = Request.Headers["X-Razorpay-Event"].ToString();

                // Ensure EventType is never null
                if (string.IsNullOrEmpty(eventType))
                    eventType = "Unknown";

                _logger.LogInformation("Webhook Body: {Body}", body);
                _logger.LogInformation("Webhook Signature: {Signature}", signature);
                _logger.LogInformation("Webhook Event: {EventType}", eventType);

                // Verify signature
                if (!_razorpayService.VerifyWebhookSignature(body, signature))
                {
                    _logger.LogWarning("Invalid webhook signature");
                    return BadRequest("Invalid signature");
                }

                // Save webhook to DB
                var webhook = new PaymentWebhook
                {
                    EventType = eventType,
                    Payload = body,
                    ReceivedAt = DateTime.UtcNow
                };
                _dbContext.PaymentWebhooks.Add(webhook);

                // Process webhook payload
                var payload = Newtonsoft.Json.Linq.JObject.Parse(body);

                var receiptId = payload["payload"]?["order"]?["entity"]?["receipt"]?.ToString();
                var razorpayOrderId = payload["payload"]?["payment"]?["entity"]?["order_id"]?.ToString();

                PaymentOrder order = null;

                if (!string.IsNullOrEmpty(receiptId))
                {
                    order = _dbContext.PaymentOrders.FirstOrDefault(o => o.OrderId == receiptId);
                }

                if (order == null && !string.IsNullOrEmpty(razorpayOrderId))
                {
                    order = _dbContext.PaymentOrders.FirstOrDefault(o => o.RazorpayOrderId == razorpayOrderId);
                }

                if (order != null)
                {
                    order.Status = "paid"; // Always mark paid for testing
                    _dbContext.PaymentOrders.Update(order);
                    _logger.LogInformation("✅ Order {OrderId} marked as paid", order.OrderId);
                }
                else
                {
                    _logger.LogWarning("⚠️ Order not found for ReceiptId {ReceiptId} or RazorpayOrderId {RazorpayOrderId}", receiptId, razorpayOrderId);
                }

                await _dbContext.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return StatusCode(500, "Internal server error");
            }
        }

    }
}