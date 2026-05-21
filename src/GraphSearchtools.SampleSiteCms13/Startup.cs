using GraphSearchtools.SampleSiteCms13.Extensions;
using GraphSearchtools.SampleSiteCms13.Services;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.Data;
using EPiServer.DependencyInjection;
using EPiServer.Scheduler;
using EPiServer.Web.Routing;
using Optimizely.Graph.Cms.ContentSources.DependencyInjection;
using Optimizely.Graph.DependencyInjection;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;

namespace GraphSearchtools.SampleSiteCms13;

public class Startup(IWebHostEnvironment webHostingEnvironment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        if (webHostingEnvironment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(webHostingEnvironment.ContentRootPath, "App_Data"));

            services.Configure<SchedulerOptions>(options => options.Enabled = false);
        }

        services.Configure<DataAccessOptions>(o => o.UpdateDatabaseCompatibilityLevel = true);

        services
            .AddCmsAspNetIdentity<ApplicationUser>()
            .AddCms()
            .AddAlloy()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();

        // Optimizely Graph CMS integration (CMS 13 SDK). Reads credentials
        // and gateway from the Optimizely:ContentGraph configuration section
        // by default. AddGraphCore wires the gateway client + auth; the
        // ContentSources extension registers the CMS event handlers that
        // sync content into Graph.
        services.AddGraphCore();
        services.AddGraphContentSources();

        services.AddGraphSearchtools()
            // Single source of truth: AlloySearchService.SampleHitsQueryDocument
            // is the same string the runtime executes (modulo dynamic facet /
            // phrase substitution), so the admin Channel detail view always
            // reflects what the storefront actually sends to Optimizely Graph.
            .AddSearchChannel("alloy-search", p => p
                .DisplayName("Alloy site search")
                .Description("Header search across the Alloy demo content.")
                // Derive locales from the CMS's enabled language branches so
                // editors enabling a new language in Admin → Manage Website
                // Languages surface it here without a redeploy.
                .LocalesFromCmsLanguages()
                .SearchedFields("Name", "MetaDescription", "MainBody")
                .UsesPinnedKey("alloy-{locale}")
                .SemanticBlend(0.3, GraphRanking.Semantic)
                .GraphQLDocumentInline(AlloySearchService.SampleHitsQueryDocument));

        // Faceted site-search service used by /search. Each request issues
        // multiple parallel queries (hits + per-facet count sources + keyword
        // enumeration) so facet counts stay stable across a click.
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
