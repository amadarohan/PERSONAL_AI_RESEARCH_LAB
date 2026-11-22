using PersonalResearchLab.Web;
using PersonalResearchLab.Web.Components;
using PersonalResearchLab.Web.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddResearchLab(builder.Configuration, builder.Environment);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Unmatched URLs never reach the Blazor router, so the <NotFound> block in Routes.razor
// cannot render them. Re-execute 404s against the real /not-found page instead of
// returning an empty Kestrel response.
app.UseStatusCodePagesWithReExecute("/not-found");

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<JobHub>("/hubs/jobs");

app.Run();
