using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceComponents.AspNet.Wireup;

namespace ServiceComponents.Host.Badge
{
    public class BadgeService : IBadgeRegistry
    {
        private readonly HealthCheckService _healthCheckService;

        private IDictionary<string, BadgeDefinition> _badges { get; } = new Dictionary<string, BadgeDefinition>();

        public BadgeService(HealthCheckService healthCheckService)
        {
            _healthCheckService = healthCheckService;
        }

        public async Task<IActionResult> GetAllBadgeAsync()
        {
            var content = new StringBuilder();

            foreach (var definition in _badges.Values)
            {
                var status = await definition.Status();
                content.Append("<div>");
                content.Append(BadgePainter.Draw(definition.Label, status, definition.Color(status)));
                content.Append("</div>");
            }

            return CreateResult("text/html", content.ToString());
        }

        public async Task<IActionResult> GetBadgeAsync(string name)
        {
            if (!_badges.TryGetValue(name, out var definition))
            {
                return CreateResult("image/svg+xml", BadgePainter.Draw("Unknown", "Badge not found!", BadgePainter.Grey));
            }

            var status = await definition.Status();
            return CreateResult("image/svg+xml", BadgePainter.Draw(definition.Label, status, definition.Color(status)));
        }  

        private ContentResult CreateResult(string contentType, string content)
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status200OK,
                ContentType = contentType,
                Content = content
            };
        }

        public void RegistrateBadge(string key, string label, Func<Task<string>> status, string color)
        {
            RegistrateBadge(key, label, status, (status) => color);
        }

        public void RegistrateBadge(string key, string label, Func<Task<string>> status, Func<string, string> color)
        {
            _badges.Add(key, new BadgeDefinition(label, status, color));
        }

        public void RegistrateVersionBadge(string version = default)
        {
            version ??= Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+', 2)[0] ?? "unknown";
            RegistrateBadge("version", "Version", () => Task.FromResult(version), BadgePainter.Blue);
        }

        public void RegistrateReadinessBadge()
        {
            RegistrateBadge("ready", "Ready", () => HealthStatusOf(ServiceComponentsHealthcheckExtensions.ReadinessTag), HealthColor);
        }

        public void RegistrateLivenessBadge()
        {
            RegistrateBadge("live", "Live", () => HealthStatusOf(ServiceComponentsHealthcheckExtensions.LivenessTag), HealthColor);
        }

        // Same selection as the /.well-known endpoints of AddHealthCheck: untagged checks and checks with the tag.
        private async Task<string> HealthStatusOf(string tag) =>
            (await _healthCheckService.CheckHealthAsync(check => check.Tags == null || check.Tags.Count == 0 || check.Tags.Contains(tag))).Status.ToString();

        private static string HealthColor(string status) => status == nameof(HealthStatus.Healthy) ? BadgePainter.Green : BadgePainter.Red;
    }
}
