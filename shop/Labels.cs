using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace BollTrack;

public static class Labels
{
    public static async Task Create(string[] args, ShopDb db, IConfiguration config, HttpClient client)
    {
        if (args.Length != 6 || !Guid.TryParseExact(args[1], "N", out _) ||
            args.Skip(2).Any(x => !int.TryParse(x, out var n) || n <= 0))
            throw new ShopProblem(400, "Usage : dotnet Shop.dll etiquette ID_COMMANDE POIDS_G LONGUEUR_CM LARGEUR_CM HAUTEUR_CM");
        var id = args[1];
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.Status == "paid")
            ?? throw new ShopProblem(404, "Commande payée introuvable.");
        var existing = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == id);
        if (existing?.Status == "ready") { Console.WriteLine(existing.LabelUrl); return; }
        var details = JsonSerializer.Deserialize<ShippingDetails>(order.DeliveryJson)!;
        string Setting(string name) => !string.IsNullOrWhiteSpace(config["MondialRelay:"+name])
            ? config["MondialRelay:"+name]! : throw new ShopProblem(503, "Configuration manquante : MondialRelay__"+name.Replace(":","__"));
        var collection = Setting("Collecte");
        if (collection is not ("REL" or "CCC")) throw new ShopProblem(400,"Mode de collecte attendu : REL ou CCC.");
        XNamespace ns = "http://www.example.org/Request";
        XElement E(string n, params object?[] content) => new(ns + n, content);
        XElement Measure(string name, string value, string unit) => E(name,new XAttribute("Value",value),new XAttribute("Unit",unit));
        XElement Address(string name,string street,string? complement,string postcode,string city,string phone,string email) =>
            E("Address",E("Streetname",street),E("CountryCode","FR"),E("PostCode",postcode),E("City",city),
              E("AddressAdd1",name),E("AddressAdd3",complement ?? ""),E("PhoneNo",phone),E("Email",email));
        var recipient = details.Recipient;
        var document = E("ShipmentCreationRequest",
            E("Context",E("Login",Setting("Api2Login")),E("Password",Setting("Api2Password")),E("CustomerId",Setting("Api2Client")),
              E("Culture","fr-FR"),E("VersionAPI","1.0")),
            E("OutputOptions",E("OutputFormat","10x15"),E("OutputType","PdfUrl")),
            E("ShipmentsList",E("Shipment",E("OrderNo",id[..15].ToUpperInvariant()),E("ParcelCount",1),
                E("DeliveryMode",new XAttribute("Mode",details.CarrierMode),new XAttribute("Location",details.Locker is {} l ? "FR-"+l.Id : "")),
                E("CollectionMode",new XAttribute("Mode",collection),new XAttribute("Location",config["MondialRelay:LieuCollecte"] ?? "")),
                E("Parcels",E("Parcel",E("Content","Merchandising BollTrack"),Measure("Weight",args[2],"gr"),Measure("Length",args[3],"cm"),
                    Measure("Width",args[4],"cm"),Measure("Depth",args[5],"cm"))),
                E("Sender",Address(Setting("Expediteur:Nom"),Setting("Expediteur:Rue"),config["MondialRelay:Expediteur:Complement"],
                    Setting("Expediteur:CodePostal"),Setting("Expediteur:Ville"),Setting("Expediteur:Telephone"),Setting("Expediteur:Email"))),
                E("Recipient",Address(recipient.Name,recipient.Street,recipient.Complement,recipient.Postcode,recipient.City,recipient.Phone,order.Email)))));
        // An interrupted carrier request may already have created a billable label. Keep the claim for reconciliation.
        if (existing != null) throw new ShopProblem(409,"Envoi en cours ou à vérifier dans Mondial Relay Connect. Référence "+id[..15].ToUpperInvariant());
        var shipment=new Shipment{OrderId=id,Status="processing"};
        db.Shipments.Add(shipment);
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateException e) when (e.InnerException is MySqlException { Number:1062 })
        { throw new ShopProblem(409,"Cette expédition a déjà été demandée."); }
        var host=config.GetValue<bool>("MondialRelay:Sandbox") ? "connect-api-sandbox.mondialrelay.com" : "connect-api.mondialrelay.com";
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://"+host+"/api/shipment");
        req.Headers.Accept.ParseAdd("application/xml");
        req.Content=new StringContent(document.ToString(),Encoding.UTF8,"text/xml");
        try
        {
            using var response=await client.SendAsync(req);
            response.EnsureSuccessStatusCode();
            using var reader=XmlReader.Create(await response.Content.ReadAsStreamAsync(),new XmlReaderSettings{
                DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=2000000
            });
            var result=XDocument.Load(reader);
            XNamespace output="http://www.example.org/Response";
            var record=result.Descendants(output+"Shipment").SingleOrDefault();
            var number=record?.Attribute("ShipmentNumber")?.Value;
            var raw=record?.Descendants(output+"Output").SingleOrDefault()?.Value;
            if (!Regex.IsMatch(number ?? "", @"^[0-9]{6,20}$") || !Uri.TryCreate(raw,UriKind.Absolute,out var label) ||
                (label.Scheme!="https" && label.Scheme!="http") || !label.Host.EndsWith(".mondialrelay.com",StringComparison.OrdinalIgnoreCase))
                throw new ShopProblem(502,"Étiquette non confirmée.");
            shipment.TrackingNumber=number;
            shipment.LabelUrl=new UriBuilder(label){Scheme="https",Port=-1}.Uri.AbsoluteUri;
            shipment.Status="ready"; await db.SaveChangesAsync();
            Console.WriteLine(shipment.LabelUrl);
        }
        catch(Exception)
        {
            await db.Shipments.Where(s=>s.OrderId==id && s.Status=="processing")
                .ExecuteUpdateAsync(set=>set.SetProperty(s=>s.Status,"review"));
            throw new ShopProblem(502,"Vérifie la référence "+id[..15].ToUpperInvariant()+" dans Mondial Relay Connect avant toute nouvelle émission.");
        }
    }
}
