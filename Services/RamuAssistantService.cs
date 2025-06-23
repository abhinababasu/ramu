using System.Net.Http;
using System.IO;
using ramu.Services;

namespace ramu.Services;

public class RamuAssistantService
{
    private readonly RamuBackEnd _backend;

    public RamuAssistantService(HttpClient? httpClient = null)
    {
        _backend = new RamuBackEnd(httpClient);
    }

    // Entry point for future Azure Function
    public async Task<(string? transcription, string? aiResponse, string? error)> ProcessAudioAsync(
        Stream audioStream,
        string speechApiKey,
        string openAIApiKey,
        string languageCode,
        string ttsVoice,
        string assistantName)
    {
        try
        {
            var transcription = await _backend.TranscribeAudioAsync(audioStream, speechApiKey, languageCode);
            if (string.IsNullOrEmpty(transcription))
                return (null, null, "No transcription result.");

            var aiResponse = await _backend.GetAIChatResponseAsync(transcription, openAIApiKey);
            if (string.IsNullOrEmpty(aiResponse))
                return (transcription, null, "No response from Azure OpenAI.");

            return (transcription, aiResponse, null);
        }
        catch (Exception ex)
        {
            return (null, null, ex.Message);
        }
    }

    public Task<string?> TranscribeAudioAsync(Stream audioStream, string apiKey, string languageCode)
        => _backend.TranscribeAudioAsync(audioStream, apiKey, languageCode);

    public Task<string?> GetAzureOpenAIChatResponseAsync(string prompt, string apiKey)
        => _backend.GetAIChatResponseAsync(prompt, apiKey);

    public Task<Stream> GetTtsAudioStreamAsync(string text, string apiKey, string languageCode, string ttsVoice)
        => _backend.GetTtsAudioStreamAsync(text, apiKey, languageCode, ttsVoice);
}
