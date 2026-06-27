using Microsoft.Extensions.AI;

namespace RagMini;

/// <summary>Bağlam + soruyu LLM'e gönderir — MEAI IChatClient üzerinden (sağlayıcı-bağımsız).</summary>
public sealed class ChatService
{
    private readonly IChatClient _client;
    public ChatService(IChatClient client) => _client = client;

    public async Task<string> AnswerAsync(string systemPrompt, string userPrompt)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };
        ChatResponse response = await _client.GetResponseAsync(messages);
        return response.Text;
    }
}
