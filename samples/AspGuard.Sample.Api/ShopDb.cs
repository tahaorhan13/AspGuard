using Microsoft.EntityFrameworkCore;

namespace AspGuard.Sample.Api;

public class Order
{
    public int Id { get; set; }

    public string Customer { get; set; } = "";

    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string Product { get; set; } = "";
}

public record OrderDto(int Id, string Customer, int ItemCount);

public class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
}

public static class SeedData
{
    public const int OrderCount = 20;

    public static void Create(ShopDb db)
    {
        db.Database.EnsureCreated();
        for (var i = 1; i <= OrderCount; i++)
        {
            db.Orders.Add(new Order
            {
                Customer = $"customer-{i}",
                Items = [new OrderItem { Product = "a" }, new OrderItem { Product = "b" }, new OrderItem { Product = "c" }],
            });
        }

        db.SaveChanges();
    }
}
