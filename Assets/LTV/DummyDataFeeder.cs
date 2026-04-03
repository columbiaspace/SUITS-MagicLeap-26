using UnityEngine;
using System.Collections.Generic;

public class DummyDataFeeder : MonoBehaviour
{
    [Header("Test Data")]
    public TextAsset dummyJsonFile;
    
    [Header("Backend Target")]
    public LtvErrorQueueService queueService;

    // --- Data structures to match the JSON ---
    [System.Serializable]
    public class DummyError 
    {
        public string code;
        public string description;
        public bool needs_resolved;
        public List<string> procedures;
    }

    [System.Serializable]
    public class DummyErrorWrapper 
    {
        public List<DummyError> errors;
    }

    void Start()
    {
        // Add a small delay so the HUD Controller has time to initialize
        Invoke(nameof(InjectDummyData), 0.5f);
    }

    private void InjectDummyData()
    {
        if (dummyJsonFile == null || queueService == null)
        {
            Debug.LogError("DummyDataFeeder is missing its JSON file or Queue Service!");
            return;
        }

        // 1. Parse the JSON using Unity's built-in utility
        DummyErrorWrapper wrapper = JsonUtility.FromJson<DummyErrorWrapper>(dummyJsonFile.text);
        
        // 2. Convert it into the Dictionary format the backend expects
        List<Dictionary<string, object>> rawList = new List<Dictionary<string, object>>();

        foreach (var err in wrapper.errors)
        {
            var dict = new Dictionary<string, object>();
            dict["code"] = err.code;
            dict["description"] = err.description;
            dict["needs_resolved"] = err.needs_resolved;
            
            // The backend explicitly expects Procedures as a List<object>
            List<object> procedureObjects = new List<object>();
            foreach (var p in err.procedures)
            {
                procedureObjects.Add(p);
            }
            dict["procedures"] = procedureObjects;

            rawList.Add(dict);
        }

        // 3. Force the queue service to start using our fake data!
        Debug.Log("Injecting Dummy Data into LTV Queue...");
        queueService.StartDiagnosis(rawList);
    }
}