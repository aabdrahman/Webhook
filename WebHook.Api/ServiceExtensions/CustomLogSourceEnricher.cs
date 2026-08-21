using Serilog.Core;
using Serilog.Events;

namespace WebHook.Api.ServiceExtensions;

/// <summary>
/// 
/// </summary>
public class CustomLogSourceEnricher : ILogEventEnricher
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="logEvent"></param>
    /// <param name="propertyFactory"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.ContainsKey("ClassName") ||
            logEvent.Properties.ContainsKey("MethodName"))
        {
            string className = GetPropertyValue(logEvent, "ClassName");
            string methodName = GetPropertyValue(logEvent, "MethodName");

            string source = string.IsNullOrEmpty(methodName)
                ? className
                : $"{className}.{methodName}";

            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty("LogSource", source));
        }
        else if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            logEvent.AddOrUpdateProperty(
                propertyFactory.CreateProperty(
                    "LogSource",
                    sourceContext.ToString().Trim('"')));
        }
    }

    private static string GetPropertyValue(
        LogEvent logEvent,
        string propertyName)
    {
        return logEvent.Properties.TryGetValue(propertyName, out var value)
                ? value.ToString().Trim('"')
                : string.Empty;
    }
}
