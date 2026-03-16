using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Nodus.Web;
using Nodus.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register HttpClient — base address will be overridden per-request using the
// ?server=... query-string parameter (so the app works against any Admin IP).
builder.Services.AddScoped(sp => new HttpClient());
builder.Services.AddScoped<NodusApiService>();

await builder.Build().RunAsync();
