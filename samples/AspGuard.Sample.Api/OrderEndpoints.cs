using Microsoft.EntityFrameworkCore;

namespace AspGuard.Sample.Api;

public static class OrderEndpoints
{
    /// <summary>Deliberate N+1: one query for the orders, then one query per order for its items.</summary>
    public static async Task<List<OrderDto>> NPlusOneAsync(ShopDb db)
    {
        var result = new List<OrderDto>();
        foreach (var order in await db.Orders.ToListAsync())
        {
            var items = await db.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync();
            result.Add(new OrderDto(order.Id, order.Customer, items.Count));
        }

        return result;
    }

    /// <summary>The fix: load the items with the orders in one query.</summary>
    public static async Task<List<OrderDto>> FixedAsync(ShopDb db)
    {
        var orders = await db.Orders.Include(o => o.Items).ToListAsync();
        return orders.Select(o => new OrderDto(o.Id, o.Customer, o.Items.Count)).ToList();
    }
}
