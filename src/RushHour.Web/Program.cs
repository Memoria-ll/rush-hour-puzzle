using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RushHour.Desktop.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Game>("#app");
await builder.Build().RunAsync();
