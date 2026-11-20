using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;

namespace BollTrack;

public record ParcelMeasurements(int WeightGrams, int? LengthCm, int? WidthCm, int? HeightCm);
public record ShipmentLabel(string TrackingNumber, string LabelUrl);

public class Labels(ShopDb db, IConfiguration config, IHttpClientFactory clients)
{
    private static readonly string[] RequiredSettings = [
        "Api2Login", "Api2Password", "Api2Client", "Expediteur:Nom", "Expediteur:Rue",
        "Expediteur:CodePostal", "Expediteur:Ville", "Expediteur:Telephone", "Expediteur:Email"
    ];

    public static bool IsConfigured(IConfiguration config) =>
        config["MondialRelay:Collecte"] is "REL" or "CCC"
        && RequiredSettings.All(name => !string.IsNullOrWhiteSpace(config["MondialRelay:" + name]));

    public async Task RunCommand(string[] args)
    {
        if (args.Length != 6 || !int.TryParse(args[2], out var weight) || !int.TryParse(args[3], out var length)
            || !int.TryParse(args[4], out var width) || !int.TryParse(args[5], out var height))
            throw new ShopProblem(400, "Usage : dotnet Shop.dll etiquette ID_COMMANDE POIDS_G LONGUEUR_CM LARGEUR_CM HAUTEUR_CM");
        var label = await Issue(args[1], new(weight, length, width, height), CancellationToken.None);
        Console.WriteLine(label.LabelUrl);
    }

    public async Task<ShipmentLabel> Issue(string id, ParcelMeasurements parcel, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ShopProblem(400, "Référence de commande invalide.");
        var reference = id[..15].ToUpperInvariant();
        XElement document;
        var shipment = new Shipment { OrderId = id, Status = "processing" };
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            var order = await ShopAdmin.LockPaidOrder(id, db, ct) ?? throw new ShopProblem(404, "Commande introuvable.");
            var existing = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == id, ct);
            if (existing is { Status: "ready", TrackingNumber: { } tracking, LabelUrl: { } url })
                return new(tracking, url);
            if (existing != null)
                throw new ShopProblem(409, existing.Status == "ready" ? "Un suivi manuel est déjà associé à cette commande."
                    : "Une émission est en cours ou à vérifier dans Mondial Relay Connect. Référence " + reference);
            if (await db.Set<OrderFulfillment>().AnyAsync(s => s.OrderId == id && s.Status == OrderFulfillment.Shipped, ct))
                throw new ShopProblem(409, "Cette commande est déjà marquée comme expédiée.");
            if (!IsConfigured(config))
                throw new ShopProblem(503, "Configure les accès API 2, la collecte et les coordonnées d’expéditeur Mondial Relay.");
            var details = JsonSerializer.Deserialize<ShippingDetails>(order.DeliveryJson)
                ?? throw new ShopProblem(409, "Coordonnées de livraison introuvables.");
            if (parcel.WeightGrams < 1 || parcel.WeightGrams > (details.Locker == null ? 30000 : 25000)
                || parcel.LengthCm is <= 0 || parcel.WidthCm is <= 0 || parcel.HeightCm is <= 0)
                throw new ShopProblem(400, "Vérifie le poids du colis emballé (25 kg maximum en Locker, 30 kg à domicile) et les dimensions renseignées.");
            document = BuildRequest(config, details, order, reference, parcel);
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Once the attempt is recorded, finish it even if the browser closes.
        try
        {
            var host = config.GetValue<bool>("MondialRelay:Sandbox") ? "connect-api-sandbox.mondialrelay.com" : "connect-api.mondialrelay.com";
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://" + host + "/api/shipment");
            req.Headers.Accept.ParseAdd("application/xml");
            req.Content = new StringContent(document.ToString(), Encoding.UTF8, "text/xml");
            using var client = clients.CreateClient("carrier");
            using var response = await client.SendAsync(req);
            response.EnsureSuccessStatusCode();
            using var reader = XmlReader.Create(await response.Content.ReadAsStreamAsync(), new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 2000000
            });
            var label = ReadLabel(XDocument.Load(reader));
            shipment.TrackingNumber = label.TrackingNumber;
            shipment.LabelUrl = label.LabelUrl;
            shipment.Status = "ready";
            await db.SaveChangesAsync();
            return label;
        }
        catch (ShopProblem e) when (e.Status == 422)
        {
            // An explicit carrier rejection without a shipment number can be corrected and retried.
            await db.Shipments.Where(s => s.OrderId == id && s.Status == "processing").ExecuteDeleteAsync();
            throw;
        }
        catch (Exception)
        {
            await db.Shipments.Where(s => s.OrderId == id && s.Status == "processing")
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, "review"));
            throw new ShopProblem(502, "Émission à vérifier dans Mondial Relay Connect avec la référence " + reference + ".");
        }
    }

    private static XElement BuildRequest(IConfiguration config, ShippingDetails details, ShopOrder order, string reference, ParcelMeasurements parcel)
    {
        XNamespace ns = "http://www.example.org/Request";
        XElement E(string name, params object?[] content) => new(ns + name, content);
        XElement? Measure(string name, int? value, string unit) =>
            value.HasValue ? E(name, new XAttribute("Value", value.Value), new XAttribute("Unit", unit)) : null;
        XElement Address(string? name, string? street, string? complement, string? postcode, string? city, string? phone, string? mail) =>
            E("Address", E("Streetname", street), E("CountryCode", "FR"), E("PostCode", postcode), E("City", city),
                E("AddressAdd1", name), E("AddressAdd3", complement ?? ""), E("PhoneNo", Shipping.NormalizePhone(phone)), E("Email", mail));

        var relay = config.GetSection("MondialRelay");
        var sender = relay.GetSection("Expediteur");
        var recipient = details.Recipient;
        return E("ShipmentCreationRequest",
            E("Context", E("Login", relay["Api2Login"]), E("Password", relay["Api2Password"]), E("CustomerId", relay["Api2Client"]),
                E("Culture", "fr-FR"), E("VersionAPI", "1.0")),
            E("OutputOptions", E("OutputFormat", "10x15"), E("OutputType", "PdfUrl")),
            E("ShipmentsList", E("Shipment", E("OrderNo", reference), E("ParcelCount", 1),
                E("ShipmentValue", new XAttribute("Amount", (order.TotalCents - order.ShippingCents) / 100m)),
                E("DeliveryMode", new XAttribute("Mode", details.CarrierMode), new XAttribute("Location", details.Locker is {} l ? "FR-" + l.Id : "")),
                E("CollectionMode", new XAttribute("Mode", relay["Collecte"]!), new XAttribute("Location", relay["LieuCollecte"] ?? "")),
                E("Parcels", E("Parcel", E("Content", "Merchandising BollTrack"), Measure("Length", parcel.LengthCm, "cm"),
                    Measure("Width", parcel.WidthCm, "cm"), Measure("Depth", parcel.HeightCm, "cm"), Measure("Weight", parcel.WeightGrams, "gr"))),
                E("Sender", Address(sender["Nom"], sender["Rue"], sender["Complement"], sender["CodePostal"], sender["Ville"], sender["Telephone"], sender["Email"])),
                E("Recipient", Address(recipient.Name, recipient.Street, recipient.Complement, recipient.Postcode, recipient.City, recipient.Phone, order.Email)))));
    }

    private static ShipmentLabel ReadLabel(XDocument result)
    {
        XNamespace ns = "http://www.example.org/Response";
        if (result.Root?.Name != ns + "ShipmentCreationResponse") throw new ShopProblem(502, "Réponse Mondial Relay inattendue.");
        var record = result.Root.Element(ns + "ShipmentsList")?.Elements(ns + "Shipment").SingleOrDefault();
        var number = (string?)record?.Attribute("ShipmentNumber");
        var raw = (string?)record?.Descendants(ns + "Output").SingleOrDefault();
        var error = result.Descendants(ns + "Status").FirstOrDefault(s => {
            var level = (string?)s.Attribute("Level") ?? "";
            return level.Equals("Error", StringComparison.OrdinalIgnoreCase) || level.StartsWith("Critical", StringComparison.OrdinalIgnoreCase);
        });
        if (string.Equals((string?)error?.Attribute("Level"), "Error", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(number) && !result.Descendants(ns + "Output").Any(o => !string.IsNullOrWhiteSpace(o.Value)))
        {
            var code = (string?)error?.Attribute("Code");
            var suffix = Regex.IsMatch(code ?? "", "^[A-Za-z0-9_-]{1,16}$") ? " (code " + code + ")" : "";
            throw new ShopProblem(422, "Mondial Relay a refusé l’étiquette" + suffix + ". Vérifie les informations du colis et du compte avant de réessayer.");
        }
        if (error != null || !Regex.IsMatch(number ?? "", "^[0-9]{6,20}$") || !Uri.TryCreate(raw, UriKind.Absolute, out var url)
            || (url.Scheme != "https" && url.Scheme != "http") || !url.IsDefaultPort || url.UserInfo.Length != 0
            || !url.Host.EndsWith(".mondialrelay.com", StringComparison.OrdinalIgnoreCase))
            throw new ShopProblem(502, "Étiquette non confirmée.");
        return new(number!, new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri);
    }
}
