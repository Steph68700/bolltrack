using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BollTrack;

public record AddressInput(string FirstName, string LastName, string Street, string? Complement, string Postcode, string City)
{
    public AddressInput Normalize()
    {
        static string Text(string? input, int max, string label)
        {
            var value = input?.Trim() ?? "";
            if (value.Length == 0 || value.Length > max || value.Any(char.IsControl))
                throw new ShopProblem(400, $"Vérifie le champ « {label} ».");
            return value;
        }
        Shipping.ValidatePostcode(Postcode?.Trim());
        return new(Text(FirstName, 60, "Prénom"), Text(LastName, 60, "Nom"), Text(Street, 40, "Adresse"),
            string.IsNullOrWhiteSpace(Complement) ? "" : Text(Complement, 30, "Complément"), Postcode!.Trim(), Text(City, 30, "Ville"));
    }
}
public record ReturnInput(string? Details);

public static class CustomerAccount
{
    public static void MapCustomerAccount(this WebApplication app)
    {
        app.MapGet("/api/compte", Read);
        app.MapPost("/api/compte/adresse", SaveAddress).RequireAuthorization();
        app.MapPost("/api/commandes/{id}/retour", RequestReturn).RequireAuthorization();
        app.MapPost("/api/commandes/{id}/facture", Invoice).RequireAuthorization();
    }

    private static async Task<IResult> Read(ClaimsPrincipal user, ShopDb db, IConfiguration config, CancellationToken ct)
    {
        if (user.FindFirstValue("sub") is not {} owner) return Results.Ok(new { connected = false });
        var address = await db.CustomerAddresses.AsNoTracking().Where(a => a.Owner == owner)
            .Select(a => new AddressInput(a.FirstName, a.LastName, a.Street, a.Complement, a.Postcode, a.City)).SingleOrDefaultAsync(ct);
        var orders = await db.Orders.AsNoTracking().Where(o => o.Owner == owner).OrderByDescending(o => o.CreatedUtc)
            .Take(100).Include(o => o.Lines).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToArray();
        var payments = ids.Length == 0 ? new Dictionary<string, OrderPayment>() : await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.OrderId)).ToDictionaryAsync(p => p.OrderId, ct);
        var returns = ids.Length == 0 ? new Dictionary<string, ReturnRequest>() : await db.Returns.AsNoTracking()
            .Where(r => ids.Contains(r.OrderId)).ToDictionaryAsync(r => r.OrderId, ct);
        var tracking = ids.Length == 0 ? new Dictionary<string, string?>() : await db.Shipments.AsNoTracking()
            .Where(s => s.Status == "ready" && ids.Contains(s.OrderId)).ToDictionaryAsync(s => s.OrderId, s => s.TrackingNumber, ct);
        return Results.Ok(new { connected = true, isAdmin = ShopAdmin.IsAdmin(user, config), address, orders = orders.Select(o => {
            var payment = payments.GetValueOrDefault(o.Id);
            var requested = returns.GetValueOrDefault(o.Id);
            var delivery = JsonSerializer.Deserialize<ShippingDetails>(o.DeliveryJson);
            return new { o.Id, o.Status, o.CreatedUtc, totalCents = payment?.AmountPaid ?? o.TotalCents,
                o.ShippingCents, discountCents = payment?.DiscountCents ?? 0, refundedCents = payment?.RefundedCents ?? 0,
                trackingNumber = tracking.GetValueOrDefault(o.Id), lockerName = delivery?.Locker?.Name,
                checkoutUrl = o.Status == "pending" && o.ExpiresUtc > DateTime.UtcNow ? o.CheckoutUrl : null,
                returnRequest = requested is null ? null : new { requested.Status, requested.Details },
                lines = o.Lines.Select(l => new { l.Name, l.SizeCode, l.Quantity, l.PriceCents }) };
        }) });
    }

    private static async Task<IResult> SaveAddress(ClaimsPrincipal user, AddressInput input, ShopDb db, CancellationToken ct)
    {
        if (user.FindFirstValue("sub") is not {} owner) return Results.Unauthorized();
        var a = input.Normalize();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO customer_addresses (Owner,FirstName,LastName,Street,Complement,Postcode,City)
            VALUES ({owner},{a.FirstName},{a.LastName},{a.Street},{a.Complement},{a.Postcode},{a.City}) AS incoming
            ON DUPLICATE KEY UPDATE FirstName=incoming.FirstName, LastName=incoming.LastName,
                Street=incoming.Street, Complement=incoming.Complement, Postcode=incoming.Postcode, City=incoming.City
            """, ct);
        return Results.Ok(a);
    }

    private static async Task<IResult> RequestReturn(string id, ClaimsPrincipal user, ReturnInput input, ShopDb db, CancellationToken ct)
    {
        var owner = user.FindFirstValue("sub");
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.Owner == owner, ct);
        if (order == null) return Results.NotFound();
        if (order.Status != "paid") throw new ShopProblem(409, "Le retour concerne une commande payée.");
        var details = input.Details?.Trim();
        if (string.IsNullOrEmpty(details) || details.Length > 1000 || details.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
            throw new ShopProblem(400, "Indique les articles et quantités à retourner (1 000 caractères maximum).");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO return_requests (OrderId,Details,CreatedUtc) VALUES ({id},{details},UTC_TIMESTAMP(6)) AS incoming
            ON DUPLICATE KEY UPDATE Details=IF(return_requests.Status='requested',incoming.Details,return_requests.Details)
            """, ct);
        var status = await db.Returns.Where(r => r.OrderId == id).Select(r => r.Status).SingleAsync(ct);
        if (status != "requested") throw new ShopProblem(409, "BollTrack traite déjà cette demande. Actualise ta commande.");
        return Results.NoContent();
    }

    private static async Task<IResult> Invoice(string id, ClaimsPrincipal user, ShopDb db, Payments pay, CancellationToken ct)
    {
        var owner = user.FindFirstValue("sub");
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.Owner == owner, ct);
        if (order == null) return Results.NotFound();
        if (order.Status != "paid") throw new ShopProblem(409, "La facture est disponible après paiement.");
        async Task<string?> Url() => await db.Payments.Where(p => p.OrderId == id).Select(p => p.InvoiceUrl).SingleOrDefaultAsync(ct);
        var url = await Url();
        if (url == null && order.StripeSessionId != null)
        {
            await pay.Refresh(order, ct);
            url = await Url();
        }
        if (url == null) throw new ShopProblem(409, "Stripe prépare la facture. Réessaie dans quelques instants.");
        return Results.Ok(new { url });
    }
}
