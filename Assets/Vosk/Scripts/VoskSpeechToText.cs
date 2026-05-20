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
	private int _recordingSessionId;
	private readonly object _recognizerLock = new object();
	// Task handle for the most recently dispatched final-result worker.
	// The next ThreadedWork awaits this before touching the recognizer
	// so that session N+1 audio cannot be mixed into session N's
	// decoder state (or wiped by its Reset()).
	private Task _lastFinalizeTask;

	//Thread safe queue of microphone data.
	private readonly ConcurrentQueue<short[]> _threadedBufferQueue = new ConcurrentQueue<short[]>();

	//Thread safe queue of resuts
	private readonly ConcurrentQueue<string> _threadedResultQueue = new ConcurrentQueue<string>();
	private readonly ConcurrentQueue<string> _threadedPartialResultQueue = new ConcurrentQueue<string>();
	// Status messages produced by background threads (e.g. the final-decode worker).
	// Unity APIs like Text/UI must be touched from the main thread only, so we
	// marshal these through Update() instead of invoking OnStatusUpdated directly.
	private readonly ConcurrentQueue<string> _threadedStatusQueue = new ConcurrentQueue<string>();
	private string _lastPartialResult = "";
	private string _lastEndpointResult = "";



	static readonly ProfilerMarker voskRecognizerCreateMarker = new ProfilerMarker("VoskRecognizer.Create");
	static readonly ProfilerMarker voskRecognizerReadMarker = new ProfilerMarker("VoskRecognizer.AcceptWaveform");

	// libvosk.so ships only as an Android x86_64 plugin (see
	// Assets/Vosk/ThirdParty/Vosk/Plugins/Androidx86_64/libvosk.so.meta —
	// Exclude Editor/OSXUniversal/Win/Linux are all 1). Calling any Vosk
	// PInvoke on the Mac Editor or a non-Android build throws
	// DllNotFoundException, so every entry point that ultimately reaches
	// libvosk gates on this.
	public static bool IsVoskNativeAvailable => Application.platform == RuntimePlatform.Android;

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

		if (!IsVoskNativeAvailable)
		{
			// libvosk is Android-only. On the Mac Editor (where we do most
			// of our authoring) any PInvoke into libvosk would crash with a
			// DllNotFoundException, so bail out cleanly instead.
			Debug.LogWarning("[Vosk] Native library is only available on Android (Magic Leap 2). Skipping initialization on this platform.");
			OnStatusUpdated?.Invoke("Vosk not available on this platform.");
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

		// new Model(...) is a blocking PInvoke that mmaps the acoustic
		// model and the HCLr.fst graph. On the Snapdragon SoC in the ML2
		// this can take hundreds of milliseconds; running it on the Unity
		// main thread stalls the compositor long enough to cause a black
		// frame / flicker. Load it on a worker thread instead and just
		// pump frames here until it completes.
		Model loadedModel = null;
		Exception modelLoadException = null;
		string modelPathSnapshot = _decompressedModelPath;
		var modelLoadTask = Task.Run(() =>
		{
			try
			{
				loadedModel = new Model(modelPathSnapshot);
			}
			catch (Exception exception)
			{
				modelLoadException = exception;
			}
		});

		while (!modelLoadTask.IsCompleted)
		{
			yield return null;
		}

		if (modelLoadException != null || loadedModel == null)
		{
			Debug.LogError($"[Vosk] Failed to load model from '{modelPathSnapshot}': {modelLoadException}");
			OnStatusUpdated?.Invoke("Vosk failed to load model. Check model path and native library.");
			_isInitializing = false;
			yield break;
		}

		_model = loadedModel;

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
			_lastPartialResult = "";
			_lastEndpointResult = "";
			ClearQueuedRecognitionData();
			int sessionId = Interlocked.Increment(ref _recordingSessionId);
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

			Task.Run(() => ThreadedWork(sessionId)).ConfigureAwait(false);
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
		if (!IsVoskNativeAvailable)
		{
			// Quietly no-op in the Editor / non-Android players. The AIA
			// controller logs a clearer "Vosk not available" message at its
			// own layer; we just want to make sure we never hit a PInvoke.
			return;
		}

		Debug.Log("Toogle Recording");
		if (!VoiceProcessor.IsRecording)
		{
			Debug.Log("Start Recording");
			if (!InitializeRecognizer())
			{
				OnStatusUpdated?.Invoke("Vosk failed to initialize recognizer.");
				return;
			}

			_lastPartialResult = "";
			_lastEndpointResult = "";
			ClearQueuedRecognitionData();
			int sessionId = Interlocked.Increment(ref _recordingSessionId);
			_running = true;
			ConfigureVoiceProcessorSilenceDetection();
			VoiceProcessor.StartRecording(autoDetect: AutoStopRecordingOnSilence);
			if (!VoiceProcessor.IsRecording)
			{
				Debug.LogError("[Vosk] Failed to start microphone recording.");
				OnStatusUpdated?.Invoke("Vosk could not start microphone recording.");
				_running = false;
				return;
			}
			Task.Run(() => ThreadedWork(sessionId)).ConfigureAwait(false);
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

		if (_threadedStatusQueue.TryDequeue(out string statusMessage))
		{
			OnStatusUpdated?.Invoke(statusMessage);
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

		if (!IsVoskNativeAvailable)
		{
			return;
		}

		// Snapshot every pending audio frame on the main thread *before*
		// scheduling the finalize worker. As soon as the next recording
		// session starts, VoiceProcessor will begin enqueueing fresh
		// frames into _threadedBufferQueue, and we MUST NOT let those
		// frames be drained into the previous session's decoder (which
		// would corrupt the new session's leading audio and then get
		// wiped by Reset()). Frames captured up to this instant belong
		// to the session being closed; everything after this belongs to
		// whatever session starts next.
		var pendingAudio = new List<short[]>();
		while (_threadedBufferQueue.TryDequeue(out short[] frame))
		{
			pendingAudio.Add(frame);
		}

		int stoppedSessionId = _recordingSessionId;

		// FinalResult() performs the full Viterbi back-trace and was
		// blocking the Unity main thread on the ML2, which is what caused
		// the "flash / black-out" the user reported every time a recording
		// ended. _recognizerLock serializes the background decode against
		// ThreadedWork (and against OnDestroy disposal), so this is safe.
		// The task handle is stashed so the next ThreadedWork can await
		// it before touching the recognizer; combined with the snapshot
		// above, that guarantees session N+1 frames cannot leak into
		// session N's decode and session N's transcript cannot leak
		// into session N+1's UI.
		_lastFinalizeTask = Task.Run(() => EmitFinalResult(stoppedSessionId, pendingAudio));
	}

	//Feeds the autio logic into the vosk recorgnizer
	private async Task ThreadedWork(int sessionId)
	{
		// If the previous recording's finalize task is still running,
		// wait for it before feeding the recognizer. EmitFinalResult
		// drains the previous session's audio snapshot, runs
		// FinalResult(), and calls Reset() — all under _recognizerLock.
		// Without this await, the lock alone serializes individual
		// AcceptWaveform calls but does not stop us from mixing session
		// N+1 audio into session N's decoder state (with session N+1's
		// leading frames then being wiped by Reset).
		Task priorFinalize = _lastFinalizeTask;
		if (priorFinalize != null && !priorFinalize.IsCompleted)
		{
			try
			{
				await priorFinalize.ConfigureAwait(false);
			}
			catch (Exception priorException)
			{
				// Prior finalize already logged its own failure; we
				// only need to make sure that failure doesn't kill
				// the new worker.
				Debug.LogWarning($"[Vosk] Previous finalize task ended with exception: {priorException.Message}");
			}
		}

		voskRecognizerReadMarker.Begin();

		try
		{
			while (_running && sessionId == _recordingSessionId)
			{
				if (_threadedBufferQueue.TryDequeue(out short[] voiceResult))
				{
					bool hasResult;
					string result = null;
					lock (_recognizerLock)
					{
						// OnDestroy can null the recognizer between our
						// _running check and the lock acquisition. Bail
						// instead of NPE-ing on a worker thread.
						if (!_recognizerReady || _recognizer == null)
						{
							break;
						}

						hasResult = _recognizer.AcceptWaveform(voiceResult, voiceResult.Length);
						if (hasResult)
						{
							result = _recognizer.Result();
							if (EmitResultsOnlyOnStop && HasRecognitionText(result))
							{
								_lastEndpointResult = result;
							}
						}
						else if (!hasResult)
						{
							result = _recognizer.PartialResult();
						}
					}

					if (hasResult && !EmitResultsOnlyOnStop && !string.IsNullOrWhiteSpace(result))
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

	private void EmitFinalResult(int sessionId, List<short[]> pendingAudio)
	{
		try
		{
			string finalResult;
			string endpointResult;
			lock (_recognizerLock)
			{
				// The recognizer may have been disposed by OnDestroy while
				// this task was queued. Bail out silently in that case
				// instead of throwing a NullRef on a background thread.
				if (!_recognizerReady || _recognizer == null)
				{
					Debug.LogWarning("[Vosk] Cannot emit final result because recognizer is not ready.");
					return;
				}

				// Drain the audio that was captured up to the Stop moment.
				// Iterating the local snapshot (instead of the shared
				// _threadedBufferQueue) means session N+1 frames cannot
				// sneak into session N's decode.
				if (pendingAudio != null)
				{
					for (int i = 0; i < pendingAudio.Count; i++)
					{
						short[] frame = pendingAudio[i];
						if (frame == null || frame.Length == 0)
						{
							continue;
						}

						if (_recognizer.AcceptWaveform(frame, frame.Length))
						{
							string drainEndpoint = _recognizer.Result();
							if (HasRecognitionText(drainEndpoint))
							{
								_lastEndpointResult = drainEndpoint;
							}
						}
					}
				}

				finalResult = _recognizer.FinalResult();
				endpointResult = _lastEndpointResult;
				_lastEndpointResult = "";

				// Reuse the recognizer across sessions instead of
				// disposing it. vosk_recognizer_reset() is essentially
				// free and avoids the expensive vosk_recognizer_new_grm
				// call (which rebuilds the decoding FST) on every Start.
				// This was a major contributor to the per-transcription
				// main-thread stall on ML2. Reset must always run so
				// the recognizer is clean for whatever session ThreadedWork
				// processes next.
				_recognizer.Reset();
				_lastPartialResult = "";
			}

			if (!HasRecognitionText(finalResult) && HasRecognitionText(endpointResult))
			{
				finalResult = endpointResult;
			}

			// Belt + suspenders against late finalization. If the user
			// already started a new recording session while we were
			// finalizing, dropping this transcript prevents an old
			// utterance from being submitted to Luna against the new
			// session's intent. The await in ThreadedWork already
			// prevents recognizer-state corruption in this case; the
			// session-id check here also prevents the stale text from
			// surfacing in the UI.
			if (sessionId != _recordingSessionId)
			{
				Debug.Log("[Vosk] Dropping stale final result from previous recording session.");
				return;
			}

			Debug.Log($"[Vosk] Final result: {finalResult}");
			_threadedResultQueue.Enqueue(finalResult);
		}
		catch (Exception exception)
		{
			Debug.LogError($"[Vosk] Failed to emit final result: {exception}");
			// Marshal the status update through the main-thread queue —
			// EmitFinalResult now runs on a worker thread and OnStatusUpdated
			// subscribers touch UI objects.
			_threadedStatusQueue.Enqueue("Vosk failed to process recording.");
		}
	}

	private void ClearQueuedRecognitionData()
	{
		while (_threadedBufferQueue.TryDequeue(out _)) { }
		while (_threadedResultQueue.TryDequeue(out _)) { }
		while (_threadedPartialResultQueue.TryDequeue(out _)) { }
	}

	private static bool HasRecognitionText(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}

		var result = new RecognitionResult(json);
		return result.Phrases != null &&
		       result.Phrases.Length > 0 &&
		       !string.IsNullOrWhiteSpace(result.Phrases[0]?.Text);
	}

	private void OnDestroy()
	{
		// Stop the worker loop first so ThreadedWork exits its while loop
		// before we tear the recognizer down underneath it.
		_running = false;

		if (VoiceProcessor != null)
		{
			VoiceProcessor.OnFrameCaptured -= VoiceProcessorOnOnFrameCaptured;
			VoiceProcessor.OnRecordingStop -= VoiceProcessorOnOnRecordingStop;
		}

		if (!IsVoskNativeAvailable)
		{
			// Nothing native was ever created on this platform.
			return;
		}

		try
		{
			lock (_recognizerLock)
			{
				if (_recognizer != null)
				{
					_recognizer.Dispose();
					_recognizer = null;
				}
				_recognizerReady = false;
			}
		}
		catch (Exception exception)
		{
			Debug.LogWarning($"[Vosk] Recognizer cleanup failed during OnDestroy: {exception}");
		}

		try
		{
			if (_model != null)
			{
				_model.Dispose();
				_model = null;
			}
		}
		catch (Exception exception)
		{
			Debug.LogWarning($"[Vosk] Model cleanup failed during OnDestroy: {exception}");
		}
	}

}
