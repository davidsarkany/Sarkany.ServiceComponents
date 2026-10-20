namespace ServiceComponents.Host.Badge
{
    public class BadgeOptions
    {
        public const string ConfigurationSectionName = "ServiceComponents:BadgeOptions";

        public string Path { get; set; } = ".badge";
    }
}
