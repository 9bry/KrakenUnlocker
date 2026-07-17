using System;
using System.Collections.Generic;
using System.Text.Json;

class Program {
    static void Main() {
        var userToken = "userToken123";
        var deviceToken = "deviceToken456";
        object props = new Dictionary<string, object> { { "SandboxId", "RETAIL" }, { "UserTokens", new[] { userToken } }, { "DeviceToken", deviceToken } };
        var body = JsonSerializer.Serialize(new Dictionary<string, object> { { "Properties", props }, { "RelyingParty", "RP" }, { "TokenType", "JWT" } });
        Console.WriteLine(body);
        
        var pop = new Dictionary<string, object> { { "kty", "EC" } };
        var body2 = JsonSerializer.Serialize(new Dictionary<string, object> {
            { "Properties", new Dictionary<string, object> { { "AuthMethod", "RPS" }, { "ProofKey", pop } } }
        });
        Console.WriteLine(body2);
    }
}
