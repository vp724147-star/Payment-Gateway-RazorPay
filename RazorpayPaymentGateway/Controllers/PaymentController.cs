using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RazorpayPaymentGateway.Data;
using RazorpayPaymentGateway.DTOs;
using RazorpayPaymentGateway.Models;
using RazorpayPaymentGateway.Services;
using System.Text;

namespace RazorpayPaymentGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentController : ControllerBase
    {
        private readonly IRazorpayService _razorpayService;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IRazorpayService razorpayService,   // 👈 interface inject karo
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
            if (amount <= 0)
            {
                return BadRequest("Amount must be greater than zero.");
            }

            var receipt = Guid.NewGuid().ToString();

            var orderDto = _razorpayService.CreateOrder(amount, "INR", receipt);
            if (orderDto == null)
            {
                return StatusCode(500, "Failed to create order");
            }

            var paymentOrder = new PaymentOrder
            {
                OrderId = orderDto.ReceiptId,           // DTO se hi le lo
                RazorpayOrderId = orderDto.RazorpayOrderId,
                Amount = orderDto.Amount,
                Currency = orderDto.Currency,
                Status = "created",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PaymentOrders.Add(paymentOrder);
            _dbContext.SaveChanges();

            // Use DTO instead of anonymous object
            var response = new PaymentOrderResponseDto
            {
                RazorpayOrderId = orderDto.RazorpayOrderId,
                ReceiptId = orderDto.ReceiptId,
                Amount = orderDto.Amount,
                Currency =orderDto.Currency,
                Key = _razorpayService.GetKey()
            };

            return Ok(response);
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

                if (string.IsNullOrEmpty(eventType))
                    eventType = "Unknown";

                _logger.LogInformation("Webhook Body: {Body}", body);
                _logger.LogInformation("Webhook Signature: {Signature}", signature);
                _logger.LogInformation("Webhook Event: {EventType}", eventType);

                // ✅ Empty body
                if (string.IsNullOrWhiteSpace(body))
                {
                    return new BadRequestObjectResult("Empty body");
                }

                // ✅ Invalid signature
                if (!_razorpayService.VerifyWebhookSignature(body, signature))
                {
                    _logger.LogWarning("Invalid webhook signature");
                    return new BadRequestObjectResult("Invalid signature");
                }

                // ✅ Invalid JSON handling
                Newtonsoft.Json.Linq.JObject payload;
                try
                {
                    payload = Newtonsoft.Json.Linq.JObject.Parse(body);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Invalid JSON payload received");
                    return new BadRequestObjectResult("Invalid JSON payload");
                }

                var webhook = new PaymentWebhook
                {
                    EventType = eventType,
                    Payload = body,
                    ReceivedAt = DateTime.UtcNow
                };
                _dbContext.PaymentWebhooks.Add(webhook);

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
                    order.Status = "paid";
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