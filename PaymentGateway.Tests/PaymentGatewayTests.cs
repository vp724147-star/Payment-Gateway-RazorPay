using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using Razorpay.Api;
using RazorpayPaymentGateway.Controllers;
using RazorpayPaymentGateway.Data;
using RazorpayPaymentGateway.DTOs;
using RazorpayPaymentGateway.Models;
using RazorpayPaymentGateway.Services;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PaymentGateway.Tests
{
    public class PaymentControllerTests
    {
        private readonly PaymentController _controller;
        private readonly Mock<IRazorpayService> _razorpayServiceMock;
        private readonly ApplicationDbContext _dbContext;
        private readonly Mock<ILogger<PaymentController>> _loggerMock;

        public PaymentControllerTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
                .Options;

            _dbContext = new ApplicationDbContext(options);

            _loggerMock = new Mock<ILogger<PaymentController>>();
            _razorpayServiceMock = new Mock<IRazorpayService>();

            _controller = new PaymentController(
                _razorpayServiceMock.Object,
                _dbContext,
                _loggerMock.Object
            );
        }

        private void SetupWebhookRequest(string body, string signature, string eventType)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["X-Razorpay-Signature"] = signature;
            httpContext.Request.Headers["X-Razorpay-Event"] = eventType;

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        #region CreateOrder Tests
        [Fact]
        public void CreateOrder_ValidAmount_ReturnsOk()
        {
            // Arrange
            decimal amount = 100;
            var fakeOrder = new PaymentOrderResponseDto
            {
                RazorpayOrderId = "order_test_id",
                ReceiptId = "receipt_test_id",
                Amount = amount,
                Currency = "INR",
                Key = "fake_key"
            };

            _razorpayServiceMock
                .Setup(x => x.CreateOrder(amount, "INR", It.IsAny<string>()))
                .Returns(fakeOrder);

            _razorpayServiceMock
                .Setup(x => x.GetKey())
                .Returns("fake_key");

            // Act
            var result = _controller.CreateOrder(amount) as OkObjectResult;

            // Assert
            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);

            var dto = Assert.IsType<PaymentOrderResponseDto>(result.Value);
            Assert.Equal("order_test_id", dto.RazorpayOrderId);
            Assert.Equal(amount, dto.Amount);
            Assert.Equal("fake_key", dto.Key);

            var orderInDb = _dbContext.PaymentOrders.FirstOrDefault();
            Assert.NotNull(orderInDb);
            Assert.Equal("created", orderInDb.Status);
        }
        [Fact]
        public void CreateOrder_NegativeAmount_ReturnsBadRequest()
        {
            // Arrange
            decimal amount = -50;

            // Act
            var result = _controller.CreateOrder(amount);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Amount must be greater than zero.", badRequest.Value);
        }


        #endregion

        #region Webhook Tests

        [Fact]
        public async Task Webhook_InvalidSignature_ReturnsBadRequest()
        {
            // Arrange
            SetupWebhookRequest("{}", "invalid-signature", "payment.captured");
            _razorpayServiceMock.Setup(x => x.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            // Act
            var result = await _controller.Webhook();

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Webhook_ValidSignature_OrderFound_MarksPaid()
        {
            // Arrange
            var body = @"{
                ""payload"": {
                    ""order"": { ""entity"": { ""receipt"": ""order_123"" } },
                    ""payment"": { ""entity"": { ""order_id"": ""razorpay_order_456"" } }
                }
            }";
            SetupWebhookRequest(body, "valid-signature", "payment.captured");

            _razorpayServiceMock.Setup(x => x.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            _dbContext.PaymentOrders.Add(new PaymentOrder
            {
                OrderId = "order_123",
                RazorpayOrderId = "razorpay_order_456",
                Amount = 100,
                Currency = "INR",
                Status = "created",
                CreatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _controller.Webhook();

            // Assert
            Assert.IsType<OkResult>(result);

            var order = await _dbContext.PaymentOrders.FirstOrDefaultAsync(o => o.OrderId == "order_123");
            Assert.Equal("paid", order.Status);
        }

        [Fact]
        public async Task Webhook_OrderNotFound_ReturnsOkWithWarning()
        {
            // Arrange
            var body = @"{
                ""payload"": {
                    ""order"": { ""entity"": { ""receipt"": ""non_existing"" } },
                    ""payment"": { ""entity"": { ""order_id"": ""razorpay_order_789"" } }
                }
            }";
            SetupWebhookRequest(body, "valid-signature", "payment.failed");

            _razorpayServiceMock.Setup(x => x.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            // Act
            var result = await _controller.Webhook();

            // Assert
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public async Task Webhook_EmptyBody_ReturnsBadRequest()
        {
            // Arrange
            SetupWebhookRequest("", "signature", "payment.captured");
            _razorpayServiceMock.Setup(x => x.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            // Act
            var result = await _controller.Webhook();

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Webhook_InvalidJson_ReturnsBadRequest()
        {
            // Arrange
            SetupWebhookRequest("invalid-json", "signature", "payment.captured");
            _razorpayServiceMock.Setup(x => x.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);

            // Act
            var result = await _controller.Webhook();

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion
        #region Additional Edge Case Tests

        [Fact]
        public void CreateOrder_ZeroAmount_ReturnsBadRequest()
        {
            // Arrange
            decimal amount = 0;

            // Act
            var result = _controller.CreateOrder(amount);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Amount must be greater than zero.", badRequest.Value);
        }

        [Fact]
        public async Task Webhook_MissingSignatureHeader_ReturnsBadRequest()
        {
            // Arrange
            var body = @"{
            ""payload"": {
            ""order"": { ""entity"": { ""receipt"": ""order_123"" } },
            ""payment"": { ""entity"": { ""order_id"": ""razorpay_order_456"" } }
        }
    }";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = stream;
            httpContext.Request.Headers["X-Razorpay-Event"] = "payment.captured";

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            _razorpayServiceMock.Setup(x => x.VerifyWebhookSignature(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(false);

            // Act
            var result = await _controller.Webhook();

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid signature", badRequest.Value);
        }

        #endregion
    }
}
