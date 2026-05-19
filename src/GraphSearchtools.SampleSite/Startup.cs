using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Extensions;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;
using EPiServer.Cms.Shell;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.DependencyInjection;
using EPiServer.Scheduler;
using EPiServer.ServiceLocation;
using EPiServer.Web.Routing;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite;

public class Startup
{
    private readonly IWebHostEnvironment _webHostingEnvironment;

    public Startup(IWebHostEnvironment webHostingEnvironment)
    {
        _webHostingEnvironment = webHostingEnvironment;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        if (_webHostingEnvironment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(_webHostingEnvironment.ContentRootPath, "App_Data"));

            services.Configure<SchedulerOptions>(options => options.Enabled = false);
        }

        services
            .AddCmsAspNetIdentity<ApplicationUser>()
            .AddCms()
            .AddAlloy()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();

        // Optimizely Graph CMS integration. Reads credentials and gateway from
        // the Optimizely:ContentGraph configuration section by default. With
        // empty creds the package still registers its services so the addon can
        // run against an unconfigured Graph (admin endpoints will surface the
        // missing-creds state instead of failing at startup).
        // ContentGraph depends on the Content Delivery API for serialization,
        // so AddContentDeliveryApi must be called first.
        services.AddContentDeliveryApi(_ => { });
        services.AddContentGraph(_ => { });

        // Graph Search Tools
        services.AddGraphSearchtools(options =>
            {
                // Configure options here or in appsettings.json under "CodeArt:GraphSearchtools"
            })
            // Single source of truth: AlloySearchService.SampleHitsQueryDocument
            // is the same string the runtime executes (modulo dynamic facet
            // / phrase substitution), so the admin Channel detail view always
            // reflects what the storefront actually sends to Optimizely Graph.
            .AddSearchChannel("alloy-search", p => p
                .DisplayName("Alloy site search")
                .Description("Header search across the Alloy demo content.")
                // Derive locales from the CMS's enabled language branches so
                // editors enabling a new language in Admin → Manage Website
                // Languages surface it here without a redeploy. Stock Alloy
                // ships English + Swedish enabled; turning on another branch
                // (e.g. Finnish) appears in the Channel detail's locale
                // picker on next page load.
                .LocalesFromCmsLanguages()
                .SearchedFields("Name", "MetaDescription", "MainBody")
                .UsesPinnedKey("alloy-{locale}")
                .SemanticBlend(0.3, GraphRanking.Semantic)
                .GraphQLDocumentInline(AlloySearchService.SampleHitsQueryDocument));

        // Faceted site-search service used by /search. Each request issues
        // four parallel queries (hits + per-facet count sources + keyword
        // enumeration) so facet counts stay stable across a click — see
        // Services/AlloySearchService.cs and the faceted-search guidelines.
        services.AddHttpClient<AlloySearchService>();

        // Required by Wangkanai.Detection
        services.AddDetection();

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromSeconds(10);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // Required by Wangkanai.Detection
        app.UseDetection();
        app.UseSession();

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseGraphSearchtools();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapContent();
            endpoints.MapGraphSearchtools();
            // Attribute-routed MVC controllers — used by SearchSuggestController
            // for the autocomplete JSON endpoint at /api/search/suggest.
            endpoints.MapControllers();
        });
    }
}
