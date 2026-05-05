using GraphSearchtools.SampleSiteCms13.Extensions;
using EPiServer.Cms.UI.AspNetIdentity;
using EPiServer.Data;
using EPiServer.DependencyInjection;
using EPiServer.Scheduler;
using EPiServer.Web.Routing;
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

        services.AddGraphSearchtools()
            .AddSearchProfile("alloy-search", p => p
                .DisplayName("Alloy site search")
                .Description("Header search across the Alloy demo content.")
                .Locales("en")
                .SearchedFields("Name", "MetaDescription", "MainBody")
                .UsesPinnedKey("alloy-{locale}")
                .SemanticBlend(0.3, GraphRanking.Semantic)
                .GraphQLDocument("Queries/AlloySearch.graphql"))
            .AddSearchProfile("alloy-products", p => p
                .DisplayName("Product cards")
                .Description("Pinned recommendations for the product/teaser surface.")
                .Locales("en")
                .SearchedFields("Name", "TeaserText")
                .UsesPinnedKey("alloy-products-{locale}"));

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
        });
    }
}
