using System.Linq;
using System.Reflection;

var nodeAssembly = Assembly.Load("Aspire.Hosting.NodeJs");

Console.WriteLine($"Assembly: {nodeAssembly.FullName}");

foreach (var type in nodeAssembly.GetTypes().Where(t => t.IsSealed && t.IsAbstract))
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        if (!method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
        {
            continue;
        }

        if (
            method.Name.Contains("Npm", StringComparison.OrdinalIgnoreCase)
            || method.Name.Contains("Node", StringComparison.OrdinalIgnoreCase)
        )
        {
            var parameters = string.Join(
                ", ",
                method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")
            );
            Console.WriteLine(
                $"{type.FullName}.{method.Name}({parameters}) -> {method.ReturnType.Name}"
            );
        }
    }
}

var appHostAssembly = Assembly.Load("Aspire.Hosting.AppHost");
Console.WriteLine("Endpoint-related extensions:");
foreach (var type in appHostAssembly.GetTypes().Where(t => t.IsSealed && t.IsAbstract))
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        if (!method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
        {
            continue;
        }

        if (method.Name.Contains("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            var parameters = string.Join(
                ", ",
                method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")
            );
            Console.WriteLine(
                $" - {type.FullName}.{method.Name}({parameters}) -> {method.ReturnType.Name}"
            );
        }
    }
}
