/* * * * *
 * A unity voice processor
 * ------------------------------
 * 
 * A Unity script for recording and delivering frames of audio for real-time processing
 * 
 * Written by Picovoice 
 * 2021-02-19
 * 
 * Apache License
 * 
 * Copyright (c) 2021 Picovoice
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 *   you may not use this file except in compliance with the License.
 *   You may obtain a copy of the License at
 *   
 *   http://www.apache.org/licenses/LICENSE-2.0
 *   
 *   Unless required by applicable law or agreed to in writing, software
 *   distributed under the License is distributed on an "AS IS" BASIS,
 *   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *   See the License for the specific language governing permissions and
 *   limitations under the License.
 * 
 * * * * */
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Class that records audio and delivers frames for real-time audio processing
/// </summary>
public class VoiceProcessor : MonoBehaviour
{
    /// <summary>
    /// Indicates whether microphone is capturing or not
    /// </summary>
    public bool IsRecording
    {
        get { return _audioClip != null && Microphone.IsRecording(CurrentDeviceName); }
    }

    [SerializeField] private int MicrophoneIndex;

    /// <summary>
    /// Sample rate of recorded audio
    /// </summary>
    public int SampleRate { get; private set; }

    /// <summary>
    /// Size of audio frames that are delivered
    /// </summary>
    public int FrameLength { get; private set; }

    /// <summary>
    /// Event where frames of audio are delivered
    /// </summary>
    public event Action<short[]> OnFrameCaptured;

    /// <summary>
    /// Event when audio capture thread stops
    /// </summary>
    public event Action OnRecordingStop;

    /// <summary>
    /// Event when audio capture thread starts
    /// </summary>
    public event Action OnRecordingStart;

    /// <summary>
    /// Available audio recording devices
    /// </summary>
    public List<string> Devices { get; private set; }

    /// <summary>
    /// Index of selected audio recording device
    /// </summary>
    public int CurrentDeviceIndex { get; private set; }

    /// <summary>
    /// Name of selected audio recording device
    /// </summary>
    public string CurrentDeviceName
    {
        get
        {
            if (Devices == null || CurrentDeviceIndex < 0 || CurrentDeviceIndex >= Devices.Count)
                return null;
            return Devices[CurrentDeviceIndex];
        }
    }

    [Header("Voice Detection Settings")]
    // Magic Leap 2's headset mic often peaks around 0.02-0.04 for normal indoor
    // speech. This is only the peak gate; RMS and noise-floor gates below keep
    // single-sample spikes from resetting the silence timer.
    [SerializeField, Tooltip("The minimum volume to detect voice input for"), Range(0.0f, 1.0f)]
    private float _minimumSpeakingSampleValue = 0.01f;

    [SerializeField, Tooltip("Minimum RMS frame volume required before audio is considered speech."), Range(0.0f, 1.0f)]
    private float _minimumSpeakingRmsValue = 0.0015f;

    [SerializeField, Tooltip("Speech must rise this many times above the learned room-noise RMS floor."), Range(1.0f, 10.0f)]
    private float _noiseFloorMultiplier = 2.0f;

    [SerializeField, Tooltip("Time in seconds of detected silence before voice request is sent")]
    private float _silenceTimer = 1.0f;

    [SerializeField, Tooltip("How long speech-like audio must persist before recording opens."), Range(0.0f, 0.5f)]
    private float _speechStartDebounceSeconds = 0.06f;

    [SerializeField, Tooltip("How long quiet audio must persist before speech is considered paused."), Range(0.0f, 0.5f)]
    private float _speechEndDebounceSeconds = 0.12f;

    [SerializeField, Tooltip("Audio kept before the gate opens, so the first word is not clipped."), Range(0.0f, 0.5f)]
    private float _preSpeechBufferSeconds = 0.25f;

    [SerializeField, Tooltip("Auto detect speech using the volume threshold.")]
    private bool _autoDetect;

    [SerializeField, Tooltip("When auto-detecting, still send every frame to Vosk. The gate only decides when speech has started and when to stop.")]
    private bool _sendAllFramesToRecognizerWhileAutoDetecting = true;

    public float SilenceTimer
    {
        get { return _silenceTimer; }
        set { _silenceTimer = Mathf.Max(0f, value); }
    }

    public float MinimumSpeakingSampleValue
    {
        get { return _minimumSpeakingSampleValue; }
        set { _minimumSpeakingSampleValue = Mathf.Clamp01(value); }
    }

    public float MinimumSpeakingRmsValue
    {
        get { return _minimumSpeakingRmsValue; }
        set { _minimumSpeakingRmsValue = Mathf.Clamp01(value); }
    }

    public float NoiseFloorMultiplier
    {
        get { return _noiseFloorMultiplier; }
        set { _noiseFloorMultiplier = Mathf.Max(1f, value); }
    }

    public float SpeechStartDebounceSeconds
    {
        get { return _speechStartDebounceSeconds; }
        set { _speechStartDebounceSeconds = Mathf.Clamp(value, 0f, 0.5f); }
    }

    public float SpeechEndDebounceSeconds
    {
        get { return _speechEndDebounceSeconds; }
        set { _speechEndDebounceSeconds = Mathf.Clamp(value, 0f, 0.5f); }
    }

    /// <summary>
    /// Highest absolute sample amplitude observed in the most recent capture frame.
    /// Useful for diagnosing "no audio detected" issues: if this never exceeds the
    /// threshold during a recording, the mic is too quiet or the threshold too high.
    /// </summary>
    public float LastFrameMaxAmplitude { get; private set; }
    public float LastFrameRmsAmplitude { get; private set; }
    public float EstimatedNoiseFloorRms { get; private set; }
    public float CurrentRmsSpeechThreshold { get; private set; }
    public bool HasDetectedSpeech { get; private set; }

    public bool StopRecordingAfterSilence { get; set; }

    public bool SendAllFramesToRecognizerWhileAutoDetecting
    {
        get { return _sendAllFramesToRecognizerWhileAutoDetecting; }
        set { _sendAllFramesToRecognizerWhileAutoDetecting = value; }
    }

    private float _timeAtSilenceBegan;
    private float _timeAtSpeechCandidateBegan;
    private float _timeAtQuietCandidateBegan;
    private bool _audioDetected;
    private bool _didDetect;
    private bool _transmit;
    private readonly Queue<short[]> _preSpeechFrames = new Queue<short[]>();
    private int _maxPreSpeechFrames = 1;


    AudioClip _audioClip;
    private Coroutine _recordDataCoroutine;
    private event Action RestartRecording;

    void Awake()
    {
        UpdateDevices();
    }
#if UNITY_EDITOR
    void Update()
    {
        if (CurrentDeviceIndex != MicrophoneIndex)
        {
            ChangeDevice(MicrophoneIndex);
        }
    }
#endif

    /// <summary>
    /// Updates list of available audio devices
    /// </summary>
    public void UpdateDevices()
    {
        Devices = new List<string>();
        foreach (var device in Microphone.devices)
            Devices.Add(device);

        if (Devices == null || Devices.Count == 0)
        {
            CurrentDeviceIndex = -1;
            Debug.LogWarning("No named recording devices were reported. Unity will try the default microphone.");
            return;
        }

        CurrentDeviceIndex = MicrophoneIndex;
    }

    /// <summary>
    /// Change audio recording device
    /// </summary>
    /// <param name="deviceIndex">Index of the new audio capture device</param>
    public void ChangeDevice(int deviceIndex)
    {
        if (deviceIndex < 0 || deviceIndex >= Devices.Count)
        {
            Debug.LogError(string.Format("Specified device index {0} is not a valid recording device", deviceIndex));
            return;
        }

        if (IsRecording)
        {
            // one time event to restart recording with the new device 
            // the moment the last session has completed
            RestartRecording += () =>
            {
                CurrentDeviceIndex = deviceIndex;
                StartRecording(SampleRate, FrameLength);
                RestartRecording = null;
            };
            StopRecording();
        }
        else
        {
            CurrentDeviceIndex = deviceIndex;
        }
    }

    /// <summary>
    /// Start recording audio
    /// </summary>
    /// <param name="sampleRate">Sample rate to record at</param>
    /// <param name="frameSize">Size of audio frames to be delivered</param>
    /// <param name="autoDetect">Should the audio continuously record based on the volume</param>
    public void StartRecording(int sampleRate = 16000, int frameSize = 512, bool ?autoDetect = null)
    {
        if (autoDetect != null)
        {
            _autoDetect = (bool) autoDetect;
        }

        if (IsRecording)
        {
            // if sample rate or frame size have changed, restart recording
            if (sampleRate != SampleRate || frameSize != FrameLength)
            {
                RestartRecording += () =>
                {
                    StartRecording(SampleRate, FrameLength, autoDetect);
                    RestartRecording = null;
                };
                StopRecording();
            }

            return;
        }

        SampleRate = sampleRate;
        FrameLength = frameSize;
        _audioDetected = false;
        _didDetect = false;
        HasDetectedSpeech = false;
        _transmit = false;
        _timeAtSilenceBegan = Time.unscaledTime;
        _timeAtSpeechCandidateBegan = -1f;
        _timeAtQuietCandidateBegan = -1f;
        _preSpeechFrames.Clear();
        _maxPreSpeechFrames = Mathf.Max(1, Mathf.CeilToInt(_preSpeechBufferSeconds / ((float) frameSize / sampleRate)));
        EstimatedNoiseFloorRms = Mathf.Max(0.0001f, _minimumSpeakingRmsValue / _noiseFloorMultiplier);
        CurrentRmsSpeechThreshold = _minimumSpeakingRmsValue;

        _audioClip = Microphone.Start(CurrentDeviceName, true, 1, sampleRate);

        _recordDataCoroutine = StartCoroutine(RecordData());
    }

    /// <summary>
    /// Stops recording audio
    /// </summary>
    public void StopRecording()
    {
        if (!IsRecording)
            return;

        Microphone.End(CurrentDeviceName);
        Destroy(_audioClip);
        _audioClip = null;
        _didDetect = false;

        if (_recordDataCoroutine != null)
        {
            StopCoroutine(_recordDataCoroutine);
            _recordDataCoroutine = null;
        }

        if (OnRecordingStop != null)
            OnRecordingStop.Invoke();
    }

    /// <summary>
    /// Loop for buffering incoming audio data and delivering frames
    /// </summary>
    IEnumerator RecordData()
    {
        float[] sampleBuffer = new float[FrameLength];
        int startReadPos = 0;

        if (OnRecordingStart != null)
            OnRecordingStart.Invoke();

        while (IsRecording)
        {
            int curClipPos = Microphone.GetPosition(CurrentDeviceName);
            if (curClipPos < startReadPos)
                curClipPos += _audioClip.samples;

            int samplesAvailable = curClipPos - startReadPos;
            if (samplesAvailable < FrameLength)
            {
                yield return null;
                continue;
            }

            int endReadPos = startReadPos + FrameLength;
            if (endReadPos > _audioClip.samples)
            {
                // fragmented read (wraps around to beginning of clip)
                // read bit at end of clip
                int numSamplesClipEnd = _audioClip.samples - startReadPos;
                float[] endClipSamples = new float[numSamplesClipEnd];
                _audioClip.GetData(endClipSamples, startReadPos);

                // read bit at start of clip
                int numSamplesClipStart = endReadPos - _audioClip.samples;
                float[] startClipSamples = new float[numSamplesClipStart];
                _audioClip.GetData(startClipSamples, 0);

                // combine to form full frame
                Array.Copy(endClipSamples, 0, sampleBuffer, 0, numSamplesClipEnd);
                Array.Copy(startClipSamples, 0, sampleBuffer, numSamplesClipEnd, numSamplesClipStart);
            }
            else
            {
                _audioClip.GetData(sampleBuffer, startReadPos);
            }

            startReadPos = endReadPos % _audioClip.samples;

            // Convert once per frame and use both peak and RMS energy. Peak-only
            // detection is easily fooled by clicks, wind, or short headset spikes.
            float maxVolume = 0.0f;
            float sumSquares = 0.0f;
            short[] pcmBuffer = new short[sampleBuffer.Length];
            for (int i = 0; i < sampleBuffer.Length; i++)
            {
                float abs = sampleBuffer[i] < 0 ? -sampleBuffer[i] : sampleBuffer[i];
                if (abs > maxVolume)
                {
                    maxVolume = abs;
                }
                sumSquares += sampleBuffer[i] * sampleBuffer[i];
                pcmBuffer[i] = (short) Math.Floor(sampleBuffer[i] * short.MaxValue);
            }
            float rmsVolume = Mathf.Sqrt(sumSquares / sampleBuffer.Length);
            LastFrameMaxAmplitude = maxVolume;
            LastFrameRmsAmplitude = rmsVolume;

            if (_autoDetect == false)
            {
                _transmit = _audioDetected = true;
                HasDetectedSpeech = true;
                OnFrameCaptured?.Invoke(pcmBuffer);
            }
            else
            {
                bool speechCandidate = IsSpeechCandidate(maxVolume, rmsVolume);
                float now = Time.unscaledTime;
                bool sentFrameToRecognizer = false;
                _transmit = false;

                if (_sendAllFramesToRecognizerWhileAutoDetecting && OnFrameCaptured != null)
                {
                    OnFrameCaptured.Invoke(pcmBuffer);
                    sentFrameToRecognizer = true;
                }

                if (!_audioDetected)
                {
                    if (!_sendAllFramesToRecognizerWhileAutoDetecting)
                    {
                        BufferPreSpeechFrame(pcmBuffer);
                    }
                    UpdateNoiseFloor(rmsVolume, speechCandidate);

                    if (speechCandidate)
                    {
                        if (_timeAtSpeechCandidateBegan < 0f)
                        {
                            _timeAtSpeechCandidateBegan = now;
                        }

                        if (now - _timeAtSpeechCandidateBegan >= _speechStartDebounceSeconds)
                        {
                            _audioDetected = true;
                            _didDetect = true;
                            HasDetectedSpeech = true;
                            _transmit = true;
                            _timeAtSilenceBegan = now;
                            _timeAtQuietCandidateBegan = -1f;
                            if (!_sendAllFramesToRecognizerWhileAutoDetecting)
                            {
                                FlushPreSpeechFrames();
                                continue;
                            }
                        }
                    }
                    else
                    {
                        _timeAtSpeechCandidateBegan = -1f;
                    }
                }
                else
                {
                    if (speechCandidate)
                    {
                        _transmit = true;
                        _timeAtSilenceBegan = now;
                        _timeAtQuietCandidateBegan = -1f;
                    }
                    else
                    {
                        if (_timeAtQuietCandidateBegan < 0f)
                        {
                            _timeAtQuietCandidateBegan = now;
                        }

                        if (now - _timeAtQuietCandidateBegan < _speechEndDebounceSeconds)
                        {
                            _transmit = true;
                        }
                        else if (now - _timeAtSilenceBegan > _silenceTimer)
                        {
                            _audioDetected = false;
                        }
                    }
                }

                if (!sentFrameToRecognizer && _audioDetected && OnFrameCaptured != null)
                    OnFrameCaptured.Invoke(pcmBuffer);
            }

            if (!_audioDetected)
            {
                if (_didDetect)
                {
                    if (StopRecordingAfterSilence)
                    {
                        StopRecording();
                        yield break;
                    }

                    if (OnRecordingStop != null)
                        OnRecordingStop.Invoke();
                    _didDetect = false;
                }
            }
        }


        if (OnRecordingStop != null)
            OnRecordingStop.Invoke();
        _recordDataCoroutine = null;
        if (RestartRecording != null)
            RestartRecording.Invoke();
    }

    private bool IsSpeechCandidate(float maxVolume, float rmsVolume)
    {
        CurrentRmsSpeechThreshold = Mathf.Max(_minimumSpeakingRmsValue, EstimatedNoiseFloorRms * _noiseFloorMultiplier);
        return maxVolume >= _minimumSpeakingSampleValue && rmsVolume >= CurrentRmsSpeechThreshold;
    }

    private void UpdateNoiseFloor(float rmsVolume, bool speechCandidate)
    {
        if (speechCandidate || _audioDetected)
        {
            return;
        }

        float smoothing = rmsVolume > EstimatedNoiseFloorRms ? 0.08f : 0.25f;
        EstimatedNoiseFloorRms = Mathf.Lerp(EstimatedNoiseFloorRms, rmsVolume, smoothing);
    }

    private void BufferPreSpeechFrame(short[] pcmBuffer)
    {
        short[] copy = new short[pcmBuffer.Length];
        Array.Copy(pcmBuffer, copy, pcmBuffer.Length);
        _preSpeechFrames.Enqueue(copy);
        while (_preSpeechFrames.Count > _maxPreSpeechFrames)
        {
            _preSpeechFrames.Dequeue();
        }
    }

    private void FlushPreSpeechFrames()
    {
        while (_preSpeechFrames.Count > 0)
        {
            OnFrameCaptured?.Invoke(_preSpeechFrames.Dequeue());
        }
    }
}
