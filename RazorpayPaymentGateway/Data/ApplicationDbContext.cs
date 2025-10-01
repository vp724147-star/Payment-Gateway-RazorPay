using Microsoft.EntityFrameworkCore;
using RazorpayPaymentGateway.Models;

namespace RazorpayPaymentGateway.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<PaymentOrder> PaymentOrders { get; set; }
        public DbSet<PaymentWebhook> PaymentWebhooks { get; set; }
    }
}
