using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Services;
using GV23_Notice.Services.Notices;
using GV23_Notice.Services.Notices.DearJohnny;
using GV23_Notice.Services.Notices.Invalidity;
using GV23_Notice.Services.Notices.Section49;
using GV23_Notice.Services.Notices.Section51;
using GV23_Notice.Services.Notices.Section52;
using GV23_Notice.Services.Notices.Section53;
using GV23_Notice.Services.Notices.Section78;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.Rolls;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// DB
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// --------------------------- Step 1 (Settings) ---------------------------
builder.Services.AddScoped<INoticeSettingsService, NoticeSettingsService>();

// --------------------------- Storage ---------------------------
builder.Services.AddScoped<IStorageRootResolver, StorageRootResolver>();
builder.Services.AddScoped<IWorkflowAssetStorage, WorkflowAssetStorage>();

// --------------------------- Batch Name Generation ---------------------------
builder.Services.AddScoped<IBatchNameService, BatchNameService>();

// --------------------------- Roll DB Connection Factory ---------------------------
builder.Services.AddScoped<IRollDbConnectionFactory, RollDbConnectionFactory>();

// --------------------------- Holiday / Appeal Close Date ---------------------------
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddScoped<IS53AppealCloseDateCalculator, S53AppealCloseDateCalculator>();

// --------------------------- Preview ---------------------------
builder.Services.AddScoped<IDummyPreviewDataFactory, DummyPreviewDataFactory>();
builder.Services.AddScoped<INoticePreviewService, NoticePreviewService>();

// --------------------------- PDF Builders / Services (REQUIRED) ---------------------------
// NOTE: these must match the interfaces used in NoticePreviewService ctor

builder.Services.AddScoped<ISection49PdfBuilder, Section49PdfBuilder>();
builder.Services.AddScoped<ISection51PdfBuilder, Section51PdfBuilder>();
builder.Services.AddScoped<ISection52PdfService, Section52PdfService>();
builder.Services.AddScoped<ISection53PdfService, Section53PdfService>();
builder.Services.AddScoped<IDearJonnyPdfService, DearJonnyPdfService>();
builder.Services.AddScoped<IInvalidNoticePdfService, InvalidNoticePdfService>();
builder.Services.AddScoped<ISection78PdfBuilder, Section78PdfBuilder>();

// Options (if you use them)
builder.Services.Configure<Section53PdfOptions>(builder.Configuration.GetSection("Section53Pdf"));

// --------------------------- Email Templates ---------------------------
builder.Services.Configure<EmailTemplateOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<GV23_Notice.Services.Email.INoticeEmailTemplateService,
                          GV23_Notice.Services.Email.NoticeEmailTemplateService>();

//----------------------Notify Email Service----------------------
builder.Services.AddScoped<GV23_Notice.Services.Email.INoticeEmailArchiveService,
                          GV23_Notice.Services.Email.NoticeEmailArchiveService>();

builder.Services.AddScoped<GV23_Notice.Services.Email.IWorkflowApprovalEmailService,
                          GV23_Notice.Services.Email.WorkflowApprovalEmailService>();

//-------------------------Step3-------------------------------------------------------
builder.Services.AddScoped<IS49RollRepository, S49RollRepository>();



// --------------------------- Auth ---------------------------
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

var app = builder.Build();

// Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();   // ✅ IMPORTANT (missing in your file)
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
