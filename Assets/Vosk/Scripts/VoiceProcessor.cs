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
    // Lowered from the upstream 0.05f default. Magic Leap 2's headset mic regularly
    // peaks around 0.02–0.04 for normal indoor speech, so the old threshold caused
    // Vosk to receive zero frames and return an empty transcript ("Vosk could not
    // transcribe that recording"). 0.009 lets typical speech through while still
    // rejecting room tone (which is usually well under 0.005).
    [SerializeField, Tooltip("The minimum volume to detect voice input for"), Range(0.0f, 1.0f)]
    private float _minimumSpeakingSampleValue = 0.009f;
    // Raised from 0.0025 → 0.003 for outdoor / wind scenarios. Wind buffeting on
    // the ML2 headset mic produces sustained low-energy noise that occasionally
    // crests the peak-amplitude gate (a single sample can spike from a gust)
    // but holds the per-frame RMS below true-speech levels (RMS for indoor
    // speech reliably sits well above 0.005, room tone < 0.0015, wind hiss
    // 0.0015–0.0028). Bumping the RMS floor to 0.003 rejects that wind band
    // without clipping normal speech, which keeps Vosk from being fed long
    // runs of pure-noise frames that it would otherwise hallucinate words
    // for ("verge recording", etc.).
    private const float MinimumSpeakingRmsValue = 0.003f;

    [SerializeField, Tooltip("Time in seconds of detected silence before voice request is sent")]
    private float _silenceTimer = 1.0f;

    [SerializeField, Tooltip("Auto detect speech using the volume threshold.")]
    private bool _autoDetect;

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

    /// <summary>
    /// Highest absolute sample amplitude observed in the most recent capture frame.
    /// Useful for diagnosing "no audio detected" issues: if this never exceeds the
    /// threshold during a recording, the mic is too quiet or the threshold too high.
    /// </summary>
    public float LastFrameMaxAmplitude { get; private set; }

    public bool StopRecordingAfterSilence { get; set; }

    private float _timeAtSilenceBegan;
    private bool _audioDetected;
    private bool _didDetect;
    private bool _transmit;


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
        _transmit = false;
        _timeAtSilenceBegan = Time.time;

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
                Buffer.BlockCopy(endClipSamples, 0, sampleBuffer, 0, numSamplesClipEnd);
                Buffer.BlockCopy(startClipSamples, 0, sampleBuffer, numSamplesClipEnd, numSamplesClipStart);
            }
            else
            {
                _audioClip.GetData(sampleBuffer, startReadPos);
            }

            startReadPos = endReadPos % _audioClip.samples;
            if (_autoDetect == false)
            {
                _transmit =_audioDetected = true;
            }
            else
            {
                // Absolute value: speech waveforms swing both positive and negative,
                // so checking sampleBuffer[i] > maxVolume alone misses every trough.
                float maxVolume = 0.0f;
                float sumSquares = 0.0f;
                for (int i = 0; i < sampleBuffer.Length; i++)
                {
                    float abs = sampleBuffer[i] < 0 ? -sampleBuffer[i] : sampleBuffer[i];
                    if (abs > maxVolume)
                    {
                        maxVolume = abs;
                    }
                    sumSquares += sampleBuffer[i] * sampleBuffer[i];
                }
                float rmsVolume = Mathf.Sqrt(sumSquares / sampleBuffer.Length);
                LastFrameMaxAmplitude = maxVolume;

                if (maxVolume >= _minimumSpeakingSampleValue && rmsVolume >= MinimumSpeakingRmsValue)
                {
                    _transmit= _audioDetected = true;
                    _timeAtSilenceBegan = Time.time;
                }
                else
                {
                    _transmit = false;

                    if (_audioDetected && Time.time - _timeAtSilenceBegan > _silenceTimer)
                    {
                        _audioDetected = false;
                    }
                }
            }

            if (_audioDetected)
            {
                _didDetect = true;
                // converts to 16-bit int samples
                short[] pcmBuffer = new short[sampleBuffer.Length];
                for (int i = 0; i < FrameLength; i++)
                {
                    pcmBuffer[i] = (short) Math.Floor(sampleBuffer[i] * short.MaxValue);
                }

                // raise buffer event
                if (OnFrameCaptured != null)
                    OnFrameCaptured.Invoke(pcmBuffer);
            }
            else
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
}
