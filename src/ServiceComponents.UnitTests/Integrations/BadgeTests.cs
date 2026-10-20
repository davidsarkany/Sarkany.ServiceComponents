using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceComponents.AspNet.Wireup;
using ServiceComponents.Host.Badge;
using ServiceComponents.UnitTests.Fixtures;
using Xunit;

namespace ServiceComponents.UnitTests.Integrations
{
    [Collection("prometheus")]
    public class BadgeTests
    {
        private static async Task<(HttpStatusCode status, string contentType, string body)> Get(HttpTestHost host, string path)
        {
            var response = await host.CreateClient().GetAsync(path);
            return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
        }

        private static HttpTestHost BadgeHost(Dictionary<string, string> configuration = null) => new(builder => builder
            .RegisterCallback((IHostBuilder host) => host.ConfigureAppConfiguration(config => config.AddInMemoryCollection(configuration ?? new())))
            .AddHealthCheck((_, checks) => checks.AddCheck("db", () => HealthCheckResult.Unhealthy(), new[] { ServiceComponentsHealthcheckExtensions.ReadinessTag }))
            .AddBadges(badges => {
                badges.RegistrateVersionBadge("1.2.3");
                badges.RegistrateLivenessBadge();
                badges.RegistrateReadinessBadge();
                badges.RegistrateBadge("custom", "Custom", () => Task.FromResult("fine"), status => "#123456");
            }));

        [Fact]
        public async Task Badges_are_served_as_svg()
        {
            await using var host = BadgeHost();

            var version = await Get(host, "/.badge/version");
            Assert.Equal((HttpStatusCode.OK, "image/svg+xml"), (version.status, version.contentType));
            Assert.Contains("1.2.3", version.body);

            Assert.Contains("Healthy", (await Get(host, "/.badge/live")).body);
            // The readiness badge selects checks like the /.well-known/ready endpoint.
            Assert.Contains("Unhealthy", (await Get(host, "/.badge/ready")).body);
            Assert.Contains("#123456", (await Get(host, "/.badge/custom")).body);
            Assert.Contains("Badge not found!", (await Get(host, "/.badge/missing")).body);
        }

        [Fact]
        public async Task All_badges_are_served_as_html()
        {
            await using var host = BadgeHost();

            var (status, contentType, body) = await Get(host, "/.badge");

            Assert.Equal((HttpStatusCode.OK, "text/html"), (status, contentType));
            Assert.Equal(4, body.Split("<div>").Length - 1);
        }

        [Fact]
        public async Task Badge_path_is_configurable()
        {
            await using var host = BadgeHost(new Dictionary<string, string> { [BadgeOptions.ConfigurationSectionName + ":Path"] = "/status/" });

            Assert.Contains("1.2.3", (await Get(host, "/status/version")).body);
            Assert.Equal(HttpStatusCode.NotFound, (await Get(host, "/.badge/version")).status);
            Assert.Equal(".badge", new BadgeOptions().Path);
        }

        [Fact]
        public async Task Version_badge_defaults_to_the_entry_assembly_version()
        {
            await using var host = new HttpTestHost(builder => builder.AddBadges(badges => badges.RegistrateVersionBadge()));

            var (status, _, body) = await Get(host, "/.badge/version");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains("Version", body);
        }
    }
}
