using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BollTrack;

public sealed class ShopProblem(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public record Locker(string Id, string Name, string Street, string Postcode, string City);
public record Delivery(string Mode, string Name, string Phone, string Street, string Postcode, string City, string? LockerId, string? Complement = null);
public record ShippingDetails(Delivery Recipient, Locker? Locker, string Country, string CarrierMode);
public record DeliveryMethod(string Id, int Cents, bool Available);

public class Shipping(IConfiguration config, IHttpClientFactory clients)
{
    private string? Brand => config["MondialRelay:Enseigne"];
    private string? Key => config["MondialRelay:ClePrivee"];
    public bool LockerSearchEnabled => !string.IsNullOrWhiteSpace(Brand) && !string.IsNullOrWhiteSpace(Key);
    public List<DeliveryMethod> Methods()
    {
        var methods = new List<DeliveryMethod>();
        Add("locker", "LockerCents", LockerSearchEnabled);
        Add("domicile", "DomicileCents", config.GetValue<bool>("MondialRelay:DomicileActif"));
        return methods;

        void Add(string id, string setting, bool available)
        {
            if (int.TryParse(config["MondialRelay:" + setting], out var cost) && cost is >= 0 and <= 100000)
                methods.Add(new(id, cost, available));
        }
    }

    // L'adresse du destinataire est requise aussi pour les étiquettes Locker.
    public async Task<ShippingDetails> Validate(Delivery data, CancellationToken ct)
    {
        var phone = Regex.Replace(Clean(data.Phone, 24), @"[\s().-]", "");
        if (phone.StartsWith("0033")) phone = "+" + phone[2..];
        if (phone.StartsWith('0')) phone = "+33" + phone[1..];
        var d = new Delivery(data.Mode, Clean(data.Name, 30), phone,
            Clean(data.Street, 40), Clean(data.Postcode, 5), Clean(data.City, 30), data.LockerId,
            string.IsNullOrWhiteSpace(data.Complement) ? null : Clean(data.Complement, 30));
        ValidatePostcode(d.Postcode);
        if (!Regex.IsMatch(d.Phone, @"^\+33[1-9][0-9]{8}$"))
            throw new ShopProblem(400, "Vérifie le numéro de téléphone pour la livraison.");
        if (d.Mode == "domicile") return new(d with { LockerId = null }, null, "FR", "HOM");
        if (d.Mode != "locker" || !Regex.IsMatch(d.LockerId ?? "", "^[0-9]{6}$"))
            throw new ShopProblem(400, "Sélectionne un Locker.");
        var lockers = await Search(null, d.LockerId, ct);
        var locker = lockers.FirstOrDefault(l => l.Id == d.LockerId)
            ?? throw new ShopProblem(409, "Ce Locker est indisponible. Choisis-en un autre.");
        return new(d, locker, "FR", "24R");
    }

    public async Task<List<Locker>> Search(string? postcode, string? id, CancellationToken ct)
    {
        if (!LockerSearchEnabled) throw new ShopProblem(503, "La recherche de Lockers sera disponible à l’ouverture.");
        if (id is null) ValidatePostcode(postcode);
        XNamespace ns = "http://www.mondialrelay.fr/webservice/";
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        const string operation = "WSI4_PointRelais_Recherche";
        var body = new XElement(ns + operation);
        var values = new (string Name, string Value)[] {
            ("Enseigne",Brand!), ("Pays","FR"), ("NumPointRelais",id ?? ""), ("Ville",""),
            ("CP",postcode ?? ""), ("Latitude",""), ("Longitude",""), ("Taille",""), ("Poids",""),
            ("Action","APM"), ("DelaiEnvoi",""), ("RayonRecherche",""), ("TypeActivite",""),
            ("NACE",""), ("NombreResultats",id is null ? "10" : "1")
        };
        foreach (var entry in values) body.Add(new XElement(ns + entry.Name, entry.Value));
        // Le protocole SOAP de Mondial Relay impose ce calcul.
        var signature = MD5.HashData(Encoding.UTF8.GetBytes(string.Concat(values.Select(x => x.Value)) + Key));
        body.Add(new XElement(ns + "Security", Convert.ToHexString(signature)));
        var envelope = new XElement(soap + "Envelope", new XElement(soap + "Body", body));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mondialrelay.com/Web_Services.asmx");
        request.Content = new StringContent(envelope.ToString(), Encoding.UTF8, "text/xml");
        request.Headers.Add("SOAPAction", "\"" + ns.NamespaceName + operation + "\"");
        using var response = await clients.CreateClient("carrier").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new ShopProblem(503, "Mondial Relay est temporairement indisponible.");
        using var reader = XmlReader.Create(await response.Content.ReadAsStreamAsync(ct), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 1000000
        });
        var result = XDocument.Load(reader).Descendants(ns + (operation + "Result")).FirstOrDefault();
        if (result?.Element(ns + "STAT")?.Value.Trim() != "0")
            throw new ShopProblem(503, "Mondial Relay n’a pas pu confirmer les Lockers.");
        return result.Descendants(ns + "PointRelais_Details")
            .Where(e => V(e,"Pays") == "FR" && V(e,"STAT") is "" or "0")
            .Select(e => new Locker(V(e,"Num"), V(e,"LgAdr1"), string.Join(" ", new[]{V(e,"LgAdr3"),V(e,"LgAdr4")}.Where(x => x.Length > 0)), V(e,"CP"), V(e,"Ville")))
            .ToList();
        string V(XElement e, string key) => e.Element(ns + key)?.Value.Trim() ?? "";
    }
    private static string Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || v.Length > max || v.Any(char.IsControl))
            throw new ShopProblem(400, "Complète les coordonnées de livraison.");
        return v;
    }
    internal static void ValidatePostcode(string? code)
    {
        if (!Regex.IsMatch(code ?? "", @"^(?:0[1-9]|[1-8][0-9]|9[0-5])[0-9]{3}$"))
            throw new ShopProblem(400, "Saisis un code postal de France métropolitaine.");
    }
}
