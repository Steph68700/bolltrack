using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using BollTrack;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Shipping = BollTrack.Shipping;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

var labelCommand = args.FirstOrDefault() == "etiquette";
var builder = WebApplication.CreateBuilder(labelCommand ? Array.Empty<string>() : args);
var config=builder.Configuration;
var origin=new Uri(config["Shop:Origin"] ?? "https://bolltrack-production.up.railway.app");
var authority=new Uri(config["Entreli:Authority"] ?? "https://studioidentity-prod.up.railway.app/");
var connection=ShopDb.Connection(config);
builder.WebHost.UseUrls("http://" + (builder.Environment.IsDevelopment() ? "127.0.0.1" : "0.0.0.0") + ":" + (config["PORT"] ?? "8080"));
builder.Services.AddDbContext<ShopDb>(o => o.UseMySql(connection,new MySqlServerVersion(new Version(8,0,0))));
builder.Services.AddDataProtection().SetApplicationName("BollTrack.Boutique").PersistKeysToDbContext<ShopDb>();
builder.Services.AddHttpClient("carrier", c=>c.Timeout=TimeSpan.FromSeconds(10));
builder.Services.AddScoped<Shipping>();
if (!string.IsNullOrWhiteSpace(config["Stripe:SecretKey"]))
    builder.Services.AddSingleton(new SessionService(new StripeClient(config["Stripe:SecretKey"])));
builder.Services.AddScoped<Payments>();
builder.Services.AddHostedService<PaymentRecovery>();
builder.Services.AddResponseCompression(o=>o.EnableForHttps=true);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",LogLevel.Warning);
builder.Services.AddRateLimiter(options=>{
    options.RejectionStatusCode = 429;
    options.AddPolicy("carrier",ctx=>RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? "anonymous",_=>new FixedWindowRateLimiterOptions{
            PermitLimit=25,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o=>{
        o.Cookie.Name="__Host-bolltrack"; o.Cookie.Path="/"; o.Cookie.HttpOnly=true;
        o.Cookie.SecurePolicy=CookieSecurePolicy.Always; o.Cookie.SameSite=SameSiteMode.Lax;
        o.ExpireTimeSpan=TimeSpan.FromHours(12); o.SlidingExpiration=false;
        o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};
        o.Events.OnRedirectToAccessDenied=c=>{c.Response.StatusCode=403;return Task.CompletedTask;};
    })
    .AddOpenIdConnect("Entreli",o=>{
        o.Authority=authority.AbsoluteUri.TrimEnd('/'); o.ClientId=config["Entreli:ClientId"]??"bolltrack-boutique-web";
        o.CallbackPath="/compte/retour"; o.ResponseType="code"; o.ResponseMode="query";
        o.UsePkce=true; o.SaveTokens=false; o.MapInboundClaims=false;
        o.Scope.Clear(); foreach(var s in new[]{"openid","profile","email"}) o.Scope.Add(s);
        o.TokenValidationParameters.NameClaimType="name";
        o.Events.OnRedirectToIdentityProvider=c=>{c.ProtocolMessage.RedirectUri=new Uri(StoreOrigin(c.Request),"/compte/retour").AbsoluteUri;return Task.CompletedTask;};
        o.Events.OnRemoteFailure=c=>{c.HandleResponse();c.Response.Redirect("/boutique.html?connexion=interrompue#compte");return Task.CompletedTask;};
    });
builder.Services.AddAuthorization(options => options.AddPolicy(ShopAdmin.Policy, policy =>
    policy.RequireAuthenticatedUser().RequireAssertion(ctx => ShopAdmin.IsAdmin(ctx.User, config))));
await ShopDb.Initialize(connection);
var app=builder.Build();
if (labelCommand)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
    var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("carrier");
    try { await Labels.Create(args, db, config, client); }
    catch (ShopProblem e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
    return;
}
// Railway terminates TLS. Only the configured public origin is used to build OIDC/Stripe redirects.
app.Use(async(ctx,next)=>{
    var path=ctx.Request.Path;
    var commerce=path.StartsWithSegments("/api") || path.StartsWithSegments("/compte") || path=="/boutique.html" || path=="/boutique-admin.html";
    var currentOrigin = StoreOrigin(ctx.Request);
    if (commerce && !app.Environment.IsDevelopment() && !ctx.Request.Host.Host.Equals(currentOrigin.Host,StringComparison.OrdinalIgnoreCase))
    {
        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
            ctx.Response.Redirect(new Uri(origin, path+ctx.Request.QueryString).AbsoluteUri);
        else ctx.Response.StatusCode=421;
        return;
    }
    if (commerce) ctx.Request.Scheme=currentOrigin.Scheme;
    ctx.Response.Headers.XContentTypeOptions="nosniff";
    ctx.Response.Headers.XFrameOptions="SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"]=commerce ? "no-referrer":"strict-origin-when-cross-origin";
    if(path.StartsWithSegments("/api") || path.StartsWithSegments("/compte"))
        ctx.Response.Headers.CacheControl="no-store";
    if (HttpMethods.IsPost(ctx.Request.Method) && path.StartsWithSegments("/api") && path!="/api/stripe/webhook"
        && (ctx.Request.Headers.Origin!=currentOrigin.GetLeftPart(UriPartial.Authority) || ctx.Request.Headers["X-BollTrack"]!="1"))
    {ctx.Response.StatusCode=403;return;}
    try {await next();}
    catch(ShopProblem e) {ctx.Response.StatusCode=e.Status;await ctx.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(Exception e) when (path.StartsWithSegments("/api") && !ctx.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError("Échec de la requête boutique ({Type})",e.GetType().Name);
        ctx.Response.StatusCode=503;
        await ctx.Response.WriteAsJsonAsync(new{error="Le service est temporairement indisponible. Réessaie dans un instant."});
    }
});
app.UseResponseCompression(); app.UseDefaultFiles(); app.UseStaticFiles();
app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
app.MapGet("/health", async (ShopDb db, CancellationToken ct)=>
    await db.Database.CanConnectAsync(ct) ? Results.Text("ok") : Results.StatusCode(503));
foreach(var path in new[]{"/boutique","/magasin"}) app.MapGet(path,()=>Results.Redirect("/boutique.html"));
app.MapGet("/api/catalogue",async (HttpContext ctx,ShopDb db,Shipping shipping,Payments pay,CancellationToken ct)=>{
    var requestingOrigin=ctx.Request.Headers.Origin.ToString();
    if(requestingOrigin is "https://www.bolltrack.fr" or "https://bolltrack.fr" or "https://steph68700.github.io")
    {
        ctx.Response.Headers.AccessControlAllowOrigin=requestingOrigin;
        ctx.Response.Headers.Append("Vary","Origin");
    }
    return Results.Ok(new { products = await Catalog.List(db, false, ct), methods = shipping.Methods(), checkoutEnabled = pay.Enabled });
});
app.MapGet("/api/catalogue/{id}/photo", Catalog.Photo);
Uri StoreOrigin(HttpRequest request)
{
    // Both registered OIDC callbacks remain usable for sessions opened before the DNS change.
    return request.Host.Host.ToLowerInvariant() switch {
        "www.bolltrack.fr" => new Uri("https://www.bolltrack.fr"),
        "bolltrack-production.up.railway.app" => new Uri("https://bolltrack-production.up.railway.app"),
        _ => origin
    };
}
app.MapGet("/compte/connexion",async (HttpContext ctx)=>await Login(ctx,false));
app.MapGet("/compte/inscription",async (HttpContext ctx)=>await Login(ctx,true));
async Task Login(HttpContext ctx,bool register)
{
    var destination = ctx.Request.Query["retour"].ToString() switch {
        "retractation" => "/boutique.html#retractation", "panier" => "/boutique.html#panier", "admin" => "/boutique-admin.html", _ => "/boutique.html#compte"
    };
    await ctx.ChallengeAsync("Entreli",new AuthenticationProperties{RedirectUri=destination});
    if(register && Uri.TryCreate(ctx.Response.Headers.Location,UriKind.Absolute,out var auth)
        && auth.Host==authority.Host)
        ctx.Response.Headers.Location=QueryHelpers.AddQueryString(new Uri(authority,"/Account/Register").AbsoluteUri,"returnUrl",auth.PathAndQuery);
}
app.MapCustomerAccount();
app.MapWithdrawals();
app.MapShopAdmin();
app.MapPost("/api/deconnexion",async(HttpContext ctx)=>{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);return Results.NoContent();
});
app.MapGet("/api/livraison/lockers",async(string cp,Shipping shipping,CancellationToken ct)=>
    Results.Ok(await shipping.Search(cp,null,ct))).RequireAuthorization().RequireRateLimiting("carrier");
app.MapPost("/api/commandes",async (Checkout request,HttpContext ctx,Payments pay,CancellationToken ct)=>{
    var owner=ctx.User.FindFirstValue("sub"); var email=ctx.User.FindFirstValue("email");
    if(string.IsNullOrWhiteSpace(owner)||string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
    return Results.Ok(new{url=await pay.Create(owner,email,request,StoreOrigin(ctx.Request).GetLeftPart(UriPartial.Authority),ct)});
}).RequireAuthorization();
app.MapPost("/api/commandes/{id}/{action}",async(string id,string action,HttpContext ctx,ShopDb db,Payments pay,CancellationToken ct)=>{
    var owner=ctx.User.FindFirstValue("sub");
    var order=await db.Orders.AsNoTracking().SingleOrDefaultAsync(o=>o.Id==id && o.Owner==owner,ct);
    if(order is null) return Results.NotFound();
    if(action=="annuler") await pay.Cancel(order,ct);
    else if(action=="actualiser") await pay.Refresh(order,ct);
    else return Results.NotFound();
    return Results.NoContent();
}).RequireAuthorization();
app.MapPost("/api/stripe/webhook", async (HttpContext ctx,Payments pay,CancellationToken ct)=>{
    var secret=config["Stripe:WebhookSecret"];
    if(string.IsNullOrWhiteSpace(secret)) return Results.StatusCode(503);
    if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        limit.MaxRequestBodySize=1024*1024;
    var payload=await new StreamReader(ctx.Request.Body).ReadToEndAsync(ct);
    Event evt;
    try {evt=EventUtility.ConstructEvent(payload,ctx.Request.Headers["Stripe-Signature"],secret,throwOnApiVersionMismatch:false);}
    catch(StripeException){return Results.BadRequest();}
    if(evt.Type is "checkout.session.completed" or "checkout.session.expired" && evt.Data.Object is Session session)
        await pay.Apply(session,ct);
    else if (evt.Type == "charge.refunded" && evt.Data.Object is Charge charge)
        await pay.RefreshRefund(charge.PaymentIntentId,ct,charge.Metadata?.GetValueOrDefault("bolltrack_order"));
    else if (evt.Type is "refund.updated" or "refund.failed" && evt.Data.Object is Refund refund)
        await pay.RefreshRefund(refund.PaymentIntentId,ct);
    return Results.Ok();
});
app.Run();
