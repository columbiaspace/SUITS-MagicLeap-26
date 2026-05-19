using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Vosk;

public class VoskSpeechToText : MonoBehaviour
{
	private const string DefaultAiaModelPath = "vosk-model-en-us-0.22-lgraph.zip";

	[Tooltip("Location of the model, relative to the Streaming Assets folder.")]
	public string ModelPath = DefaultAiaModelPath;

	[Tooltip("The source of the microphone input.")]

	public VoiceProcessor VoiceProcessor;
	[Tooltip("The Max number of alternatives that will be processed.")]
	public int MaxAlternatives = 3;

	[Tooltip("How long should we record before restarting?")]
	public float MaxRecordLength = 5;

	[Tooltip("Should the recognizer start when the application is launched?")]
	public bool AutoStart = false;

	[Tooltip("Only emit transcription text when recording is explicitly stopped.")]
	public bool EmitResultsOnlyOnStop = true;

	[Tooltip("Automatically stop recording after Vosk detects a pause in speech.")]
	public bool AutoStopRecordingOnSilence = false;

	[Tooltip("Seconds of silence to wait before automatically stopping recording.")]
	public float SilenceTimeoutSeconds = 2f;

	[Tooltip("The phrases that will be detected. If left empty, all words will be detected.")]
	public List<string> KeyPhrases = new List<string>();

	//Cached version of the Vosk Model.
	private Model _model;

	//Cached version of the Vosk recognizer.
	private VoskRecognizer _recognizer;

	//Conditional flag to see if a recognizer has already been created.
	//TODO: Allow for runtime changes to the recognizer.
	private bool _recognizerReady;

	//Holds all of the audio data until the user stops talking.
	private readonly List<short> _buffer = new List<short>();

	//Called when the the state of the controller changes.
	public Action<string> OnStatusUpdated;

	//Called after the user is done speaking and vosk processes the audio.
	public Action<string> OnTranscriptionResult;

	//Called while the user is speaking. Partial results are display-only.
	public Action<string> OnPartialTranscriptionResult;

	//The absolute path to the decompressed model folder.
	private string _decompressedModelPath;

	//A string that contains the keywords in Json Array format
	private string _grammar = "";

	//Flag that is used to wait for the model file to decompress successfully.
	private bool _isDecompressing;

	//Flag that is used to wait for the the script to start successfully.
	private bool _isInitializing;

	//Flag that is used to check if Vosk was started.
	private bool _didInit;

	//Threading Logic

	// Flag to signal we are ending
	private bool _running;
	private readonly object _recognizerLock = new object();

	//Thread safe queue of microphone data.
	private readonly ConcurrentQueue<short[]> _threadedBufferQueue = new ConcurrentQueue<short[]>();

	//Thread safe queue of resuts
	private readonly ConcurrentQueue<string> _threadedResultQueue = new ConcurrentQueue<string>();
	private readonly ConcurrentQueue<string> _threadedPartialResultQueue = new ConcurrentQueue<string>();
	private string _lastPartialResult = "";



	static readonly ProfilerMarker voskRecognizerCreateMarker = new ProfilerMarker("VoskRecognizer.Create");
	static readonly ProfilerMarker voskRecognizerReadMarker = new ProfilerMarker("VoskRecognizer.AcceptWaveform");

	//If Auto start is enabled, starts vosk speech to text.
	void Start()
	{
		if (AutoStart)
		{
			StartVoskStt();
		}
	}

	/// <summary>
	/// Start Vosk Speech to text
	/// </summary>
	/// <param name="keyPhrases">A list of keywords/phrases. Keywords need to exist in the models dictionary, so some words like "webview" are better detected as two more common words "web view".</param>
	/// <param name="modelPath">The path to the model folder relative to StreamingAssets. If the path has a .zip ending, it will be decompressed into the application data persistent folder.</param>
	/// <param name="startMicrophone">"Should the microphone after vosk initializes?</param>
	/// <param name="maxAlternatives">The maximum number of alternative phrases detected</param>
	public void StartVoskStt(List<string> keyPhrases = null, string modelPath = default, bool startMicrophone = false, int maxAlternatives = 3)
	{
		if (_isInitializing)
		{
			Debug.LogError("Initializing in progress!");
			return;
		}
		if (_didInit)
		{
			Debug.LogError("Vosk has already been initialized!");
			return;
		}

		if (!string.IsNullOrEmpty(modelPath))
		{
			ModelPath = modelPath;
		}

		if (keyPhrases != null)
		{
			KeyPhrases = keyPhrases;
		}

		MaxAlternatives = maxAlternatives;
		StartCoroutine(DoStartVoskStt(startMicrophone));
	}

	//Decompress model, load settings, start Vosk and optionally start the microphone
	private IEnumerator DoStartVoskStt(bool startMicrophone)
	{
		_isInitializing = true;
		yield return WaitForMicrophoneInput();

		yield return Decompress();

		OnStatusUpdated?.Invoke("Loading Model from: " + _decompressedModelPath);
		//Vosk.Vosk.SetLogLevel(0);
		try
		{
			_model = new Model(_decompressedModelPath);
		}
		catch (Exception exception)
		{
			Debug.LogError($"[Vosk] Failed to load model from '{_decompressedModelPath}': {exception}");
			OnStatusUpdated?.Invoke("Vosk failed to load model. Check model path and native library.");
			_isInitializing = false;
			yield break;
		}

		yield return null;

		VoiceProcessor.OnFrameCaptured += VoiceProcessorOnOnFrameCaptured;
		VoiceProcessor.OnRecordingStop += VoiceProcessorOnOnRecordingStop;
		if (!InitializeRecognizer())
		{
			OnStatusUpdated?.Invoke("Vosk failed to initialize recognizer.");
			_isInitializing = false;
			yield break;
		}

		if (startMicrophone)
		{
			_running = true;
			ConfigureVoiceProcessorSilenceDetection();
			VoiceProcessor.StartRecording(autoDetect: AutoStopRecordingOnSilence);
			if (!VoiceProcessor.IsRecording)
			{
				Debug.LogError("[Vosk] Failed to start microphone recording.");
				OnStatusUpdated?.Invoke("Vosk could not start microphone recording.");
				_running = false;
				_isInitializing = false;
				yield break;
			}

			Task.Run(ThreadedWork).ConfigureAwait(false);
		}

		_isInitializing = false;
		_didInit = true;
		OnStatusUpdated?.Invoke("Initialized");
	}

	//Translates the KeyPhraseses into a json array and appends the `[unk]` keyword at the end to tell vosk to filter other phrases.
	private void UpdateGrammar()
	{
		if (KeyPhrases.Count == 0)
		{
			_grammar = "";
			return;
		}

		JSONArray keywords = new JSONArray();
		foreach (string keyphrase in KeyPhrases)
		{
			keywords.Add(new JSONString(keyphrase.ToLower()));
		}

		keywords.Add(new JSONString("[unk]"));

		_grammar = keywords.ToString();
	}

	//Decompress the model zip file or return the location of the decompressed files.
	private IEnumerator Decompress()
	{
		Debug.Log($"[Vosk] ModelPath: {ModelPath}");

		if (!IsZipModelPath(ModelPath))
		{
			_decompressedModelPath = Path.Combine(Application.streamingAssetsPath, ModelPath);
			OnStatusUpdated?.Invoke("Using StreamingAssets model folder.");
			Debug.Log(_decompressedModelPath);
			yield break;
		}

		string extractedModelPath = Path.Combine(Application.persistentDataPath, Path.GetFileNameWithoutExtension(ModelPath));
		if (IsModelDirectoryComplete(extractedModelPath))
		{
			OnStatusUpdated?.Invoke("Using existing decompressed model.");
			_decompressedModelPath = extractedModelPath;
			Debug.Log(_decompressedModelPath);

			yield break;
		}

		if (Directory.Exists(extractedModelPath))
		{
			Debug.LogWarning($"[Vosk] Removing incomplete decompressed model at '{extractedModelPath}'.");
			Directory.Delete(extractedModelPath, true);
		}

		OnStatusUpdated?.Invoke("Decompressing model...");
		string dataPath = Path.Combine(Application.streamingAssetsPath, ModelPath);

		Stream dataStream;
		// Read data from the streaming assets path. You cannot access the streaming assets directly on Android.
		if (dataPath.Contains("://"))
		{
			UnityWebRequest www = UnityWebRequest.Get(dataPath);
			www.SendWebRequest();
			while (!www.isDone)
			{
				yield return null;
			}

			if (www.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError($"[Vosk] Failed to read model zip at '{dataPath}': {www.error}");
				OnStatusUpdated?.Invoke("Vosk failed to read model zip.");
				yield break;
			}

			dataStream = new MemoryStream(www.downloadHandler.data);
		}
		// Read the file directly on valid platforms.
		else
		{
			dataStream = File.OpenRead(dataPath);
		}

		//Read the Zip File
		var zipFile = ZipFile.Read(dataStream);

		//Listen for the zip file to complete extraction
		zipFile.ExtractProgress += ZipFileOnExtractProgress;

		//Update status text
		OnStatusUpdated?.Invoke("Reading Zip file");
		_isDecompressing = false;

		//Start Extraction
		zipFile.ExtractAll(Application.persistentDataPath);

		//Wait until it's complete
		while (_isDecompressing == false)
		{
			yield return null;
		}
		//Override path given in ZipFileOnExtractProgress to prevent crash
		_decompressedModelPath = extractedModelPath;

		//Update status text
		OnStatusUpdated?.Invoke("Decompressing complete!");
		//Wait a second in case we need to initialize another object.
		yield return new WaitForSeconds(1);
		//Dispose the zipfile reader.
		zipFile.Dispose();
		dataStream.Dispose();
	}

	///The function that is called when the zip file extraction process is updated.
	private void ZipFileOnExtractProgress(object sender, ExtractProgressEventArgs e)
	{
		if (e.EventType == ZipProgressEventType.Extracting_AfterExtractAll)
		{
			_isDecompressing = true;
			_decompressedModelPath = e.ExtractLocation;
		}
	}

	//Wait until microphones are initialized
	private IEnumerator WaitForMicrophoneInput()
	{
		float timeoutAt = Time.realtimeSinceStartup + 3f;
		while (Microphone.devices.Length <= 0 && Time.realtimeSinceStartup < timeoutAt)
		{
			yield return null;
		}

		if (Microphone.devices.Length <= 0)
		{
			Debug.LogWarning("[Vosk] No named microphone devices reported. Continuing with Unity default microphone.");
		}

		VoiceProcessor.UpdateDevices();
	}

	private static bool IsZipModelPath(string modelPath)
	{
		return !string.IsNullOrWhiteSpace(modelPath)
			&& modelPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsModelDirectoryComplete(string modelPath)
	{
		return Directory.Exists(modelPath)
			&& File.Exists(Path.Combine(modelPath, "am", "final.mdl"))
			&& File.Exists(Path.Combine(modelPath, "conf", "model.conf"))
			&& File.Exists(Path.Combine(modelPath, "graph", "HCLr.fst"));
	}

	private bool InitializeRecognizer()
	{
		if (_recognizerReady)
		{
			return true;
		}

		try
		{
			UpdateGrammar();

			if (string.IsNullOrEmpty(_grammar))
			{
				_recognizer = new VoskRecognizer(_model, 16000.0f);
			}
			else
			{
				_recognizer = new VoskRecognizer(_model, 16000.0f, _grammar);
			}

			_recognizer.SetMaxAlternatives(MaxAlternatives);
			_recognizerReady = true;
			Debug.Log("[Vosk] Recognizer ready");
			return true;
		}
		catch (Exception exception)
		{
			Debug.LogError($"[Vosk] Failed to initialize recognizer: {exception}");
			return false;
		}
	}

	//Can be called from a script or a GUI button to start detection.
	public void ToggleRecording()
	{
		Debug.Log("Toogle Recording");
		if (!VoiceProcessor.IsRecording)
		{
			Debug.Log("Start Recording");
			if (!InitializeRecognizer())
			{
				OnStatusUpdated?.Invoke("Vosk failed to initialize recognizer.");
				return;
			}

			_running = true;
			_lastPartialResult = "";
			ConfigureVoiceProcessorSilenceDetection();
			VoiceProcessor.StartRecording(autoDetect: AutoStopRecordingOnSilence);
			Task.Run(ThreadedWork).ConfigureAwait(false);
		}
		else
		{
			Debug.Log("Stop Recording");
			_running = false;
			VoiceProcessor.StopRecording();
		}
	}

	//Calls the On Phrase Recognized event on the Unity Thread
	void Update()
	{
		if (_threadedResultQueue.TryDequeue(out string voiceResult))
		{
			OnTranscriptionResult?.Invoke(voiceResult);
		}

		if (_threadedPartialResultQueue.TryDequeue(out string partialResult))
		{
			OnPartialTranscriptionResult?.Invoke(partialResult);
		}
	}

	//Callback from the voice processor when new audio is detected
	private void VoiceProcessorOnOnFrameCaptured(short[] samples)
	{	
                _threadedBufferQueue.Enqueue(samples);
	}

	private void ConfigureVoiceProcessorSilenceDetection()
	{
		VoiceProcessor.SilenceTimer = SilenceTimeoutSeconds;
		VoiceProcessor.StopRecordingAfterSilence = AutoStopRecordingOnSilence;
	}

	//Callback from the voice processor when recording stops
	private void VoiceProcessorOnOnRecordingStop()
	{
		Debug.Log("Stopped");
		_running = false;
		EmitFinalResult();
	}

	//Feeds the autio logic into the vosk recorgnizer
	private async Task ThreadedWork()
	{
		voskRecognizerReadMarker.Begin();

		try
		{
			while (_running)
			{
				if (_threadedBufferQueue.TryDequeue(out short[] voiceResult))
				{
					bool hasResult;
					string result = null;
					lock (_recognizerLock)
					{
						hasResult = _recognizer.AcceptWaveform(voiceResult, voiceResult.Length);
						if (hasResult && !EmitResultsOnlyOnStop)
						{
							result = _recognizer.Result();
						}
						else if (!hasResult)
						{
							result = _recognizer.PartialResult();
						}
					}

					if (hasResult && !string.IsNullOrWhiteSpace(result))
					{
						_threadedResultQueue.Enqueue(result);
					}
					else if (!hasResult && !string.IsNullOrWhiteSpace(result) && result != _lastPartialResult)
					{
						_lastPartialResult = result;
						_threadedPartialResultQueue.Enqueue(result);
					}
				}
				else
				{
					// Wait for some data
					await Task.Delay(100);
				}
			}
		}
		catch (Exception exception)
		{
			Debug.LogError($"[Vosk] Recognition worker failed: {exception}");
		}
		finally
		{
			voskRecognizerReadMarker.End();
		}
	}

	private void EmitFinalResult()
	{
		if (!_recognizerReady || _recognizer == null)
		{
			Debug.LogWarning("[Vosk] Cannot emit final result because recognizer is not ready.");
			return;
		}

		try
		{
			string finalResult;
			lock (_recognizerLock)
			{
				finalResult = _recognizer.FinalResult();
				_recognizer.Dispose();
				_recognizer = null;
				_recognizerReady = false;
				_lastPartialResult = "";
			}

			Debug.Log($"[Vosk] Final result: {finalResult}");
			_threadedResultQueue.Enqueue(finalResult);
		}
		catch (Exception exception)
		{
			Debug.LogError($"[Vosk] Failed to emit final result: {exception}");
			OnStatusUpdated?.Invoke("Vosk failed to process recording.");
		}
	}



}
