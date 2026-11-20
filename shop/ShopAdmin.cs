using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace BollTrack;

public record TrackingInput(string Number, string? Previous);
public record StatusChangeInput(string Status, string? Previous);

public static class ShopAdmin
{
    public const string Policy = "BollTrackAdmin";

    public static bool IsAdmin(ClaimsPrincipal user, IConfiguration config)
    {
        if (user.Identity?.IsAuthenticated != true || user.FindFirstValue("sub") is not { Length: > 0 } subject)
            return false;
        var subjects = (config["Shop:AdminSubjects"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (subjects.Length > 0 && !subjects.Contains(subject, StringComparer.Ordinal)) return false;
        var allowed = (config["Shop:AdminEmails"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (allowed.Length == 0) return subjects.Length > 0;
        return bool.TryParse(user.FindFirstValue("email_verified"), out var verified) && verified
            && allowed.Contains(user.FindFirstValue("email") ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public static void MapShopAdmin(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Policy);
        admin.MapGet("/produits", (ShopDb db, CancellationToken ct) => Catalog.List(db, true, ct));
        admin.MapPost("/produits/{id}", Catalog.Save);
        admin.MapPost("/produits/{id}/stock", Catalog.Adjust);
        admin.MapGet("/mouvements", async (ShopDb db, string? productId, CancellationToken ct) =>
            await (from m in db.Movements.AsNoTracking() join p in db.Products.AsNoTracking() on m.ProductId equals p.Id
                   where productId == null || m.ProductId == productId orderby m.Seq descending
                   select new { m.ProductId, name = p.Name, m.SizeCode, m.Delta, m.BalanceAfter, m.Reason, m.CreatedUtc }
                ).Take(100).ToListAsync(ct));
        admin.MapGet("/commandes", Orders);
        admin.MapPost("/commandes/{id}/etiquette", async (string id, ParcelMeasurements parcel, Labels labels, CancellationToken ct) =>
            Results.Ok(await labels.Issue(id, parcel, ct)));
        admin.MapPost("/commandes/{id}/suivi", SetTracking);
        admin.MapPost("/commandes/{id}/expedition", SetFulfillment);
        admin.MapPost("/commandes/{id}/retour", SetReturnStatus);
    }

    private static async Task<IResult> Orders(int? page, string? filter, ShopDb db, IConfiguration config, CancellationToken ct)
    {
        const int pageSize = 30;
        var index = page ?? 0;
        if (index < 0 || index > 100000) throw new ShopProblem(400, "Page invalide.");
        var query = db.Orders.AsNoTracking();
        query = filter switch {
            "retours" => query.Where(o => db.Returns.Any(r => r.OrderId == o.Id)),
            "preparer" => query.Where(o => o.Status == "paid" && (o.Fulfillment == null || o.Fulfillment.Status != OrderFulfillment.Shipped)
                && !db.Payments.Any(p => p.OrderId == o.Id && p.AmountPaid > 0 && p.RefundedCents >= p.AmountPaid)),
            "expediees" => query.Where(o => o.Fulfillment != null && o.Fulfillment.Status == OrderFulfillment.Shipped),
            "" or null or "toutes" => query,
            _ => throw new ShopProblem(400, "Filtre de commandes invalide.")
        };
        var orders = await query.OrderByDescending(o => o.CreatedUtc).ThenByDescending(o => o.Id)
            .Skip(index * pageSize).Take(pageSize + 1).Include(o => o.Lines).Include(o => o.Fulfillment).ToListAsync(ct);
        var more = orders.Count > pageSize;
        if (more) orders.RemoveAt(pageSize);
        var labelsConfigured = Labels.IsConfigured(config);
        var labelSandbox = config.GetValue<bool>("MondialRelay:Sandbox");
        if (orders.Count == 0) return Results.Ok(new { more = false, labelsConfigured, labelSandbox, orders = Array.Empty<object>() });
        var ids = orders.Select(o => o.Id).ToArray();
        var notices = await db.Withdrawals.AsNoTracking().Where(n => ids.Contains(n.OrderId)).OrderByDescending(n => n.SubmittedUtc)
            .Select(n => new { n.OrderId, n.FirstName, n.LastName, n.Details, n.SubmittedUtc }).ToListAsync(ct);
        var byOrder = notices.ToLookup(n => n.OrderId);
        var payments = await db.Payments.AsNoTracking().Where(p => ids.Contains(p.OrderId)).ToDictionaryAsync(p => p.OrderId, ct);
        var shipments = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.OrderId)).ToDictionaryAsync(s => s.OrderId, ct);
        var returns = await db.Returns.AsNoTracking().Where(r => ids.Contains(r.OrderId)).ToDictionaryAsync(r => r.OrderId, ct);
        return Results.Ok(new { more, labelsConfigured, labelSandbox, orders = orders.Select(o => {
            var pay = payments.GetValueOrDefault(o.Id);
            var shipment = shipments.GetValueOrDefault(o.Id);
            var request = returns.GetValueOrDefault(o.Id);
            var delivery = JsonSerializer.Deserialize<ShippingDetails>(o.DeliveryJson);
            var canManageShipping = o.Status == "paid" && (pay == null || pay.AmountPaid == 0 || pay.RefundedCents < pay.AmountPaid);
            return new { o.Id, o.CreatedUtc, o.Email, o.Status, withdrawals = byOrder[o.Id], delivery, o.ShippingCents,
                totalCents = pay?.AmountPaid ?? o.TotalCents, refundedCents = pay?.RefundedCents ?? 0,
                tracking = shipment?.TrackingNumber, shippingStatus = shipment?.Status, labelUrl = shipment?.LabelUrl,
                canCreateLabel = canManageShipping && shipment == null && o.Fulfillment?.Status != OrderFulfillment.Shipped,
                fulfillmentStatus = o.Fulfillment?.Status, canSetFulfillment = canManageShipping,
                canSetTracking = canManageShipping
                    && (shipment == null || (shipment.Status == "ready" && shipment.LabelUrl == null)),
                returnRequest = request == null ? null : new { request.Status, request.Details, request.CreatedUtc },
                lines = o.Lines.Select(l => new { l.Name, l.SizeCode, l.Quantity }) };
        }) });
    }

    // The Stripe payment/refund update also locks this order before writing its payment row.
    internal static async Task<ShopOrder?> LockPaidOrder(string id, ShopDb db, CancellationToken ct)
    {
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM orders WHERE Id={id} FOR UPDATE").AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if (order == null) return null;
        if (order.Status != "paid") throw new ShopProblem(409, "Seule une commande payée peut être expédiée.");
        if (await db.Payments.AnyAsync(p => p.OrderId == id && p.AmountPaid > 0 && p.RefundedCents >= p.AmountPaid, ct))
            throw new ShopProblem(409, "Cette commande a déjà été intégralement remboursée.");
        return order;
    }

    private static async Task<IResult> SetFulfillment(string id, StatusChangeInput input, ShopDb db, CancellationToken ct)
    {
        if (input.Status is not (OrderFulfillment.Preparing or OrderFulfillment.Shipped))
            throw new ShopProblem(400, "Choisis un état d’expédition.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (await LockPaidOrder(id, db, ct) == null) return Results.NotFound();
        var states = db.Set<OrderFulfillment>();
        var state = await states.FindAsync([id], ct);
        if (state?.Status == input.Status) return Results.NoContent();
        if (state?.Status != input.Previous)
            throw new ShopProblem(409, "L’état d’expédition a changé. Actualise la commande.");
        if (state == null)
            states.Add(new OrderFulfillment { OrderId = id, Status = input.Status });
        else
            state.Status = input.Status;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetTracking(string id, TrackingInput input, ShopDb db, CancellationToken ct)
    {
        var number = (input.Number ?? "").Trim();
        if (!Regex.IsMatch(number, "^[0-9]{6,20}$")) throw new ShopProblem(400, "Indique le numéro de suivi Mondial Relay (6 à 20 chiffres).");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (await LockPaidOrder(id, db, ct) == null) return Results.NotFound();
        var shipment = await db.Shipments.FindAsync([id], ct);
        if (shipment is { Status: "ready" } && shipment.TrackingNumber == number) return Results.NoContent();
        if (shipment != null && (shipment.Status != "ready" || shipment.LabelUrl != null || shipment.TrackingNumber != input.Previous))
            throw new ShopProblem(409, "Cette expédition a changé ou une étiquette est déjà associée. Actualise la commande.");
        if (shipment == null)
        {
            if (input.Previous != null) throw new ShopProblem(409, "Actualise la commande.");
            shipment = new Shipment { OrderId = id };
            db.Shipments.Add(shipment);
        }
        shipment.TrackingNumber = number;
        shipment.Status = "ready";
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is MySqlException { Number: 1062 })
        { throw new ShopProblem(409, "Une demande d’expédition existe déjà. Actualise la commande."); }
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetReturnStatus(string id, StatusChangeInput input, ShopDb db, CancellationToken ct)
    {
        var request = await db.Returns.AsNoTracking().SingleOrDefaultAsync(r => r.OrderId == id, ct);
        if (request == null) return Results.NotFound();
        if (input.Status == request.Status) return Results.NoContent();
        var possible = input.Previous switch {
            "requested" => input.Status is "processing" or "received" or "closed",
            "processing" => input.Status is "received" or "closed",
            "received" => input.Status == "closed",
            _ => false
        };
        if (!possible) throw new ShopProblem(400, "Transition de retour invalide.");
        var changed = await db.Returns.Where(r => r.OrderId == id && r.Status == input.Previous)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, input.Status), ct);
        if (changed == 0) throw new ShopProblem(409, "Le retour a changé. Actualise la commande.");
        return Results.NoContent();
    }
}
