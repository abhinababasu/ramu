using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ramu.Services;

public class RamuBackEnd
{
    private readonly HttpClient _httpClient;

    public RamuBackEnd(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Transcribe the audio stream to text by sending it to Speech Service.
    /// </summary>
    /// <param name="audioStream">Audio stream</param>
    /// <param name="apiKey">API Key used to authenticate</param>
    /// <param name="languageCode">Language code for the audio</param>
    /// <returns></returns>
    public async Task<string?> TranscribeAudioAsync(Stream audioStream, string apiKey, string languageCode)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
        var requestUri = $"https://westus3.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={languageCode}";
        using var content = new StreamContent(audioStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        var response = await _httpClient.PostAsync(requestUri, content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("DisplayText").GetString();
    }

    /// <summary>
    /// Get a response from AI chat model using the provided prompt.
    /// </summary>
    /// <param name="prompt">User prompt</param>
    /// <param name="apiKey">API Key used to authenticate</param>
    /// <returns></returns>
    public async Task<string?> GetAIChatResponseAsync(string prompt, string apiKey)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        var endpoint = "https://ramu-openai.openai.azure.com/";
        var deploymentName = "gpt-35-turbo";
        var requestBodyWithOptions = new
        {
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.85,
            max_tokens = 800,
            top_p = 0.95,
            frequency_penalty = 0,
            presence_penalty = 0
        };
        var json = JsonSerializer.Serialize(requestBodyWithOptions);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var url = $"{endpoint}openai/deployments/{deploymentName}/chat/completions?api-version=2024-02-15-preview";
        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    }

    /// <summary>
    /// Given a text, get the TTS audio stream using the specified voice and language.
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="apiKey">API Key used to authenticate</param>
    /// <param name="languageCode">Language code</param>
    /// <param name="ttsVoice">Which voice to use</param>
    /// <returns></returns>
    public async Task<Stream> GetTtsAudioStreamAsync(string text, string apiKey, string languageCode, string ttsVoice)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", "audio-16khz-32kbitrate-mono-mp3");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ramu-maui-app/1.0");
        var endpoint = $"https://westus3.tts.speech.microsoft.com/cognitiveservices/v1";
        var ssml = $@"<speak version='1.0' xml:lang='{languageCode}'>\n<voice name='{ttsVoice}'>{System.Security.SecurityElement.Escape(text)}</voice>\n</speak>";
        using var content = new StringContent(ssml);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");
        var response = await _httpClient.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }
}
