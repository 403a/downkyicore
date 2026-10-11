using Newtonsoft.Json;

namespace DownKyi.Core.Aria2cNet.Client.Entity
{
    [JsonObject]
    public class AriaShutdown
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("jsonrpc")]
        public string Jsonrpc { get; set; } = string.Empty;

        [JsonProperty("result")]
        public string? Result { get; set; }

        [JsonProperty("error")]
        public AriaError? Error { get; set; }

        [JsonIgnore]
        public bool IsSuccess => string.Equals(Result, "OK", StringComparison.Ordinal);

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
