namespace CodeAtlas.Roslyn.Models;

public sealed record UiEventHandlerInfo(
    string ControlName,
    string EventName,
    string HandlerMethodName,
    string HandlerMethodSymbolId,
    string? DeclaringFile);
