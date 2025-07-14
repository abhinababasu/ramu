using Plugin.Maui.Audio;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Maui.Controls;
namespace ramu;

public partial class MainPage : ContentPage
{
	private IAudioManager _audioManager;
	private IAudioRecorder _recorder;
	private bool _isRecording = false;
	private bool _isSpeakerEnabled = true;
	private IAudioPlayer? _ttsPlayer; // Track current TTS playback

	// Dictionary to hold language setup information
	// Key: Language name, Value: Tuple of (SpeechCode, AssistantName, TtsVoice)
	private readonly Dictionary<string, (string SpeechCode, string AssistantName, string TtsVoice)> _languageSetup = new()
	{
		{ "English", ("en-US", "Ramu", "en-US-GuyNeural") },
		{ "Hindi", ("hi-IN", "रामु", "hi-IN-MadhurNeural") },
		{ "Bengali", ("bn-IN", "রামু ", "bn-IN-TanishaaNeural") },
		{ "Spanish", ("es-ES", "Ramu", "es-ES-AlvaroNeural") },
		{ "Tamil", ("ta-IN", "ராமு", "ta-IN-ValluvarNeural") }
	};

	// Constants for Azure OpenAI configuration
	// These should match your Azure OpenAI deployment settings 
	private const string endpoint = "https://ramu-openai.openai.azure.com/";
    private const string deploymentName = "gpt-35-turbo";
    private const string apiVersion = "2024-02-15-preview";
	private readonly string? apiKey = Environment.GetEnvironmentVariable("AzOpenAIKey");


	private FormattedString _resultFormattedString = new FormattedString();
	private readonly HttpClient _httpClient = new();

	public MainPage()
	{
		InitializeComponent();
		_audioManager = AudioManager.Current;
		_recorder = _audioManager.CreateRecorder();

		// Initialize language picker
		languagePicker.ItemsSource = _languageSetup.Keys.ToList();
		languagePicker.SelectedIndex = 0; // Default to first language (English)
#pragma warning disable CS8622
		languagePicker.SelectedIndexChanged += LanguagePicker_SelectedIndexChanged;
#pragma warning restore CS8622
		speakerToggleButton.AutomationId = "SpeakerOn"; // Set only once
		// Set initial speaker icon
		UpdateSpeakerToggleButton();
	}

	private string SelectedLanguageCode =>
		languagePicker.SelectedIndex >= 0 && languagePicker.SelectedIndex < _languageSetup.Count
			? _languageSetup[languagePicker.Items[languagePicker.SelectedIndex]].SpeechCode
			: "en-US";

	private string SelectedAssistantName =>
		languagePicker.SelectedIndex >= 0 && languagePicker.SelectedIndex < _languageSetup.Count
			? _languageSetup[languagePicker.Items[languagePicker.SelectedIndex]].AssistantName
			: "Ramu";

	private string SelectedVoice =>
		languagePicker.SelectedIndex >= 0 && languagePicker.SelectedIndex < _languageSetup.Count
			? _languageSetup[languagePicker.Items[languagePicker.SelectedIndex]].TtsVoice
			: "en-US-GuyNeural";

	private async void OnRecordAudioClicked(object sender, EventArgs e)
	{
		// Stop any ongoing TTS playback before recording
		if (_ttsPlayer != null)
		{
			try { _ttsPlayer.Stop(); } catch { }
			try { _ttsPlayer.Dispose(); } catch { }
			_ttsPlayer = null;
		}
		if (!_isRecording)
		{
			await _recorder.StartAsync();
			_isRecording = true;
			recordAudioButton.Text = "Stop Listening";
		}
		else
		{
			recordAudioButton.IsEnabled = false; // Disable button to prevent multiple clicks
			recordAudioButton.Text = "Thinking...";

			// Stop recording and show text
			var transcription = await TranscribeAudioAsync();
			if (!string.IsNullOrEmpty(transcription))
			{
				_resultFormattedString.Spans.Add(new Span { Text = transcription + Environment.NewLine, FontAttributes = FontAttributes.Bold });
			}
			else
			{
				_resultFormattedString.Spans.Add(new Span { Text = "No transcription result." + Environment.NewLine, FontAttributes = FontAttributes.Italic });
			}

			// Send transcription to Azure OpenAI and display response
			if (!string.IsNullOrEmpty(transcription))
			{
				var aiResponse = await GetAzureOpenAIChatResponseAsync(transcription);
				if (!string.IsNullOrEmpty(aiResponse))
				{
					_resultFormattedString.Spans.Add(new Span { Text = aiResponse + Environment.NewLine + Environment.NewLine });
					if (_isSpeakerEnabled)
					{
						await SpeakTextAsync(aiResponse, SelectedLanguageCode);
					}
				}
				else
				{
					_resultFormattedString.Spans.Add(new Span { Text = "No response from Azure OpenAI." + Environment.NewLine + Environment.NewLine });
				}
			}

			recordAudioButton.Text = $"Ask {SelectedAssistantName} 🎤";
			recordAudioButton.IsEnabled = true;				
			_isRecording = false;

			resultText.FormattedText = _resultFormattedString;
			// This is a bug fix, 
			// This is a known issue in .NET MAUI: await resultScrollView.ScrollToAsync(resultText, ScrollToPosition.End, true); can hang or never return if the layout hasn't 
			// completed or if the content size hasn't updated yet. This is especially true right after updating the
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				await Task.Delay(50); // Give layout a moment to update
				await resultScrollView.ScrollToAsync(resultText, ScrollToPosition.End, true);
			});
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
			// Clear and set headers for this request
			_httpClient.DefaultRequestHeaders.Clear();
			_httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

			// Use selected language
			var languageCode = SelectedLanguageCode;
			var requestUri = $"https://westus3.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language={languageCode}";
			using var content = new StreamContent(audioStream);
			content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

			// Send request
			var response = await _httpClient.PostAsync(requestUri, content);
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
           
			if (string.IsNullOrEmpty(apiKey))
				throw new InvalidOperationException("AzOpenAIKey environment variable not set.");

			// Clear and set headers for this request
			_httpClient.DefaultRequestHeaders.Clear();
			_httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };
			var options = new
			{
				temperature = 0.85,
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

			var url = $"{endpoint}openai/deployments/{deploymentName}/chat/completions?api-version={apiVersion}";
            var response = await _httpClient.PostAsync(url, content);
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

	private async Task SpeakTextAsync(string text, string languageCode)
	{
		try
		{
			var apiKey = Environment.GetEnvironmentVariable("AzSpeechKey");
			if (string.IsNullOrEmpty(apiKey))
				throw new InvalidOperationException("AzSpeechKey environment variable not set.");
			var region = "westus3"; // Change if your region is different
			var endpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";

			// Clear and set headers for this request
			_httpClient.DefaultRequestHeaders.Clear();
			_httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
			_httpClient.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", "audio-16khz-32kbitrate-mono-mp3");
			_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ramu-maui-app/1.0");

			var voice = SelectedVoice;

			var ssml = $@"<speak version='1.0' xml:lang='{languageCode}'>
				<voice name='{voice}'>{System.Security.SecurityElement.Escape(text)}</voice>
			</speak>";

			using var content = new StringContent(ssml);
			content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/ssml+xml");

			var response = await _httpClient.PostAsync(endpoint, content);
			response.EnsureSuccessStatusCode();
			var audioStream = await response.Content.ReadAsStreamAsync();

			// Stop any previous TTS playback
			if (_ttsPlayer != null)
			{
				try { _ttsPlayer.Stop(); } catch { }
				try { _ttsPlayer.Dispose(); } catch { }
			}
			_ttsPlayer = AudioManager.Current.CreatePlayer(audioStream);
			_ttsPlayer.Play();
		}
		catch (Exception ex)
		{
			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				var page = this.Window?.Page;
				if (page != null)
				{
					await page.DisplayAlert("Error", $"Text-to-Speech failed: {ex.Message}", "OK");
				}
			});
		}
	}

#pragma warning disable CS8622
	private void LanguagePicker_SelectedIndexChanged(object sender, EventArgs e)
	{
		var selectedLanguage = languagePicker.SelectedIndex >= 0 && languagePicker.SelectedIndex < _languageSetup.Count
			? languagePicker.Items[languagePicker.SelectedIndex]
			: "English";
		recordAudioButton.Text = $"Ask {SelectedAssistantName} 🎤";
		this.Window.Title = $"Ask {SelectedAssistantName}";
	}
#pragma warning restore CS8622

	private void UpdateSpeakerToggleButton()
	{
		speakerToggleButton.Text = _isSpeakerEnabled ? "🔈" : "🔇";
		// Do not set AutomationId here (can only be set once)
	}

	private void OnSpeakerToggleButtonClicked(object sender, EventArgs e)
	{
		_isSpeakerEnabled = !_isSpeakerEnabled;
		UpdateSpeakerToggleButton();
	}
}
