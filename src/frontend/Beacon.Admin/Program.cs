using Beacon.Admin.Components;
using Beacon.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<AdminAuthState>();
builder.Services.AddScoped<IncidentAlertsClient>();

// No .AddHttpMessageHandler<T>() here: IHttpClientFactory builds message handlers in its own
// pooled internal scope, separate from the circuit's DI scope, so a handler that depends on the
// Scoped AdminAuthState would silently capture a different instance than the one Login.razor
// calls SetSession on and never see the token. BeaconApiClient attaches the header itself instead.
builder.Services
    .AddHttpClient<BeaconApiClient>(client =>
    {
        var baseUrl = builder.Configuration["BeaconApi:BaseUrl"] ?? "http://localhost:5252";
        client.BaseAddress = new Uri(baseUrl);
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
