using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RagMini;

/// <summary>Bağlam + soruyu LLM'e gönderir — MEAI IChatClient üzerinden (sağlayıcı-bağımsız).</summary>
public sealed class ChatService
{
    private readonly IChatClient _client;
    public ChatService(IChatClient client) => _client = client;

    /// <summary>Cevabı tek seferde döndürür.</summary>
    public async Task<string> AnswerAsync(string systemPrompt, string userPrompt)
    {
        ChatResponse response = await _client.GetResponseAsync(Build(systemPrompt, userPrompt));
        return response.Text;
    }

    /// <summary>Cevabı parça parça (streaming) akıtır.</summary>
    public async IAsyncEnumerable<string> AnswerStreamingAsync(
        string systemPrompt, string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var update in _client.GetStreamingResponseAsync(Build(systemPrompt, userPrompt), cancellationToken: ct))
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
    }

    private static List<ChatMessage> Build(string system, string user) =>
    [
        new(ChatRole.System, system),
        new(ChatRole.User, user),
    ];
}
