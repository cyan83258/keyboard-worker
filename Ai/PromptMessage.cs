namespace KeyboardWorker;

// One message of a request: who says it (system, user, assistant) and what. A request is a list of these (AiClient.Build).
public sealed record PromptMessage(string Role, string Text)
{
    public const string System = "system", User = "user", Assistant = "assistant";
}
