using System.Linq;
using System.Reflection;

var appHostAssembly = Assembly.Load("Aspire.Hosting.AppHost");

var dashboardExtensions = appHostAssembly.GetTypes()
    .Where(t => t.IsSealed && t.IsAbstract && t.Name.Contains("Dashboard", StringComparison.OrdinalIgnoreCase));

foreach (var type in dashboardExtensions)
{
    Console.WriteLine(type.FullName);
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        if (!method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
        {
            continue;
        }

        var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  {method.Name}({parameters}) -> {method.ReturnType.Name}");
    }
}
