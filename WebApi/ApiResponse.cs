using System.Text.Json.Serialization;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    public class ApiResponse {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Message { get; set; }
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Data { get; set; }

        public static ApiResponse Ok(object data = null) => new ApiResponse { Success = true, Data = data };
        public static ApiResponse Fail(string message) => new ApiResponse { Success = false, Message = message };
    }
}
