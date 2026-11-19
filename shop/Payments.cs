using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Stripe;
using Stripe.Checkout;

namespace BollTrack;

public record CartItem(string ProductId, string SizeCode, int Quantity);
public record Checkout(string RequestId, List<CartItem> Items, Delivery Delivery);
public class Payments(ShopDb db, Shipping shipping, IConfiguration config, SessionService? sessions = null)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(config["Stripe:SecretKey"])
        && !string.IsNullOrWhiteSpace(config["Stripe:WebhookSecret"]) && shipping.Methods().Any(m => m.Available);
    private SessionService Sessions => sessions ?? throw new ShopProblem(503, "Stripe est indisponible.");

    public async Task<string> Create(string owner, string email, Checkout cart, string origin, CancellationToken ct)
    {
        if (!Enabled) throw new ShopProblem(503, "Les commandes ouvriront après la configuration du paiement et de la livraison.");
        if (!Guid.TryParseExact(cart.RequestId, "N", out _) || cart.Items is not { Count: > 0 and <= 60 }
            || cart.Delivery is null || cart.Items.Any(x => x is null || x.Quantity is < 1 or > 999
                || x.ProductId is not { Length: > 0 and <= 64 } || x.SizeCode is not { Length: > 0 and <= 12 }))
            throw new ShopProblem(400, "Le panier est incomplet.");
        var items = cart.Items.OrderBy(i => i.ProductId, StringComparer.Ordinal).ThenBy(i => i.SizeCode, StringComparer.Ordinal).ToList();
        if (items.DistinctBy(i => (i.ProductId,i.SizeCode)).Count() != items.Count)
            throw new ShopProblem(400, "Une taille figure deux fois dans le panier.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { items, cart.Delivery })));
        var existing = await db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == cart.RequestId, ct);
        if (existing != null) return await Resume(existing, owner, fingerprint, origin, ct);
        var method = shipping.Methods().Find(m => m.Id == cart.Delivery.Mode && m.Available)
            ?? throw new ShopProblem(400, "Ce mode de livraison est indisponible.");
        var delivery = await shipping.Validate(cart.Delivery, ct);
        var order = new ShopOrder { Id=cart.RequestId, Owner=owner, Email=email, CreatedUtc=DateTime.UtcNow,
            ExpiresUtc=DateTime.UtcNow.AddMinutes(40), DeliveryJson=JsonSerializer.Serialize(delivery),
            RequestHash=fingerprint, ShippingCents=method.Cents, TotalCents=method.Cents };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            foreach (var item in items)
            {
                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == item.ProductId, ct);
                var reserved = await db.Stock.Where(s => s.ProductId == item.ProductId && s.SizeCode == item.SizeCode && s.Available >= item.Quantity)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.Available, s => s.Available - item.Quantity), ct);
                if (product is null || reserved != 1)
                    throw new ShopProblem(409, $"Stock insuffisant pour {product?.Name ?? "cet article"} en {item.SizeCode}.");
                order.Lines.Add(new() { OrderId=order.Id, ProductId=product.Id, SizeCode=item.SizeCode,
                    Name=product.Name, PriceCents=product.PriceCents, Quantity=item.Quantity });
                order.TotalCents = checked(order.TotalCents + product.PriceCents * item.Quantity);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is MySqlException { Number: 1062 })
        {
            db.ChangeTracker.Clear();
            var retry = await db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == cart.RequestId, ct);
            if (retry == null) throw new ShopProblem(409, "Un paiement est déjà en cours. Retrouve-le dans Mon compte.");
            return await Resume(retry, owner, fingerprint, origin, ct);
        }
        return await Resume(order, owner, fingerprint, origin, ct);
    }

    private async Task<string> Resume(ShopOrder order, string owner, string hash, string origin, CancellationToken ct)
    {
        if (order.Owner != owner || order.RequestHash != hash || order.Status != "pending" || order.ExpiresUtc <= DateTime.UtcNow)
            throw new ShopProblem(409, "Ce panier a changé ou a expiré. Actualise-le puis réessaie.");
        if (order.CheckoutUrl is { } url) return url;
        var delivery = JsonSerializer.Deserialize<ShippingDetails>(order.DeliveryJson)!;
        var lines = order.Lines.OrderBy(l => l.ProductId, StringComparer.Ordinal).ThenBy(l => l.SizeCode, StringComparer.Ordinal)
            .Select(l => Line(l.Name + " · " + l.SizeCode, l.Quantity, l.PriceCents)).ToList();
        var shippingName = delivery.Locker == null ? "Mondial Relay à domicile" : "Mondial Relay en Locker";
        var options = new SessionCreateOptions {
            Mode="payment", Locale="fr", PaymentMethodTypes=["card"], CustomerEmail=order.Email,
            ClientReferenceId=order.Id, Metadata=new(){["bolltrack_order"]=order.Id, ["pricing"]="shipping_rate_v1"}, LineItems=lines,
            AllowPromotionCodes=true, AdaptivePricing=new() { Enabled=false },
            ShippingOptions=[new() { ShippingRateData=new() { Type="fixed_amount", DisplayName=shippingName,
                FixedAmount=new() { Amount=order.ShippingCents, Currency="eur" } } }],
            PaymentIntentData=new() { Metadata=new() { ["bolltrack_order"]=order.Id }, Shipping=new() {
                Name=delivery.Recipient.Name, Phone=delivery.Recipient.Phone, Address=new() {
                    Line1=delivery.Locker?.Street ?? delivery.Recipient.Street,
                    Line2=delivery.Locker == null ? delivery.Recipient.Complement : delivery.Locker.Name,
                    PostalCode=delivery.Locker?.Postcode ?? delivery.Recipient.Postcode,
                    City=delivery.Locker?.City ?? delivery.Recipient.City, Country=delivery.Country } } },
            ExpiresAt=order.ExpiresUtc, InvoiceCreation=new() { Enabled=true, InvoiceData=new() {
                Description="Commande BollTrack " + order.Id[..12].ToUpperInvariant(),
                Metadata=new() { ["bolltrack_order"]=order.Id } } },
            SuccessUrl=origin + "/boutique.html?commande=" + order.Id + "#compte",
            CancelUrl=origin + "/boutique.html#panier"
        };
        Session session;
        try
        {
            session=await Sessions.CreateAsync(options, new RequestOptions{IdempotencyKey="bolltrack-"+order.Id}, ct);
        }
        catch (StripeException e) when (e.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // These requests were rejected by Stripe. An unissued link can safely be released.
            await Release(order.Id, null, CancellationToken.None);
            throw new ShopProblem(503, "Le paiement n’a pas pu démarrer. Ton panier est conservé.");
        }
        var saved=await db.Orders.Where(o => o.Id==order.Id && o.Status=="pending")
            .ExecuteUpdateAsync(set=>set.SetProperty(o=>o.StripeSessionId,session.Id).SetProperty(o=>o.CheckoutUrl,session.Url), ct);
        if (saved == 0) throw new ShopProblem(409, "Ce paiement est déjà clôturé.");
        return session.Url;
    }

    private static SessionLineItemOptions Line(string name, int count, int cents) => new() {
        Quantity=count, PriceData=new() {Currency="eur", UnitAmount=cents, ProductData=new(){Name=name}}
    };

    public async Task Apply(Session session, CancellationToken ct)
    {
        if (session.Metadata?.TryGetValue("bolltrack_order", out var id) != true || id != session.ClientReferenceId) return;
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order == null || (order.StripeSessionId != null && order.StripeSessionId != session.Id)) return;
        if (session.Status == "expired") { await Release(id, session.Id, ct); return; }
        if (session.Status != "complete" || (session.PaymentStatus != "paid"
            && !(session.PaymentStatus == "no_payment_required" && session.AmountTotal == 0))) return;
        var discount = session.TotalDetails?.AmountDiscount ?? 0;
        var goods = order.TotalCents - order.ShippingCents;
        var valid = session.Currency == "eur" && session.AmountTotal == order.TotalCents - discount;
        if (session.Metadata.GetValueOrDefault("pricing") == "shipping_rate_v1")
            valid &= session.AmountSubtotal == goods && session.TotalDetails?.AmountShipping == order.ShippingCents
                && session.TotalDetails.AmountTax == 0 && discount >= 0 && discount <= goods;
        else
            valid &= discount == 0; // Sessions émises avant la prise en charge des codes promo.
        if (!valid) throw new InvalidOperationException("Montant Stripe incohérent.");

        var paid = checked((int)session.AmountTotal!.Value);
        var invoice = session.Invoice?.HostedInvoiceUrl ?? session.Invoice?.InvoicePdf;
        var refund = session.PaymentIntent?.LatestCharge?.AmountRefunded;
        if (refund < 0 || refund > paid) throw new InvalidOperationException("Remboursement Stripe incohérent.");
        var receipt = invoice ?? session.PaymentIntent?.LatestCharge?.ReceiptUrl;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var updated = await db.Orders.Where(o=>o.Id==id && (o.Status=="pending" || o.Status=="paid"))
            .ExecuteUpdateAsync(set=>set.SetProperty(o=>o.Status,"paid").SetProperty(o=>o.StripeSessionId,session.Id)
                .SetProperty(o=>o.CheckoutUrl,(string?)null).SetProperty(o=>o.ReceiptUrl,o=>receipt ?? o.ReceiptUrl), ct);
        if (updated != 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO order_payments (OrderId,AmountPaid,DiscountCents,RefundedCents,InvoiceUrl,PaymentIntentId)
                VALUES ({id},{paid},{discount},{refund ?? 0},{invoice},{session.PaymentIntentId}) AS incoming
                ON DUPLICATE KEY UPDATE InvoiceUrl=COALESCE(incoming.InvoiceUrl,order_payments.InvoiceUrl),
                    RefundedCents=COALESCE({refund},order_payments.RefundedCents),
                    PaymentIntentId=COALESCE(incoming.PaymentIntentId,order_payments.PaymentIntentId)
                """, ct);
        await tx.CommitAsync(ct);
    }

    public async Task RefreshRefund(string? paymentIntent, CancellationToken ct, string? orderReference = null)
    {
        if (string.IsNullOrEmpty(paymentIntent)) return;
        var order = await (from p in db.Payments where p.PaymentIntentId == paymentIntent
                           join o in db.Orders.AsNoTracking() on p.OrderId equals o.Id select o).SingleOrDefaultAsync(ct);
        if (order == null && Guid.TryParseExact(orderReference, "N", out _))
            order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderReference, ct);
        if (order?.StripeSessionId != null) await Refresh(order, ct);
    }

    private async Task Release(string id, string? sessionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        var changed = await db.Orders.Where(o=>o.Id==id && o.Status=="pending" && (o.StripeSessionId==null || o.StripeSessionId==sessionId))
            .ExecuteUpdateAsync(set=>set.SetProperty(o=>o.Status,"expired").SetProperty(o=>o.CheckoutUrl,(string?)null),ct);
        if (changed==1)
        {
            var lines=await db.OrderLines.AsNoTracking().Where(l=>l.OrderId==id).OrderBy(l=>l.ProductId).ThenBy(l=>l.SizeCode).ToListAsync(ct);
            foreach(var line in lines)
                await db.Stock.Where(s=>s.ProductId==line.ProductId && s.SizeCode==line.SizeCode)
                    .ExecuteUpdateAsync(set=>set.SetProperty(s=>s.Available,s=>s.Available+line.Quantity),ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task Refresh(ShopOrder order, CancellationToken ct)
    {
        if (order.StripeSessionId == null)
        {
            // URLs are only handed to customers after the Stripe session ID is committed.
            if (order.ExpiresUtc.AddMinutes(2) < DateTime.UtcNow) await Release(order.Id,null,ct);
            return;
        }
        await Apply(await Sessions.GetAsync(order.StripeSessionId, new() {Expand=["invoice","payment_intent.latest_charge"]}, cancellationToken:ct), ct);
    }

    public async Task Cancel(ShopOrder order, CancellationToken ct)
    {
        if (order.Status!="pending") return;
        if (order.StripeSessionId==null) throw new ShopProblem(409,"Le paiement est en cours de création. Réessaie dans un instant.");
        try { await Apply(await Sessions.ExpireAsync(order.StripeSessionId, cancellationToken:ct),ct); }
        catch (StripeException e) when (e.HttpStatusCode == HttpStatusCode.BadRequest) { await Refresh(order,ct); }
    }
}

public sealed class PaymentRecovery(IServiceScopeFactory scopes, ILogger<PaymentRecovery> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var scope=scopes.CreateScope();
                var db=scope.ServiceProvider.GetRequiredService<ShopDb>();
                var service=scope.ServiceProvider.GetRequiredService<Payments>();
                if (string.IsNullOrEmpty(scope.ServiceProvider.GetRequiredService<IConfiguration>()["Stripe:SecretKey"])) continue;
                var due=DateTime.UtcNow.AddMinutes(-2);
                var pending=await db.Orders.AsNoTracking().Where(o=>o.Status=="pending" && o.ExpiresUtc<due)
                    .OrderBy(o=>o.ExpiresUtc).Take(50).ToListAsync(ct);
                foreach(var o in pending)
                {
                    try { await service.Refresh(o,ct); }
                    catch(Exception e) when (!ct.IsCancellationRequested) { log.LogWarning("Rapprochement Stripe différé ({Type})", e.GetType().Name); }
                }
            }
            catch(Exception e) when (!ct.IsCancellationRequested) { log.LogWarning("Lecture des paiements différée ({Type})",e.GetType().Name); }
        }
    }
}
