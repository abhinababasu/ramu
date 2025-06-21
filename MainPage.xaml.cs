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
			recordAudioButton.Text = "Stop Recording";
		}
		else
		{
			recordAudioButton.IsEnabled = false; // Disable button to prevent multiple clicks
			recordAudioButton.Text = "Transcribing...";
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
			// Reset button text
			recordAudioButton.Text = "Record Audio";
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
}
