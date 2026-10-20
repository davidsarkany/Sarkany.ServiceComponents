using System;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json.Linq;

namespace ServiceComponents.Infrastructure.CouchDB
{
    public class CouchDbConfigurationSource : JsonStreamConfigurationSource
    {
        private readonly ServiceConfigurationOptions _options;

        public CouchDbConfigurationSource(ServiceConfigurationOptions options)
        {
            _options = options;
        }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            using var httpClient = new HttpClient { BaseAddress = _options.Uri };
            var path = $"{_options.Database}/{_options.Document}";
            using var httpResponse = httpClient.GetAsync(path).GetAwaiter().GetResult();
            var response = httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            // An error document (e.g. {"error":"not_found"}) used to be loaded as configuration.
            if (!httpResponse.IsSuccessStatusCode) {
                throw new InvalidOperationException($"Could not load the configuration document {path} from {_options.Uri}: {(int)httpResponse.StatusCode} {response}");
            }

            var json = JObject.Parse(response);
            json.Remove("_id");
            json.Remove("_rev");

            Stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString()));
            return new JsonStreamConfigurationProvider(this);
        }
    }
}
