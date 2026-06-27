using OpenAI.Chat;

namespace RagMini;

/// <summary>Bağlam + soruyu LLM'e gönderip cevap metnini döndüren ince sarmalayıcı.</summary>
public sealed class ChatService
{
    private readonly ChatClient _client;
    public ChatService(ChatClient client) => _client = client;

    public async Task<string> AnswerAsync(string systemPrompt, string userPrompt)
    {
        ChatCompletion completion = await _client.CompleteChatAsync(
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt));
        return completion.Content[0].Text;
    }
}
