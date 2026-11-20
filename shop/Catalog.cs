using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace BollTrack;

public record VariantQuantity(string SizeCode, int Quantity);
public record ProductShipping(int? UnitWeightGrams);
public record ProductEdit(int Revision, string Name, int PriceCents, string Category, string? Collection,
    string? Color, string? Fit, bool Published, string? Photo, List<VariantQuantity>? InitialStock, ProductShipping? Shipping = null);
public record StockChange(string OperationId, string SizeCode, int Delta, string Reason);

public static class Catalog
{
    private const int Limit = 1000000;

    public static async Task<object> List(ShopDb db, bool includeHidden, CancellationToken ct)
    {
        return await db.Listings.AsNoTracking().Where(l => includeHidden || l.Published)
            .OrderBy(l => l.Position).ThenBy(l => l.Product.Name).ThenBy(l => l.ProductId)
            .Select(l => new {
                id = l.ProductId, l.Product.Name, l.Product.PriceCents, l.Category,
                collection = l.CollectionName, l.Color, l.Fit, l.Published, l.Revision,
                unitWeightGrams = includeHidden ? db.ShippingWeights.Where(w => w.ProductId == l.ProductId)
                    .Select(w => (int?)w.WeightGrams).FirstOrDefault() : null,
                image = l.PhotoVersion == null ? l.StaticImage : "/api/catalogue/" + l.ProductId + "/photo?v=" + l.PhotoVersion,
                sizes = l.Product.Sizes.OrderBy(s => s.SizeCode).Select(s => new { s.SizeCode, s.Available }).ToList()
            }).ToListAsync(ct);
    }

    public static async Task<IResult> Photo(string id, HttpContext ctx, ShopDb db, IConfiguration config, CancellationToken ct)
    {
        var admin = ShopAdmin.IsAdmin(ctx.User, config);
        var photo = await (from l in db.Listings.AsNoTracking()
                           join p in db.Photos.AsNoTracking() on l.ProductId equals p.ProductId
                           where l.ProductId == id && (l.Published || admin)
                           select p).SingleOrDefaultAsync(ct);
        return photo == null ? Results.NotFound() : Results.File(photo.Bytes, photo.ContentType);
    }

    internal static string Text(string? value, int max, string field, bool required = true)
    {
        var text = value?.Trim() ?? "";
        if ((required && text.Length == 0) || text.Length > max || text.Any(char.IsControl))
            throw new ShopProblem(400, "Vérifie le champ « " + field + " ».");
        return text;
    }

    private static string Size(string? value)
    {
        var size = (value ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(size, "^[A-Z0-9][A-Z0-9 -]{0,11}$"))
            throw new ShopProblem(400, "Indique une taille valide, ou TU pour la taille unique.");
        return size;
    }

    private static (byte[] Bytes, string Type)? DecodePhoto(string? encoded)
    {
        if (encoded == null) return null;
        if (encoded.Length > 2800000) throw new ShopProblem(400, "La photo doit faire moins de 2 Mo.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new ShopProblem(400, "La photo est illisible."); }
        if (bytes.Length < 12 || bytes.Length > 2 * 1024 * 1024)
            throw new ShopProblem(400, "La photo doit faire moins de 2 Mo.");
        var data = bytes.AsSpan();
        string type;
        if (data.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) type = "image/jpeg";
        else if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) type = "image/png";
        else if (data.StartsWith("RIFF"u8) && data[8..].StartsWith("WEBP"u8)) type = "image/webp";
        else throw new ShopProblem(400, "Choisis une photo JPEG, PNG ou WebP.");
        return (bytes, type);
    }

    public static async Task<IResult> Save(string id, ProductEdit input, ClaimsPrincipal user, ShopDb db, CancellationToken ct)
    {
        var creating = input.Revision == 0;
        if (input.Revision < 0 || (creating && !Guid.TryParseExact(id, "N", out _)))
            throw new ShopProblem(400, "Référence de produit invalide.");
        var name = Text(input.Name, 120, "Nom du produit");
        if (input.PriceCents is < 1 or > 1000000) throw new ShopProblem(400, "Le prix doit être compris entre 0,01 € et 10 000 €.");
        if (input.Category is not ("tshirts" or "pulls" or "accessoires")) throw new ShopProblem(400, "Choisis une catégorie.");
        var collection = Text(input.Collection, 70, "Collection", false);
        var color = Text(input.Color, 40, "Couleur", false);
        var fit = Text(input.Fit, 32, "Coupe", false);
        var photo = DecodePhoto(input.Photo);
        if (input.Shipping?.UnitWeightGrams is < 1 or > 30000)
            throw new ShopProblem(400, "Le poids d’un article doit être compris entre 1 et 30 000 g.");
        var initial = input.InitialStock ?? [];
        if (initial.Count > 30 || (!creating && initial.Count != 0)) throw new ShopProblem(400, "Vérifie les variantes du produit.");
        var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var variant in initial)
        {
            if (variant == null || variant.Quantity is < 0 or > Limit || !quantities.TryAdd(Size(variant.SizeCode), variant.Quantity))
                throw new ShopProblem(400, "Chaque taille doit apparaître une seule fois avec un stock valide.");
        }
        if (creating && quantities.Count == 0) throw new ShopProblem(400, "Ajoute au moins une taille, ou TU.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var revisionRows = await db.Listings.FromSqlInterpolated($"SELECT * FROM product_listings WHERE ProductId={id} FOR UPDATE").ToListAsync(ct);
        var listing = revisionRows.SingleOrDefault();
        if (creating && listing != null) throw new ShopProblem(409, "Ce produit existe déjà. Actualise la liste.");
        if (!creating && listing == null) return Results.NotFound();
        if (!creating && listing!.Revision != input.Revision) throw new ShopProblem(409, "Cette fiche a changé. Recharge-la avant de la modifier.");
        Product product;
        if (creating)
        {
            product = new Product { Id = id };
            listing = new ProductListing { ProductId = id, Product = product };
            db.Listings.Add(listing);
        }
        else product = await db.Products.SingleAsync(p => p.Id == id, ct);
        product.Name = name;
        product.PriceCents = input.PriceCents;
        listing!.Category = input.Category;
        listing.CollectionName = collection;
        listing.Color = color;
        listing.Fit = fit;
        listing.Published = input.Published;
        listing.Revision++;
        foreach (var (size, count) in quantities)
            product.Sizes.Add(new Stock { ProductId = id, SizeCode = size, Available = count });
        if (photo != null) listing.PhotoVersion = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        if (input.Shipping is { } shipping)
        {
            var stored = await db.ShippingWeights.FindAsync([id], ct);
            if (shipping.UnitWeightGrams is int grams)
            {
                if (stored == null)
                    db.ShippingWeights.Add(new ProductShippingWeight { ProductId = id, WeightGrams = grams });
                else
                    stored.WeightGrams = grams;
            }
            else if (stored != null)
                db.ShippingWeights.Remove(stored);
        }
        foreach (var (size, count) in quantities.Where(q => q.Value > 0))
            db.Movements.Add(Movement(Guid.NewGuid().ToString("N"), id, size, count, count, "Stock initial", user));
        if (photo is { } image)
        {
            var stored = await db.Photos.FindAsync([id], ct);
            if (stored == null) { stored = new ProductPhoto { ProductId = id }; db.Photos.Add(stored); }
            stored.ContentType = image.Type;
            stored.Bytes = image.Bytes;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { id, listing.Revision });
    }

    public static async Task<IResult> Adjust(string id, StockChange input, ClaimsPrincipal user, ShopDb db, CancellationToken ct)
    {
        if (!Guid.TryParseExact(input.OperationId, "N", out _) || input.Delta is < -Limit or > Limit)
            throw new ShopProblem(400, "Mouvement de stock invalide.");
        var size = Size(input.SizeCode);
        var reason = Text(input.Reason, 120, "Motif");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var listings = await db.Listings.FromSqlInterpolated($"SELECT * FROM product_listings WHERE ProductId={id} FOR UPDATE").ToListAsync(ct);
        if (listings.Count == 0) return Results.NotFound();
        var prior = await db.Movements.AsNoTracking().SingleOrDefaultAsync(m => m.OperationId == input.OperationId, ct);
        if (prior != null)
        {
            if (prior.ProductId != id || prior.SizeCode != size || prior.Delta != input.Delta || prior.Reason != reason)
                throw new ShopProblem(409, "Cette référence de mouvement est déjà utilisée.");
            return Results.Ok(new { available = prior.BalanceAfter });
        }
        var rows = await db.Stock.FromSqlInterpolated($"SELECT * FROM stock WHERE ProductId={id} AND SizeCode={size} FOR UPDATE").ToListAsync(ct);
        var item = rows.SingleOrDefault();
        if (item != null && input.Delta == 0) throw new ShopProblem(400, "Indique la quantité à ajouter ou à retirer.");
        if (item == null)
        {
            item = new Stock { ProductId = id, SizeCode = size };
            db.Stock.Add(item);
        }
        var balance = (long)item.Available + input.Delta;
        if (balance < 0 || balance > Limit) throw new ShopProblem(409, "Le stock disponible ne permet pas ce mouvement.");
        item.Available = (int)balance;
        await db.SaveChangesAsync(ct);
        db.Movements.Add(Movement(input.OperationId, id, size, input.Delta, item.Available, reason, user));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { item.Available });
    }

    private static StockMovement Movement(string operation, string id, string size, int delta, int balance, string reason, ClaimsPrincipal actor)
        => new() { OperationId = operation, ProductId = id, SizeCode = size, Delta = delta, BalanceAfter = balance,
            Reason = reason, Actor = actor.FindFirstValue("sub")!, CreatedUtc = DateTime.UtcNow };
}
