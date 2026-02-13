using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.MagicLeap;

public class VoiceIntents : MonoBehaviour
{
    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();   
    public MLVoiceIntentsConfiguration VoiceIntentsConfiguration;
    public GameObject targetObject;
    private bool shouldRotate = false;
    public float rotationSpeed = 50f;

    // request permission for voice input at start
    private void Start()
    {
        MLPermissions.RequestPermission(MLPermission.VoiceInput, permissionCallbacks);

    }

    void Update()
    {
        if (shouldRotate && targetObject != null)
        {
            targetObject.transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
        }
    }
    
    // subscribe to permission events
    private void Awake()
    {
        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;
    }

    // unsubscribe from permission events
    private void OnDestroy()
    {
        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;
    }


    // on voice permission denied, disable script
    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"Failed to initialize voice intents due to missing or denied {MLPermission.VoiceInput} permission. Please add to manifest. Disabling script.");
        enabled = false;
    }

    // on voice permission granted, initialize voice input
    private void OnPermissionGranted(string permission)
    {
        if (permission == MLPermission.VoiceInput)
            InitializeVoiceInput();
            
    }


    // check if voice commands setting is enabled, then set up voice intents
    private void InitializeVoiceInput()
    {
        bool isVoiceEnabled = MLVoice.VoiceEnabled;

        // if voice setting is enabled, try to set up voice intents
        if (isVoiceEnabled)
        {
            Debug.Log("Voice commands setting is enabled");
            var result = MLVoice.SetupVoiceIntents(VoiceIntentsConfiguration);
            if (result.IsOk)
            {
                Debug.Log("Voice intents successfully initialized");
                MLVoice.OnVoiceEvent += MLVoiceOnVoiceEvent;
                
            }
            else
            {
                Debug.LogError("Voice could not initialize: " + result);
            }
        }

        // if voice setting is disabled, open voice settings so user can enable it
        else
        {
            Debug.Log("Voice commands setting is disabled - opening settings");
            UnityEngine.XR.MagicLeap.SettingsIntentsLauncher.LaunchSystemVoiceInputSettings();
            Application.Quit();
        }
    }

    // handle voice events
    private void MLVoiceOnVoiceEvent(in bool wasSuccessful, in MLVoice.IntentEvent voiceEvent)
    {
        if (wasSuccessful)
        {
            switch (voiceEvent.EventID)
            {
                case 101:
                    Debug.Log("Show object");
                    targetObject.SetActive(true);
              
                    break;

                case 102:
                    Debug.Log("Hide object");
                    targetObject.SetActive(false);
             
                    break;

                case 103:
                    Debug.Log("Start rotating object");
                    shouldRotate = true;

                    break;

                case 104:
                    Debug.Log("Stop rotating object");
                    shouldRotate = false;
       
                    break;
            }
        }
    }

}