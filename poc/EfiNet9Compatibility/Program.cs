using Efipay;
using Newtonsoft.Json.Linq;
using System.Reflection;

var sdkType = typeof(EfiPay);
var sdkAssembly = sdkType.Assembly;

Console.WriteLine($"Assembly: {sdkAssembly.GetName().Name}");
Console.WriteLine($"AssemblyVersion: {sdkAssembly.GetName().Version}");
Console.WriteLine($"PublicType: {sdkType.FullName}");

var efiPay = new EfiPay(
    "client_id_ficticio",
    "client_secret_ficticio",
    sandbox: true,
    certificate: null!);
Console.WriteLine($"Instantiation: {efiPay.GetType().FullName}");
Console.WriteLine("Constructors:");

foreach (var constructor in sdkType.GetConstructors())
{
    var parameters = string.Join(
        ", ",
        constructor.GetParameters().Select(parameter =>
            $"{parameter.ParameterType.Name} {parameter.Name}"));
    Console.WriteLine($"- EfiPay({parameters})");
}

Console.WriteLine("Public Pix methods:");

foreach (var method in sdkType
             .GetMethods(BindingFlags.Instance | BindingFlags.Public)
             .Where(method => method.Name.Contains("Pix", StringComparison.OrdinalIgnoreCase))
             .OrderBy(method => method.Name, StringComparer.Ordinal))
{
    var parameters = string.Join(
        ", ",
        method.GetParameters().Select(parameter =>
            $"{parameter.ParameterType.Name} {parameter.Name}"));
    Console.WriteLine($"- {method.ReturnType.Name} {method.Name}({parameters})");
}

var constantsField = sdkType.GetField("constants", BindingFlags.Static | BindingFlags.NonPublic);
var constants = constantsField?.GetValue(null) as JObject;
var pixEndpoints = constants?["APIS"]?["PIX"]?["ENDPOINTS"] as JObject;
var pixMethods = new[]
{
    "PixSend",
    "PixSendDetail",
    "PixSendDetailId",
    "PixSendList",
    "PixConfigWebhook"
};

Console.WriteLine("Configured Pix endpoints:");

foreach (var method in pixMethods)
{
    var endpoint = pixEndpoints?[method] as JObject;
    Console.WriteLine(endpoint is null
        ? $"- {method}: unavailable"
        : $"- {method}: {endpoint["method"]} {endpoint["route"]}");
}
