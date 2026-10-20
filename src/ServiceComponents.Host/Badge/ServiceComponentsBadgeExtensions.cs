using System;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceComponents.AspNet.Wireup;

namespace ServiceComponents.Host.Badge
{
    public static class ServiceComponentsBadgeExtensions
    {
        /// <summary>
        /// Serves SVG status badges at <c>{BadgeOptions.Path}/{name}</c>, and all badges as HTML at <c>{BadgeOptions.Path}</c>.
        /// The path is read from the <c>ServiceComponents:BadgeOptions</c> section. Needs <c>AddEndpoints</c>.
        /// </summary>
        public static ServiceComponentsHostBuilder AddBadges(this ServiceComponentsHostBuilder builder, Action<IBadgeRegistry> registrate)
        {
            // AddHealthChecks is idempotent; the badge service needs HealthCheckService even without AddHealthCheck.
            builder.RegisterCallback((IConfiguration _, IServiceCollection services) => services.AddHealthChecks());
            builder.RegisterCallback((HostBuilderContext _, ContainerBuilder container) => container.AddBadgeService(registrate ?? (_ => { })));
            builder.RegisterCallback((IMvcBuilder mvc) => mvc.AddApplicationPart(typeof(BadgeController).Assembly));
            builder.RegisterCallback((IConfiguration configuration, IEndpointRouteBuilder endpoints) => endpoints.MapBadges(BadgeOptions(configuration).Path));
            return builder;
        }

        /// <summary>
        /// Maps the badge controller route (<c>{path}/{name?}</c>).
        /// </summary>
        public static IEndpointRouteBuilder MapBadges(this IEndpointRouteBuilder endpoints, string path = ".badge")
        {
            endpoints.MapControllerRoute(
                name: "badge",
                pattern: $"{(path ?? ".badge").Trim('/')}/{{name?}}",
                defaults: new { controller = "Badge", action = nameof(BadgeController.Get) });
            return endpoints;
        }

        private static BadgeOptions BadgeOptions(IConfiguration configuration)
        {
            var options = new BadgeOptions();
            configuration.GetSection(Badge.BadgeOptions.ConfigurationSectionName).Bind(options);
            return options;
        }
    }
}
