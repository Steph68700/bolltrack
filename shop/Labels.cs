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
public record LabelPreviewInput(ParcelMeasurements? Parcel, string? LockerId);

public class Labels(ShopDb db, IConfiguration config, IHttpClientFactory clients, ILogger<Labels> logger)
{
    private static readonly string[] RequiredSettings = [
        "Api2Login", "Api2Password", "Api2Client", "Expediteur:Nom", "Expediteur:Rue",
        "Expediteur:CodePostal", "Expediteur:Ville", "Expediteur:Telephone", "Expediteur:Email"
    ];

    public static bool IsConfigured(IConfiguration config) =>
        config["MondialRelay:Collecte"] is "REL" or "CCC"
        && RequiredSettings.All(name => !string.IsNullOrWhiteSpace(config["MondialRelay:" + name]));

    public static bool CanPreview(IConfiguration config)
    {
        var test = config.GetSection("MondialRelay:Test");
        return new[] { "Api2Client", "Api2Login", "Api2Password" }.All(key => !string.IsNullOrWhiteSpace(test[key]))
            && !string.Equals(test["Api2Login"], config["MondialRelay:Api2Login"], StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> Preview(LabelPreviewInput input, CancellationToken ct)
    {
        if (!CanPreview(config)) throw new ShopProblem(503, "Les accès de test Mondial Relay sont indisponibles.");
        if (input.Parcel is not { } parcel) throw new ShopProblem(400, "Renseigne le poids du colis test.");
        ValidateParcel(parcel);
        if (input.LockerId is { } id && !Regex.IsMatch(id, "^[0-9]{6}$"))
            throw new ShopProblem(400, "Identifiant de Locker invalide.");
        var locker = input.LockerId is { } lockerId ? new Locker(lockerId, "", "", "", "") : null;
        var recipient = new Delivery(locker == null ? "domicile" : "locker", "CLIENT TEST", "+33600000000",
            "1 RUE DU TEST", "68000", "COLMAR", input.LockerId);
        var details = new ShippingDetails(recipient, locker, "FR", locker == null ? "HOM" : "24R");
        var reference = "BT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var document = BuildRequest(details, "buyer@example.invalid", 2000, reference, parcel, preview: true);
        var response = await SendRequest(document, sandbox: true, ct);
        return ReadLabel(response, parcel.WeightGrams, details.CarrierMode, reference, preview: true).LabelUrl;
    }

    private static void ValidateParcel(ParcelMeasurements parcel)
    {
        if (parcel.WeightGrams is < 100 or > 25_000)
            throw new ShopProblem(400, "Le poids du colis emballé doit être compris entre 100 et 25 000 g. Pour 0,5 kg, saisis 500.");
        if (parcel.LengthCm is <= 0 || parcel.WidthCm is <= 0 || parcel.HeightCm is <= 0)
            throw new ShopProblem(400, "Les dimensions renseignées doivent être supérieures à zéro, en centimètres.");
    }

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
        ShippingDetails details;
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
            details = JsonSerializer.Deserialize<ShippingDetails>(order.DeliveryJson)
                ?? throw new ShopProblem(409, "Coordonnées de livraison introuvables.");
            ValidateParcel(parcel);
            document = BuildRequest(details, order.Email, order.TotalCents - order.ShippingCents, reference, parcel);
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Once the attempt is recorded, finish it even if the browser closes.
        try
        {
            var response = await SendRequest(document, config.GetValue<bool>("MondialRelay:Sandbox"), CancellationToken.None);
            var label = ReadLabel(response, parcel.WeightGrams, details.CarrierMode, reference);
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

    private async Task<XDocument> SendRequest(XElement document, bool sandbox, CancellationToken ct)
    {
        var host = sandbox ? "connect-api-sandbox.mondialrelay.com" : "connect-api.mondialrelay.com";
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://" + host + "/api/shipment");
        req.Headers.Accept.ParseAdd("application/xml");
        req.Content = new StringContent(document.ToString(), Encoding.UTF8, "text/xml");
        using var client = clients.CreateClient("carrier");
        using var response = await client.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        using var reader = XmlReader.Create(await response.Content.ReadAsStreamAsync(ct), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 2000000
        });
        return XDocument.Load(reader);
    }

    private XElement BuildRequest(ShippingDetails details, string email, int goodsCents, string reference, ParcelMeasurements parcel, bool preview = false)
    {
        XNamespace ns = "http://www.example.org/Request";
        XElement E(string name, params object?[] content) => new(ns + name, content);
        XElement? Measure(string name, int? value, string unit) =>
            value.HasValue ? E(name, new XAttribute("Value", value.Value), new XAttribute("Unit", unit)) : null;
        XElement Address(string? name, string? street, string? complement, string? postcode, string? city, string? phone, string? mail) =>
            E("Address", E("Streetname", street), E("CountryCode", "FR"), E("PostCode", postcode), E("City", city),
                E("AddressAdd1", name), E("AddressAdd3", complement ?? ""), E("PhoneNo", Shipping.NormalizePhone(phone)), E("Email", mail));

        var relay = config.GetSection("MondialRelay");
        var credentials = preview ? relay.GetSection("Test") : relay;
        var sender = relay.GetSection("Expediteur");
        var recipient = details.Recipient;
        var senderAddress = preview
            ? Address("ASSOCIATION TEST", "1 RUE DU TEST", null, "68000", "COLMAR", "+33300000000", "sender@example.invalid")
            : Address(sender["Nom"], sender["Rue"], sender["Complement"], sender["CodePostal"], sender["Ville"], sender["Telephone"], sender["Email"]);
        return E("ShipmentCreationRequest",
            E("Context", E("Login", credentials["Api2Login"]), E("Password", credentials["Api2Password"]), E("CustomerId", credentials["Api2Client"]),
                E("Culture", "fr-FR"), E("VersionAPI", "1.0")),
            E("OutputOptions", E("OutputFormat", "10x15"), E("OutputType", "PdfUrl")),
            E("ShipmentsList", E("Shipment", E("OrderNo", reference), E("ParcelCount", 1),
                E("ShipmentValue", new XAttribute("Amount", goodsCents / 100m), new XAttribute("Currency", "EUR")),
                E("DeliveryMode", new XAttribute("Mode", details.CarrierMode), new XAttribute("Location", details.Locker is {} l ? "FR-" + l.Id : "")),
                E("CollectionMode", new XAttribute("Mode", preview ? "REL" : relay["Collecte"]!), new XAttribute("Location", preview ? "" : relay["LieuCollecte"] ?? "")),
                E("Parcels", E("Parcel", E("Content", "Merchandising BollTrack"), Measure("Length", parcel.LengthCm, "cm"),
                    Measure("Width", parcel.WidthCm, "cm"), Measure("Depth", parcel.HeightCm, "cm"), Measure("Weight", parcel.WeightGrams, "gr"))),
                E("Sender", senderAddress),
                E("Recipient", Address(recipient.Name, recipient.Street, recipient.Complement, recipient.Postcode, recipient.City, recipient.Phone, email)))));
    }

    private ShipmentLabel ReadLabel(XDocument result, int weightGrams, string deliveryMode, string reference, bool preview = false)
    {
        XNamespace ns = "http://www.example.org/Response";
        if (result.Root?.Name != ns + "ShipmentCreationResponse") throw new ShopProblem(502, "Réponse Mondial Relay inattendue.");
        var record = result.Root.Element(ns + "ShipmentsList")?.Elements(ns + "Shipment").SingleOrDefault();
        var number = (string?)record?.Attribute("ShipmentNumber");
        var raw = (string?)record?.Descendants(ns + "Output").SingleOrDefault();
        var statuses = result.Descendants(ns + "Status").ToList();
        if (statuses.Any(s => ((string?)s.Attribute("Level"))?.StartsWith("Critical", StringComparison.OrdinalIgnoreCase) == true))
            throw new ShopProblem(502, "Étiquette non confirmée.");
        var errors = statuses.Where(s => string.Equals((string?)s.Attribute("Level"), "Error", StringComparison.OrdinalIgnoreCase)).ToList();
        if (errors.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(number) || result.Descendants(ns + "Output").Any(o => !string.IsNullOrWhiteSpace(o.Value)))
                throw new ShopProblem(502, "Étiquette non confirmée.");
            var reasons = errors.Select(s => {
                var code = (string?)s.Attribute("Code") ?? "";
                return (Code: Regex.IsMatch(code, @"^[0-9]{1,5}$") ? code : "", Message: CarrierMessage((string?)s.Attribute("Message")));
            }).Distinct().ToList();
            logger.LogWarning("Refus Mondial Relay {Codes}, commande {Reference}, {WeightGrams} g, mode {DeliveryMode}",
                string.Join(", ", reasons.Select(r => r.Code).Distinct()), reference, weightGrams, deliveryMode);
            var lines = reasons.Take(8).Select(r => string.Join(" · ", new[] { r.Code, r.Message }.Where(v => !string.IsNullOrEmpty(v))))
                .Where(line => line.Length > 0);
            var route = deliveryMode == "24R" ? "Locker" : "domicile";
            throw new ShopProblem(422, string.Join(" ", new[] {
                $"Mondial Relay a refusé l’étiquette ({weightGrams} g, {route}).", string.Join(" / ", lines),
                $"Référence marchand {reference}."
            }.Where(text => text.Length > 0)));
        }
        if ((!preview && !Regex.IsMatch(number ?? "", "^[0-9]{6,20}$")) || !Uri.TryCreate(raw, UriKind.Absolute, out var url)
            || (url.Scheme != "https" && url.Scheme != "http") || !url.IsDefaultPort || url.UserInfo.Length != 0
            || !url.Host.EndsWith(".mondialrelay.com", StringComparison.OrdinalIgnoreCase)
            || (preview && !url.Host.Equals("connect-sandbox.mondialrelay.com", StringComparison.OrdinalIgnoreCase)))
            throw new ShopProblem(502, "Étiquette non confirmée.");
        return new(number ?? "", new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri);
    }

    private string? CarrierMessage(string? message)
    {
        message = message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > 300 || message.Any(char.IsControl) || !message.Contains(' ')
            || Regex.IsMatch(message, @"[<>{}@]|https?://|\d{10,}", RegexOptions.IgnoreCase))
            return null;
        foreach (var key in new[] { "Api2Password", "Api2Login", "Api2Client", "ClePrivee", "Test:Api2Password", "Test:Api2Client" })
        {
            var value = config["MondialRelay:" + key];
            if (!string.IsNullOrEmpty(value) && message.Contains(value, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return message;
    }
}
