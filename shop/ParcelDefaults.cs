using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BollTrack;

[Table("parcel_defaults")]
public class ParcelDefaults
{
    [JsonIgnore] public int Id { get; set; } = 1;
    public int Revision { get; set; }
    public int? UnitWeightGrams { get; set; }
    public int? PackagingGrams { get; set; }
    public int? LengthCm { get; set; }
    public int? WidthCm { get; set; }
    public int? HeightCm { get; set; }

    public long? EstimateWeight(IReadOnlyCollection<OrderLine> lines, IReadOnlyDictionary<string, int> productWeights)
    {
        if (PackagingGrams is not int packaging || lines.Count == 0) return null;
        long total = packaging;
        foreach (var line in lines)
        {
            int? unitWeight = productWeights.TryGetValue(line.ProductId, out var grams) ? grams : UnitWeightGrams;
            if (unitWeight == null) return null;
            total += (long)line.Quantity * unitWeight.Value;
        }
        return total;
    }

    public static Task<ParcelDefaults> Read(ShopDb db, CancellationToken ct) =>
        db.ParcelDefaults.AsNoTracking().SingleAsync(p => p.Id == 1, ct);

    public static async Task<IResult> Save(ParcelDefaults input, ShopDb db, CancellationToken ct)
    {
        if (input.UnitWeightGrams is < 1 or > 30000 || input.PackagingGrams is < 0 or > 30000
            || input.LengthCm is <= 0 || input.WidthCm is <= 0 || input.HeightCm is <= 0)
            throw new ShopProblem(400, "Vérifie les poids en grammes et les dimensions en centimètres.");

        var changed = await db.ParcelDefaults.Where(p => p.Id == 1 && p.Revision == input.Revision)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.UnitWeightGrams, input.UnitWeightGrams)
                .SetProperty(p => p.PackagingGrams, input.PackagingGrams)
                .SetProperty(p => p.LengthCm, input.LengthCm)
                .SetProperty(p => p.WidthCm, input.WidthCm)
                .SetProperty(p => p.HeightCm, input.HeightCm)
                .SetProperty(p => p.Revision, p => p.Revision + 1), ct);
        if (changed == 0)
            throw new ShopProblem(409, "Les réglages colis ont été modifiés. Recharge-les avant d’enregistrer.");
        return Results.Ok(new { revision = input.Revision + 1 });
    }
}
