using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Extensions;
using EPiServer.Cms.Shell;
using EPiServer.Cms.UI.AspNetIdentity;
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

        // Graph Search Tools
        services.AddGraphSearchtools(options =>
            {
                // Configure options here or in appsettings.json under "CodeArt:GraphSearchtools"
            })
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
