var builder = WebApplication.CreateBuilder(args);

// 1. Core Services Setup
builder.Services.AddRazorPages();

// 2. Bulletproof Browser-Side Secure Cookie Authentication
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

// 3. Native Security Identity Middleware (Perfectly Sequenced)
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();