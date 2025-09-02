using Azure.Data.Tables;
using Azure.Storage.Files.Shares;
using CLDV6212_GROUP_04.Service;
using ABCRetailers.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add memory caching for better performance
builder.Services.AddMemoryCache();

// Configure Azure Storage Services
var connectionString = builder.Configuration.GetConnectionString("AzureStorageConnection")
    ?? throw new InvalidOperationException("Azure Storage connection string not found");

// Register Azure Storage clients
builder.Services.AddSingleton(new TableServiceClient(connectionString));
builder.Services.AddSingleton(new ShareClient(connectionString, builder.Configuration["AzureFileShare:Name"] ?? "proof-of-payment-files"));

// Register our Azure Storage Service
builder.Services.AddScoped<IAzureStorageService, AzureStorageService>();

// Add logging
builder.Services.AddLogging();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
