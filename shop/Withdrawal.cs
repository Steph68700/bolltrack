using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace BollTrack;

public record WithdrawalInput(string NoticeId, string FirstName, string LastName, string Details);

public static class Withdrawal
{
    public static void MapWithdrawals(this WebApplication app)
    {
        var group = app.MapGroup("/api/commandes/{id}/retractations").RequireAuthorization();
        group.MapPost("", Submit);
        group.MapGet("/{noticeId}/accuse", Receipt);
    }

    private static async Task<IResult> Submit(string id, WithdrawalInput input, ClaimsPrincipal user, ShopDb db, CancellationToken ct)
    {
        if (!Guid.TryParseExact(input.NoticeId, "N", out _)) throw new ShopProblem(400, "Référence de déclaration invalide.");
        var first = Catalog.Text(input.FirstName, 60, "Prénom");
        var last = Catalog.Text(input.LastName, 60, "Nom");
        var details = (input.Details ?? "").Trim();
        if (details.Length is < 1 or > 1000 || details.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ShopProblem(400, "Indique la commande entière ou les articles concernés.");
        var owner = user.FindFirstValue("sub");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var orders = await db.Orders.FromSqlInterpolated($"SELECT * FROM orders WHERE Id={id} AND Owner={owner} FOR UPDATE").AsNoTracking().ToListAsync(ct);
        if (orders.Count != 1) return Results.NotFound();
        if (orders[0].Status != "paid") throw new ShopProblem(409, "Choisis une commande payée ou contacte directement l’association.");
        var notice = await db.Withdrawals.FindAsync([input.NoticeId], ct);
        if (notice == null)
        {
            notice = new WithdrawalNotice { Id=input.NoticeId, OrderId=id, FirstName=first, LastName=last, Details=details, SubmittedUtc=DateTime.UtcNow };
            db.Withdrawals.Add(notice);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO return_requests (OrderId,Details,Status,CreatedUtc) VALUES ({id},{details},'requested',UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE Status=IF(Status='closed','requested',Status)
                """, ct);
            await db.SaveChangesAsync(ct);
        }
        else if (notice.OrderId != id || notice.FirstName != first || notice.LastName != last || notice.Details != details)
            throw new ShopProblem(409, "Cette référence correspond déjà à une autre déclaration.");
        await tx.CommitAsync(ct);
        return Results.Ok(new { notice.Id, notice.SubmittedUtc });
    }

    private static async Task<IResult> Receipt(string id, string noticeId, ClaimsPrincipal user, ShopDb db, CancellationToken ct)
    {
        var owner = user.FindFirstValue("sub");
        var n = await (from notice in db.Withdrawals.AsNoTracking() join order in db.Orders.AsNoTracking() on notice.OrderId equals order.Id
                       where notice.Id == noticeId && order.Id == id && order.Owner == owner select notice).SingleOrDefaultAsync(ct);
        if (n == null) return Results.NotFound();
        var stamp = n.SubmittedUtc.ToString("dd/MM/yyyy à HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var text = $"BollTrack — Accusé de réception de rétractation\r\n\r\nAssociation Omen Haze\r\n103 avenue du Château, 68540 Bollwiller\r\nomenhaze68@gmail.com\r\n\r\nDéclaration {n.Id}\r\nCommande {id}\r\nTransmise et enregistrée le {stamp}\r\n\r\n{n.FirstName} {n.LastName} déclare se rétracter pour la commande ou les articles suivants.\r\n{n.Details}\r\n";
        return Results.File(Encoding.UTF8.GetBytes("\uFEFF" + text), "text/plain; charset=utf-8", $"BollTrack-retractation-{n.Id}.txt");
    }
}
