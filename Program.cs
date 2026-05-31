var builder = WebApplication.CreateBuilder(args);

// 1. ADD COOKIE & SESSION SERVICES (Must be before builder.Build())
builder.Services.AddRazorPages();

builder.Services.AddDistributedMemoryCache(); // Creates an in-memory bucket for sessions
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Gives users 30 minutes of idle time
    options.Cookie.HttpOnly = true;                 // Protects session cookie from XSS scripts
    options.Cookie.IsEssential = true;               // Forces cookie to work even without GDPR consent blocks
});

// Use bulletproof browser-side secure cookie authentication instead of unstable memory sessions
builder.Services.AddAuthentication("FirmAuthCookie")
    .AddCookie("FirmAuthCookie", options =>
    {
        options.LoginPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 2. ENABLE SESSION MIDDLEWARE (CRITICAL: Must sit exactly between UseRouting and UseAuthorization)
app.UseSession(); 

app.UseAuthentication();

app.UseAuthorization();

app.MapRazorPages();

app.Run();