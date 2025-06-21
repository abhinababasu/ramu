using Plugin.Maui.Audio;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Diagnostics;
namespace ramu;

public partial class MainPage : ContentPage
{
	private IAudioManager _audioManager;
	private IAudioRecorder _recorder;
	private bool _isRecording = false;

	private readonly Dictionary<string, string> _languageCodes = new()
	{
		{ "English", "en-US" },
		{ "Hindi", "hi-IN" },
		{ "Bengali", "bn-IN" },
		{ "Spanish", "es-ES" }
	};

	public MainPage()
	{
		InitializeComponent();
		_audioManager = AudioManager.Current;
		_recorder = _audioManager.CreateRecorder();

		// Initialize language picker
		languagePicker.ItemsSource = _languageCodes.Keys.ToList();
		languagePicker.SelectedIndex = 0; // Default to first language (English)
	}

	private string SelectedLanguageCode =>
		languagePicker.SelectedIndex >= 0 && languagePicker.SelectedIndex < _languageCodes.Count
			? _languageCodes[languagePicker.Items[languagePicker.SelectedIndex]]
			: "en-US";

	private async void OnRecordAudioClicked(object sender, EventArgs e)
	{
		if (!_isRecording)
		{
			await _recorder.StartAsync();
			_isRecording = true;
			recordAudioButton.Text = "Stop Listening";
		}
		else
		{
			recordAudioButton.IsEnabled = false; // Disable button to prevent multiple clicks
			// Change button text to indicate transcription 
			recordAudioButton.Text = "Thinking...";
			// Stop recording and show text
			var transcription = await TranscribeAudioAsync();
			if (!string.IsNullOrEmpty(transcription))
			{
				await DisplayAlert("Transcription", transcription, "OK");
			}
			else
			{
				await DisplayAlert("Transcription", "No transcription result.", "OK");
			}
			_isRecording = false;
			recordAudioButton.IsEnabled = true; // Re-enable button

			// Send transcription to Azure OpenAI and display response
			if (!string.IsNullOrEmpty(transcription))
			{
				var aiResponse = await GetAzureOpenAIChatResponseAsync(transcription);
				if (!string.IsNullOrEmpty(aiResponse))
				{
					await DisplayAlert("AI Response", aiResponse, "OK");
				}
				else
				{
					await DisplayAlert("AI Response", "No response from Azure OpenAI.", "OK");
				}
			}
			
			// Reset button text
			recordAudioButton.Text = "Ask Ramu";
		}
	}

	private async Task<string?> TranscribeAudioAsync()
	{
		try
		{
			// Get the audio stream from the recorder
			var audioSource = await _recorder.StopAsync();
			if (audioSource == null)
				return null;

			// Get the audio stream (adjust if your recorder provides a file path instead)
			using var audioStream = audioSource.GetAudioStream();

			// Prepare HTTP client
			var apiKey = Environment.GetEnvironmentVariable("AzSpeechKey");
			if (string.IsNullOrEmpty(apiKey))
				throw new InvalidOperationException("AzSpeechKey environment variable not set.");

			Debug.WriteLine($"AzSpeechKey: {apiKey}");
			using var client = new HttpClient();
			client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

			// Use selected language
			var languageCode = SelectedLanguageCode;
			var requestUri = $"https://westus3.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={languageCode}";
			using var content = new StreamContent(audioStream);
			content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

			// Send request
			var response = await client.PostAsync(requestUri, content);
			response.EnsureSuccessStatusCode();

			// Parse response
			var json = await response.Content.ReadAsStringAsync();
			using var doc = JsonDocument.Parse(json);
			var text = doc.RootElement.GetProperty("DisplayText").GetString();

			return text;
		}
		catch (Exception ex)
		{
			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				var page = this.Window?.Page;
				if (page != null)
				{
					await page.DisplayAlert("Error", $"Transcription failed: {ex.Message}", "OK");
				}
			});
			return null;
		}
	}

    private async Task<string?> GetAzureOpenAIChatResponseAsync(string prompt)
    {
        try
        {
            var endpoint = "https://ramu-openai.openai.azure.com/";
            var deploymentName = "gpt-35-turbo";
			var apiKey = Environment.GetEnvironmentVariable("AzOpenAIKey");
			if (string.IsNullOrEmpty(apiKey))
				throw new InvalidOperationException("AzOpenAIKey environment variable not set.");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };
			var options = new
			{
				temperature = 0.7,
				max_tokens = 800,
				top_p = 0.95,
				frequency_penalty = 0,
				presence_penalty = 0
			};
			var requestBodyWithOptions = new
			{
				messages = requestBody.messages,
				temperature = options.temperature,
				max_tokens = options.max_tokens,
				top_p = options.top_p,
				frequency_penalty = options.frequency_penalty,
				presence_penalty = options.presence_penalty
			};
			var json = JsonSerializer.Serialize(requestBodyWithOptions);
            
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var url = $"{endpoint}openai/deployments/{deploymentName}/chat/completions?api-version=2024-02-15-preview";
            var response = await client.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var message = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return message;
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = this.Window?.Page;
                if (page != null)
                {
                    await page.DisplayAlert("Error", $"Azure OpenAI request failed: {ex.Message}", "OK");
                }
            });
            return null;
        }
    }
}
