using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.ComponentModel.DataAnnotations.Schema;

namespace BollTrack;

public class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<OrderPayment> Payments => Set<OrderPayment>();
    public DbSet<ReturnRequest> Returns => Set<ReturnRequest>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductListing> Listings => Set<ProductListing>();
    public DbSet<ProductPhoto> Photos => Set<ProductPhoto>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<Stock> Stock => Set<Stock>();
    public DbSet<ShopOrder> Orders => Set<ShopOrder>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CustomerAddress>().HasKey(a => a.Owner);
        model.Entity<OrderPayment>().HasKey(p => p.OrderId);
        model.Entity<ReturnRequest>().HasKey(r => r.OrderId);
        model.Entity<Shipment>().HasKey(s => s.OrderId);
        model.Entity<ProductListing>().HasKey(l => l.ProductId);
        model.Entity<ProductListing>().HasOne(l => l.Product).WithOne().HasForeignKey<ProductListing>(l => l.ProductId);
        model.Entity<ProductPhoto>().HasKey(p => p.ProductId);
        model.Entity<StockMovement>().HasKey(m => m.Seq);
        model.Entity<Stock>().HasKey(s => new { s.ProductId, s.SizeCode });
        model.Entity<OrderLine>().HasKey(s => new { s.OrderId, s.ProductId, s.SizeCode });
        model.Entity<ShopOrder>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
        model.Entity<Product>().HasMany(p => p.Sizes).WithOne().HasForeignKey(s => s.ProductId);
    }

    public static string Connection(IConfiguration config)
    {
        var uri = new Uri(config["MYSQL_URL"] ?? throw new InvalidOperationException("MYSQL_URL manquante."));
        var login = uri.UserInfo.Split(':', 2);
        return new MySqlConnectionStringBuilder {
            Server = uri.Host, Port = (uint)(uri.Port > 0 ? uri.Port : 3306),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
            UserID = Uri.UnescapeDataString(login[0]), Password = Uri.UnescapeDataString(login[1]),
            AllowUserVariables = true, DateTimeKind = MySqlDateTimeKind.Utc, ConnectionTimeout = 8
        }.ConnectionString;
    }

    public static async Task Initialize(string connection)
    {
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync();
        foreach (var file in new[] { "stock.sql", "orders.sql", "admin.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file));
            await using var command = new MySqlCommand(sql, db);
            await command.ExecuteNonQueryAsync();
        }
    }
}

[Table("products")]
public class Product
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceCents { get; set; }
    public List<Stock> Sizes { get; set; } = [];
}
[Table("stock")]
public class Stock
{
    public string ProductId { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public int Available { get; set; }
}
[Table("orders")]
public class ShopOrder
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string Status { get; set; } = "pending";
    public string? StripeSessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? ReceiptUrl { get; set; }
    public string Email { get; set; } = "";
    public string DeliveryJson { get; set; } = "";
    public int ShippingCents { get; set; }
    public int TotalCents { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}
[Table("order_lines")]
public class OrderLine
{
    public string OrderId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceCents { get; set; }
    public int Quantity { get; set; }
}

[Table("shipments")]
public class Shipment
{
    public string OrderId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? TrackingNumber { get; set; }
    public string? LabelUrl { get; set; }
}

[Table("customer_addresses")]
public class CustomerAddress
{
    public string Owner { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Street { get; set; } = "";
    public string Complement { get; set; } = "";
    public string Postcode { get; set; } = "";
    public string City { get; set; } = "";
}
[Table("order_payments")]
public class OrderPayment
{
    public string OrderId { get; set; } = "";
    public int AmountPaid { get; set; }
    public int DiscountCents { get; set; }
    public int RefundedCents { get; set; }
    public string? InvoiceUrl { get; set; }
    public string? PaymentIntentId { get; set; }
}
[Table("return_requests")]
public class ReturnRequest
{
    public string OrderId { get; set; } = "";
    public string Details { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

[Table("product_listings")]
public class ProductListing
{
    public string ProductId { get; set; } = "";
    public Product Product { get; set; } = null!;
    public string Category { get; set; } = "";
    public string CollectionName { get; set; } = "";
    public string Color { get; set; } = "";
    public string Fit { get; set; } = "";
    public string? StaticImage { get; set; }
    public string? PhotoVersion { get; set; }
    public bool Published { get; set; }
    public int Position { get; set; } = 100;
    public int Revision { get; set; }
}
[Table("product_photos")]
public class ProductPhoto
{
    public string ProductId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}
[Table("stock_movements")]
public class StockMovement
{
    public long Seq { get; set; }
    public string OperationId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public int Delta { get; set; }
    public int BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
