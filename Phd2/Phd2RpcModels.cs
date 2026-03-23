using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndreasBehrend.NINA.Phd2Api.Phd2 {

    public class Phd2RpcRequest {
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;
        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Params { get; set; }
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    public class Phd2RpcResponse {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = string.Empty;
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }
        [JsonPropertyName("error")]
        public Phd2RpcError Error { get; set; }
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    public class Phd2RpcError {
        [JsonPropertyName("code")]
        public int Code { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class SettleParams {
        [JsonPropertyName("pixels")]
        public double Pixels { get; set; }
        [JsonPropertyName("time")]
        public double Time { get; set; }
        [JsonPropertyName("timeout")]
        public double Timeout { get; set; }
    }
}
