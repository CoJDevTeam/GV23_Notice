using GV23_Notice.Data;
using GV23_Notice.Domain.Email;
using GV23_Notice.Domain.Storage;
using GV23_Notice.Models.Security;
using GV23_Notice.Services;
using GV23_Notice.Services.Audit;
using GV23_Notice.Services.Corrections;
using GV23_Notice.Services.Email;
using GV23_Notice.Services.Notices;
using GV23_Notice.Services.Notices.DearJohnny;
using GV23_Notice.Services.Notices.Invalidity;
using GV23_Notice.Services.Notices.Section49;
using GV23_Notice.Services.Notices.Section51;
using GV23_Notice.Services.Notices.Section52;
using GV23_Notice.Services.Notices.Section53;
using GV23_Notice.Services.Notices.Section78;
using GV23_Notice.Services.Preview;
using GV23_Notice.Services.Preview.GV23_Notice.Services.Notices;
using GV23_Notice.Services.QA;
using GV23_Notice.Services.RevisedMVD;
using GV23_Notice.Services.Rolls;
using GV23_Notice.Services.Search;
using GV23_Notice.Services.Security;
using GV23_Notice.Services.SnapShotStep2;
using GV23_Notice.Services.Stats;
using GV23_Notice.Services.Step3;
using GV23_Notice.Services.Storage;
using GV23_Notice.Services.ThirdPartyApplications;
using GV23_Notice.Services.Workflow;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;

// QuestPDF license — Community
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// Configuration objects from appsettings
// ----------------------------------------------------
var accessControlSection = builder.Configuration
    .GetSection("AccessControl")
    .Get<AccessControlOptions>() ?? new AccessControlOptions();

// ----------------------------------------------------
// Database
// ----------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ----------------------------------------------------
// Options
// ----------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<UserManagementAuthOptions>(builder.Configuration.GetSection("UserManagementAuth"));
builder.Services.Configure<AccessControlOptions>(builder.Configuration.GetSection("AccessControl"));
builder.Services.Configure<Section53PdfOptions>(builder.Configuration.GetSection("Section53Pdf"));
builder.Services.Configure<EmailTemplateOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// ----------------------------------------------------
// MVC / Razor
// ----------------------------------------------------
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// ----------------------------------------------------
// Step 1 (Settings)
// ----------------------------------------------------
builder.Services.AddScoped<INoticeSettingsService, NoticeSettingsService>();

// ----------------------------------------------------
// Storage
// ----------------------------------------------------
builder.Services.AddScoped<IStorageRootResolver, StorageRootResolver>();
builder.Services.AddScoped<IWorkflowAssetStorage, WorkflowAssetStorage>();
builder.Services.AddScoped<INoticePathService, NoticePathService>();
builder.Services.AddScoped<INoticeBatchPrintService, NoticeBatchPrintService>();

// ----------------------------------------------------
// Batch Name Generation
// ----------------------------------------------------
builder.Services.AddScoped<IBatchNameService, BatchNameService>();

// ----------------------------------------------------
// Roll DB Connection Factory
// ----------------------------------------------------
builder.Services.AddScoped<IRollDbConnectionFactory, RollDbConnectionFactory>();
builder.Services.AddScoped<IS49RollRepository, S49RollRepository>();

// ----------------------------------------------------
// Holiday / Appeal Close Date
// ----------------------------------------------------
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddScoped<IS53AppealCloseDateCalculator, S53AppealCloseDateCalculator>();

// ----------------------------------------------------
// Preview / Step 2
// ----------------------------------------------------
builder.Services.AddScoped<INoticePreviewService, NoticePreviewService>();
builder.Services.AddScoped<ITempFileStore, TempFileStore>();
builder.Services.AddScoped<IPreviewDbDataService, PreviewDbDataService>();
builder.Services.AddScoped<INoticeSettingsAuditService, NoticeSettingsAuditService>();
builder.Services.AddScoped<IStep2WorkflowAuditService, Step2WorkflowAuditService>();
builder.Services.AddScoped<INoticeStep2SnapshotService, NoticeStep2SnapshotService>();
builder.Services.AddScoped<INoticeQaService,NoticeQaService>();
// ----------------------------------------------------
// PDF Builders / Notice Services
// ----------------------------------------------------
builder.Services.AddScoped<ISection49PdfBuilder, Section49PdfBuilder>();
builder.Services.AddScoped<ISection51PdfBuilder, Section51PdfBuilder>();
builder.Services.AddScoped<ISection52PdfService, Section52PdfService>();
builder.Services.AddScoped<ISection53PdfService, Section53PdfService>();
builder.Services.AddScoped<IDearJonnyPdfService, DearJonnyPdfService>();
builder.Services.AddScoped<IInvalidNoticePdfService, InvalidNoticePdfService>();
builder.Services.AddScoped<ISection78PdfBuilder, Section78PdfBuilder>();

// Also register concrete Section52PdfService if some services inject the concrete type
builder.Services.AddScoped<Section52PdfService>();
builder.Services.AddScoped<INoticeSourceStatusService,NoticeSourceStatusService>();
builder.Services.AddScoped<IRevisedMvdRepository,RevisedMvdRepository>();

// ----------------------------------------------------
// Search / Audit / Workflow
// ----------------------------------------------------
builder.Services.AddScoped<INoticeSearchService, NoticeSearchService>();
builder.Services.AddScoped<IWorkflowEmailReadService, WorkflowEmailReadService>();
builder.Services.AddScoped<INoticeAuditLogQueryService, NoticeAuditLogQueryService>();
builder.Services.AddScoped<ICorrectionTicketQueryService, CorrectionTicketQueryService>();

// ----------------------------------------------------
// Step 3
// ----------------------------------------------------
builder.Services.AddScoped<IStep3BatchService, Step3BatchService>();
builder.Services.AddScoped<IStep3BatchQueryService, Step3BatchQueryService>();
builder.Services.AddScoped<IStep3Step1Service, Step3Step1Service>();
builder.Services.AddScoped<IStep3WorkflowSelectService, Step3WorkflowSelectService>();
builder.Services.AddScoped<IStep3PrintQueryService,Step3PrintQueryService>();
builder.Services.AddScoped<IS52RangePrintService,S52RangePrintService>();

// ----------------------------------------------------
// Email
// ----------------------------------------------------
builder.Services.AddScoped<INoticeEmailTemplateService,NoticeEmailTemplateService>();
builder.Services.AddScoped<INoticeEmailArchiveService,NoticeEmailArchiveService>();
builder.Services.AddScoped<IWorkflowApprovalEmailService,WorkflowApprovalEmailService>();
builder.Services.AddScoped<INoticeBatchEmailService, NoticeBatchEmailService>();



// ----------------------------------------------------
// Stats / Excel / Email
// ----------------------------------------------------
builder.Services.AddScoped<INoticeStatsEmailService,NoticeStatsEmailService>();
builder.Services.AddScoped<INoticeSendStatsService,NoticeSendStatsService>();
builder.Services.AddScoped<INoticeStatsDashboardService,NoticeStatsDashboardService>();


// ----------------------------------------------------
// Corrections
// ----------------------------------------------------
builder.Services.AddScoped<INoticeCorrectionSourceService, NoticeCorrectionSourceService>();
builder.Services.AddScoped<INoticeCorrectionBatchService, NoticeCorrectionBatchService>();
builder.Services.AddScoped<INoticeCorrectionPrintService, NoticeCorrectionPrintService>();
builder.Services.AddScoped<INoticeCorrectionEmailService, NoticeCorrectionEmailService>();

// ----------------------------------------------------
// THIRD PARTY APPEAL DATE CONFIGURATION
// ----------------------------------------------------
builder.Services.AddScoped<IThirdPartyAppealDateConfigurationService, ThirdPartyAppealDateConfigurationService>();

// ----------------------------------------------------
// Security
// ----------------------------------------------------
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddTransient<IClaimsTransformation, UserClaimsTransformation>();


builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();



builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;

    foreach (var policy in accessControlSection.Policies)
    {
        var roles = policy.Value ?? Array.Empty<string>();

        options.AddPolicy(policy.Key, p =>
        {
            p.RequireAuthenticatedUser();
            p.RequireRole(roles);
        });
    }
});

// ----------------------------------------------------
// Build
// ----------------------------------------------------
var app = builder.Build();

// ----------------------------------------------------
// Pipeline
// ----------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();