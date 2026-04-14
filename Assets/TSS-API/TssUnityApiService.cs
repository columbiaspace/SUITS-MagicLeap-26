using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TssApi
{
    public class TssUnityApiService : MonoBehaviour
    {
        private const int UdpGetEva = 1;
        private const int UdpGetLtv = 2;
        private const int UdpGetLtvErrors = 3;

        [Header("TSS UDP Source")]
        [SerializeField] private string tssHost = "127.0.0.1";
        [SerializeField] private int tssPort = 14141;
        [SerializeField] private float pollIntervalSeconds = 0.25f;
        [SerializeField] private int udpTimeoutMs = 500;
        [SerializeField] private int udpRetries = 2;

        [Header("Procedure Data")]
        [SerializeField] private TextAsset ltvProceduresJson;

        public event Action<Dictionary<string, object>> EvaUpdated;

        public static TssUnityApiService Instance { get; private set; }

        private TssUdpClient _udp;
        private Coroutine _pollCoroutine;
        private Dictionary<string, object> _eva = new Dictionary<string, object>();
        private Dictionary<string, object> _ltv = new Dictionary<string, object>();
        private Dictionary<string, object> _procedures = new Dictionary<string, object>();
        private bool _sourceOnline;
        private double _lastUpdatedUnix;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadProcedures();
        }

        private void OnEnable()
        {
            InitializeUdp();
            if (_pollCoroutine == null)
            {
                _pollCoroutine = StartCoroutine(PollLoop());
            }
        }

        private void OnDisable()
        {
            if (_pollCoroutine != null)
            {
                StopCoroutine(_pollCoroutine);
                _pollCoroutine = null;
            }

            if (_udp != null)
            {
                _udp.Dispose();
                _udp = null;
            }
        }

        public Dictionary<string, object> GetHealth()
        {
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "source_online", _sourceOnline },
                { "last_updated_unix", _sourceOnline ? _lastUpdatedUnix : 0.0d }
            };
        }

        public Dictionary<string, object> GetEva()
        {
            return new Dictionary<string, object>
            {
                { "status", NormalizeStatusSectionForUi(_eva) },
                { "telemetry", GetNestedDict(_eva, "telemetry") },
                { "dcu", GetNestedDict(_eva, "dcu") },
                { "error", GetNestedDict(_eva, "error") },
                { "imu", GetNestedDict(_eva, "imu") },
                { "uia", NormalizeUiaSectionForUi(_eva) }
            };
        }

        public Dictionary<string, object> GetEvaById(string evaId)
        {
            string normalizedEvaId = NormalizeEvaId(evaId);
            Dictionary<string, object> telemetryRoot = GetNestedDict(_eva, "telemetry");
            Dictionary<string, object> dcuRoot = GetNestedDict(_eva, "dcu");
            Dictionary<string, object> imuRoot = GetNestedDict(_eva, "imu");

            return new Dictionary<string, object>
            {
                { "telemetry", GetNestedDict(telemetryRoot, normalizedEvaId) },
                { "dcu", GetNestedDict(dcuRoot, normalizedEvaId) },
                { "imu", GetNestedDict(imuRoot, normalizedEvaId) }
            };
        }

        public Dictionary<string, object> GetEvaErrors()
        {
            return GetNestedDict(_eva, "error");
        }

        public Dictionary<string, object> GetEvaUiaById(string evaId)
        {
            string normalizedEvaId = NormalizeEvaId(evaId);
            Dictionary<string, object> uia = NormalizeUiaSectionForUi(_eva);
            string prefix = normalizedEvaId + "_";

            return new Dictionary<string, object>
            {
                { "evaId", normalizedEvaId },
                {
                    "uia",
                    new Dictionary<string, object>
                    {
                        { "power", ToBool(GetPath(uia, prefix + "power", false)) },
                        { "oxy", ToBool(GetPath(uia, prefix + "oxy", false)) },
                        { "water_supply", ToBool(GetPath(uia, prefix + "water_supply", false)) },
                        { "water_waste", ToBool(GetPath(uia, prefix + "water_waste", false)) }
                    }
                },
                {
                    "shared",
                    new Dictionary<string, object>
                    {
                        { "oxy_vent", ToBool(GetPath(uia, "oxy_vent", false)) },
                        { "depress", ToBool(GetPath(uia, "depress", false)) }
                    }
                }
            };
        }

        public Dictionary<string, object> GetEvaDcuById(string evaId)
        {
            string normalizedEvaId = NormalizeEvaId(evaId);
            Dictionary<string, object> dcuRoot = GetNestedDict(_eva, "dcu");
            return GetNestedDict(dcuRoot, normalizedEvaId);
        }

        public Dictionary<string, object> GetDcuEva1()
        {
            return GetEvaDcuById("eva1");
        }

        public Dictionary<string, object> GetImuEva1()
        {
            Dictionary<string, object> imuRoot = GetNestedDict(_eva, "imu");
            return GetNestedDict(imuRoot, "eva1");
        }

        public Dictionary<string, object> GetStatus()
        {
            return GetNestedDict(_eva, "status");
        }

        public Dictionary<string, object> GetLtv()
        {
            return new Dictionary<string, object>
            {
                { "location", GetNestedDict(_ltv, "location") },
                { "signal", GetNestedDict(_ltv, "signal") },
                { "errors", GetNestedDict(_ltv, "errors") }
            };
        }

        public Dictionary<string, object> GetLtvErrors()
        {
            return GetNestedDict(_ltv, "errors");
        }

        public Dictionary<string, object> GetLtvSignal()
        {
            return GetNestedDict(_ltv, "signal");
        }

        public Dictionary<string, object> GetLtvLocation()
        {
            return GetNestedDict(_ltv, "location");
        }

        public Dictionary<string, object> GetEgressReadiness(string evaId)
        {
            string normalizedEvaId = NormalizeEvaId(evaId);
            Dictionary<string, object> eva = GetEva();

            List<object> checks = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "name", "mission_started" },
                    { "pass", ToBool(GetPath(eva, "status.started", false)) },
                    { "path", "status.started" }
                },
                new Dictionary<string, object>
                {
                    { "name", "uia_depress" },
                    { "pass", ToBool(GetPath(eva, "uia.depress", false)) },
                    { "path", "uia.depress" }
                },
                new Dictionary<string, object>
                {
                    { "name", "uia_oxy_vent" },
                    { "pass", ToBool(GetPath(eva, "uia.oxy_vent", false)) },
                    { "path", "uia.oxy_vent" }
                }
            };

            return new Dictionary<string, object>
            {
                { "evaId", normalizedEvaId },
                { "checks", checks }
            };
        }

        public Dictionary<string, object> GetLtvProcedures()
        {
            List<object> results = new List<object>();
            Dictionary<string, object> ltv = GetNestedDict(_procedures, "ltv");
            foreach (KeyValuePair<string, object> kvp in ltv)
            {
                Dictionary<string, object> proc = kvp.Value as Dictionary<string, object>;
                string title = proc != null && proc.ContainsKey("title") ? Convert.ToString(proc["title"]) : kvp.Key;
                results.Add(new Dictionary<string, object> { { "id", kvp.Key }, { "title", title } });
            }

            return new Dictionary<string, object> { { "procedures", results } };
        }

        public List<Dictionary<string, object>> GetLtvErrorProcedures()
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            object rawList = GetPath(_ltv, "error_procedures", null);
            List<object> procedures = rawList as List<object>;

            if (procedures == null)
            {
                return result;
            }

            for (int i = 0; i < procedures.Count; i++)
            {
                Dictionary<string, object> entry = procedures[i] as Dictionary<string, object>;
                if (entry != null)
                {
                    result.Add(DeepCopyDict(entry));
                }
            }

            return result;
        }

        public Dictionary<string, object> GetLtvProcedure(string procedureId)
        {
            Dictionary<string, object> ltv = GetNestedDict(_procedures, "ltv");
            if (!ltv.ContainsKey(procedureId))
            {
                return null;
            }

            return DeepCopyDict(ltv[procedureId] as Dictionary<string, object>);
        }

        public Dictionary<string, object> GetLtvProcedureStatus(string procedureId)
        {
            Dictionary<string, object> procedure = GetLtvProcedure(procedureId);
            if (procedure == null)
            {
                return null;
            }

            Dictionary<string, object> mergedState = new Dictionary<string, object>
            {
                { "eva", DeepCopyDict(_eva) },
                { "ltv", DeepCopyDict(_ltv) }
            };

            List<object> stepsRaw = GetNestedList(procedure, "steps");
            List<object> stepsOut = new List<object>();
            int completed = 0;

            foreach (object stepObj in stepsRaw)
            {
                Dictionary<string, object> step = stepObj as Dictionary<string, object>;
                if (step == null)
                {
                    continue;
                }

                List<object> criteria = GetNestedList(step, "completion_criteria");
                List<object> checks = new List<object>();
                bool allPassed = true;

                foreach (object criterionObj in criteria)
                {
                    Dictionary<string, object> criterion = criterionObj as Dictionary<string, object>;
                    if (criterion == null)
                    {
                        continue;
                    }

                    bool passed = EvaluateCriterion(mergedState, criterion);
                    checks.Add(new Dictionary<string, object>
                    {
                        { "path", GetPath(criterion, "path", string.Empty) },
                        { "op", GetPath(criterion, "op", "eq") },
                        { "value", GetPath(criterion, "value", null) },
                        { "pass", passed }
                    });
                    allPassed = allPassed && passed;
                }

                if (allPassed)
                {
                    completed++;
                }

                stepsOut.Add(new Dictionary<string, object>
                {
                    { "id", GetPath(step, "id", string.Empty) },
                    { "instruction", GetPath(step, "instruction", string.Empty) },
                    { "criticality", GetPath(step, "criticality", "optional") },
                    { "hint", GetPath(step, "hint", string.Empty) },
                    { "voice_short", GetPath(step, "voice_short", string.Empty) },
                    { "complete", allPassed },
                    { "checks", checks }
                });
            }

            Dictionary<string, object> nextStep = null;
            foreach (object stepObj in stepsOut)
            {
                Dictionary<string, object> step = stepObj as Dictionary<string, object>;
                if (step != null && !ToBool(GetPath(step, "complete", false)))
                {
                    nextStep = step;
                    break;
                }
            }

            return new Dictionary<string, object>
            {
                { "procedure_id", procedureId },
                { "completed_steps", completed },
                { "total_steps", stepsOut.Count },
                { "complete", completed == stepsOut.Count },
                { "next_step", nextStep },
                { "steps", stepsOut }
            };
        }

        // Generic endpoint router for feature code that prefers route strings.
        public bool TryHandleEndpoint(string path, out object response, out int statusCode)
        {
            response = null;
            statusCode = 200;

            if (string.IsNullOrEmpty(path))
            {
                statusCode = 404;
                return false;
            }

            string trimmed = path.Trim();
            if (!trimmed.StartsWith("/"))
            {
                trimmed = "/" + trimmed;
            }

            string[] parts = trimmed.Trim('/').Split('/');
            if (trimmed == "/api/v1/health")
            {
                response = GetHealth();
                return true;
            }

            if (trimmed == "/api/v1/eva")
            {
                response = GetEva();
                return true;
            }

            if (trimmed == "/api/v1/eva/errors")
            {
                response = GetEvaErrors();
                return true;
            }

            if (trimmed == "/api/v1/dcu/eva1")
            {
                response = GetDcuEva1();
                return true;
            }

            if (trimmed == "/api/v1/imu/eva1")
            {
                response = GetImuEva1();
                return true;
            }

            if (trimmed == "/api/v1/status" || trimmed == "/api/v1/status/")
            {
                response = GetStatus();
                return true;
            }

            if (trimmed == "/api/v1/ltv")
            {
                response = GetLtv();
                return true;
            }

            if (trimmed == "/api/v1/ltv/errors")
            {
                response = GetLtvErrors();
                return true;
            }

            if (trimmed == "/api/v1/ltv/signal")
            {
                response = GetLtvSignal();
                return true;
            }

            if (trimmed == "/api/v1/ltv/location")
            {
                response = GetLtvLocation();
                return true;
            }

            if (trimmed == "/api/v1/procedures/ltv")
            {
                response = GetLtvProcedures();
                return true;
            }

            if (parts.Length == 4 && parts[0] == "api" && parts[1] == "v1" && parts[2] == "eva")
            {
                try
                {
                    response = GetEvaById(parts[3]);
                    return true;
                }
                catch
                {
                    statusCode = 404;
                    return false;
                }
            }

            if (parts.Length == 5 && parts[0] == "api" && parts[1] == "v1" && parts[2] == "eva")
            {
                try
                {
                    if (parts[4] == "uia")
                    {
                        response = GetEvaUiaById(parts[3]);
                        return true;
                    }

                    if (parts[4] == "dcu")
                    {
                        response = GetEvaDcuById(parts[3]);
                        return true;
                    }

                    if (parts[4] == "egress-readiness")
                    {
                        response = GetEgressReadiness(parts[3]);
                        return true;
                    }
                }
                catch
                {
                    statusCode = 404;
                    return false;
                }
            }

            if (parts.Length == 5 && parts[0] == "api" && parts[1] == "v1" && parts[2] == "procedures" && parts[3] == "ltv")
            {
                Dictionary<string, object> proc = GetLtvProcedure(parts[4]);
                if (proc == null)
                {
                    statusCode = 404;
                    return false;
                }

                response = proc;
                return true;
            }

            if (parts.Length == 6 && parts[0] == "api" && parts[1] == "v1" && parts[2] == "procedures" && parts[3] == "ltv" && parts[5] == "status")
            {
                Dictionary<string, object> status = GetLtvProcedureStatus(parts[4]);
                if (status == null)
                {
                    statusCode = 404;
                    return false;
                }

                response = status;
                return true;
            }

            statusCode = 404;
            return false;
        }

        private IEnumerator PollLoop()
        {
            WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.05f, pollIntervalSeconds));
            while (true)
            {
                Dictionary<string, object> evaRaw = _udp != null ? _udp.RequestJson(UdpGetEva) : null;
                Dictionary<string, object> ltvRaw = _udp != null ? _udp.RequestJson(UdpGetLtv) : null;
                Dictionary<string, object> ltvErrorsRaw = _udp != null ? _udp.RequestJson(UdpGetLtvErrors) : null;

                if (evaRaw != null)
                {
                    _eva = evaRaw;
                    EvaUpdated?.Invoke(GetEva());
                }

                if (ltvRaw != null)
                {
                    _ltv = ltvRaw;
                }

                if (ltvErrorsRaw != null)
                {
                    MergeLtvErrors(ltvErrorsRaw);
                }

                _sourceOnline = evaRaw != null && ltvRaw != null;
                if (_sourceOnline)
                {
                    _lastUpdatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                yield return wait;
            }
        }

        private void InitializeUdp()
        {
            if (_udp != null)
            {
                return;
            }

            try
            {
                _udp = new TssUdpClient(tssHost, tssPort, udpTimeoutMs, udpRetries);
            }
            catch (Exception e)
            {
                Debug.LogError($"TssUnityApiService UDP init failed: {e.Message}");
                _udp = null;
            }
        }

        private void LoadProcedures()
        {
            if (ltvProceduresJson == null)
            {
                ltvProceduresJson = Resources.Load<TextAsset>("ltv_procedures");
            }

            if (ltvProceduresJson == null || string.IsNullOrEmpty(ltvProceduresJson.text))
            {
                _procedures = new Dictionary<string, object>();
                return;
            }

            object parsed = MiniJson.Deserialize(ltvProceduresJson.text);
            _procedures = parsed as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private void MergeLtvErrors(Dictionary<string, object> ltvErrorsRaw)
        {
            if (_ltv == null)
            {
                _ltv = new Dictionary<string, object>();
            }

            foreach (KeyValuePair<string, object> kvp in ltvErrorsRaw)
            {
                _ltv[kvp.Key] = kvp.Value;
            }
        }

        private static string NormalizeEvaId(string evaId)
        {
            string value = (evaId ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "eva1" || value == "eva2")
            {
                return value;
            }

            throw new ArgumentException("evaId must be 'eva1' or 'eva2'");
        }

        private static bool ToBool(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is string str)
            {
                if (bool.TryParse(str, out bool parsedBool))
                {
                    return parsedBool;
                }

                if (double.TryParse(str, out double parsedNumber))
                {
                    return Math.Abs(parsedNumber) > double.Epsilon;
                }
            }

            if (value is IConvertible convertible)
            {
                try
                {
                    return Math.Abs(convertible.ToDouble(null)) > double.Epsilon;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static object GetPath(Dictionary<string, object> source, string path, object defaultValue = null)
        {
            object current = source;
            string[] parts = path.Split('.');

            for (int i = 0; i < parts.Length; i++)
            {
                Dictionary<string, object> dict = current as Dictionary<string, object>;
                if (dict == null || !dict.TryGetValue(parts[i], out current))
                {
                    return defaultValue;
                }
            }

            return current;
        }

        private static Dictionary<string, object> GetNestedDict(Dictionary<string, object> source, string key)
        {
            if (source != null && source.TryGetValue(key, out object found) && found is Dictionary<string, object> dict)
            {
                return DeepCopyDict(dict);
            }

            return new Dictionary<string, object>();
        }

        /// <summary>
        /// <see cref="ImageCarouselUI"/> expects <c>status.started</c>. Some TSS builds use other keys.
        /// </summary>
        private static Dictionary<string, object> NormalizeStatusSectionForUi(Dictionary<string, object> evaRoot)
        {
            Dictionary<string, object> st = GetNestedDict(evaRoot, "status");
            if (st.ContainsKey("started"))
            {
                return st;
            }

            foreach (string key in new[] { "mission_started", "running", "active", "missionStarted" })
            {
                if (st.TryGetValue(key, out object v))
                {
                    st["started"] = v;
                    break;
                }
            }

            return st;
        }

        /// <summary>
        /// UI expects flat UIA keys (<c>eva1_power</c>, <c>eva1_water_supply</c>, …) under <c>uia</c>.
        /// TSS often sends <c>uia.eva1.power</c> / <c>supply</c> / <c>waste</c> or aliases like <c>eva1_supply</c>.
        /// </summary>
        private static Dictionary<string, object> NormalizeUiaSectionForUi(Dictionary<string, object> evaRoot)
        {
            if (evaRoot == null || !evaRoot.TryGetValue("uia", out object uObj) || !(uObj is Dictionary<string, object> uiaRaw))
            {
                return new Dictionary<string, object>();
            }

            Dictionary<string, object> uia = DeepCopyDict(uiaRaw);

            static void EnsureCanonical(Dictionary<string, object> target, string canonical, Dictionary<string, object> src, params string[] keys)
            {
                if (target.ContainsKey(canonical))
                {
                    return;
                }

                foreach (string k in keys)
                {
                    if (src.TryGetValue(k, out object v) && v != null)
                    {
                        target[canonical] = v;
                        return;
                    }
                }
            }

            if (uia.TryGetValue("eva1", out object eva1Obj) && eva1Obj is Dictionary<string, object> eva1)
            {
                EnsureCanonical(uia, "eva1_power", eva1, "power", "emu_power", "emu1_power");
                EnsureCanonical(uia, "eva1_oxy", eva1, "oxy", "oxygen");
                EnsureCanonical(uia, "eva1_water_supply", eva1, "water_supply", "supply", "ev1_supply");
                EnsureCanonical(uia, "eva1_water_waste", eva1, "water_waste", "waste", "ev1_waste");
            }

            if (!uia.ContainsKey("eva1_water_supply") && uia.TryGetValue("eva1_supply", out object sup))
            {
                uia["eva1_water_supply"] = sup;
            }

            if (!uia.ContainsKey("eva1_water_waste") && uia.TryGetValue("eva1_waste", out object wast))
            {
                uia["eva1_water_waste"] = wast;
            }

            return uia;
        }

        private static List<object> GetNestedList(Dictionary<string, object> source, string key)
        {
            if (source != null && source.TryGetValue(key, out object found) && found is List<object> list)
            {
                return DeepCopyList(list);
            }

            return new List<object>();
        }

        private static Dictionary<string, object> DeepCopyDict(Dictionary<string, object> source)
        {
            if (source == null)
            {
                return new Dictionary<string, object>();
            }

            string json = MiniJson.Serialize(source);
            object parsed = MiniJson.Deserialize(json);
            return parsed as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static List<object> DeepCopyList(List<object> source)
        {
            if (source == null)
            {
                return new List<object>();
            }

            string json = MiniJson.Serialize(source);
            object parsed = MiniJson.Deserialize(json);
            return parsed as List<object> ?? new List<object>();
        }

        private static bool EvaluateCriterion(Dictionary<string, object> state, Dictionary<string, object> criterion)
        {
            object value = GetPath(state, Convert.ToString(GetPath(criterion, "path", string.Empty)), null);
            string op = Convert.ToString(GetPath(criterion, "op", "eq"));
            object target = GetPath(criterion, "value", null);

            switch (op)
            {
                case "eq":
                    return ValuesEqual(value, target);
                case "ne":
                    return !ValuesEqual(value, target);
                case "gt":
                    return CompareNumeric(value, target, out int gtResult) && gtResult > 0;
                case "gte":
                    return CompareNumeric(value, target, out int gteResult) && gteResult >= 0;
                case "lt":
                    return CompareNumeric(value, target, out int ltResult) && ltResult < 0;
                case "lte":
                    return CompareNumeric(value, target, out int lteResult) && lteResult <= 0;
                default:
                    return false;
            }
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (CompareNumeric(a, b, out int numericResult))
            {
                return numericResult == 0;
            }

            return string.Equals(Convert.ToString(a), Convert.ToString(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CompareNumeric(object a, object b, out int result)
        {
            result = 0;
            if (TryToDouble(a, out double da) && TryToDouble(b, out double db))
            {
                result = da.CompareTo(db);
                return true;
            }

            return false;
        }

        private static bool TryToDouble(object value, out double result)
        {
            result = 0.0d;
            if (value == null)
            {
                return false;
            }

            try
            {
                if (value is bool boolValue)
                {
                    result = boolValue ? 1.0d : 0.0d;
                    return true;
                }

                if (value is IConvertible convertible)
                {
                    result = convertible.ToDouble(null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
