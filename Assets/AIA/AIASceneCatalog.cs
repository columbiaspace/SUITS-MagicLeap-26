using System.Collections.Generic;

public static class AIASceneCatalog
{
    public const string MainScene = "Main";
    public const string AiaScene = "AIA";
    public const string EgressScene = "Egress";
    public const string IngressScene = "Ingress";
    public const string LtvScene = "LTV";
    public const string MissionScene = "Mission";

    public const string EgressScenePath = "Assets/Scenes/final_scenes/Egress.unity";
    public const string IngressScenePath = "Assets/Scenes/final_scenes/Ingress.unity";
    public const string LtvScenePath = "Assets/Scenes/final_scenes/LTV.unity";
    public const string MissionScenePath = "Assets/Scenes/final_scenes/Mission.unity";

    private static readonly HashSet<string> SupportedScenes = new HashSet<string>
    {
        AiaScene,
        EgressScene,
        IngressScene,
        LtvScene,
        MissionScene
    };

    public static bool IsAiaEnabledScene(string sceneName)
    {
        return !string.IsNullOrWhiteSpace(sceneName) && SupportedScenes.Contains(sceneName);
    }

    public static string GetScenePath(string sceneName)
    {
        return sceneName switch
        {
            EgressScene => EgressScenePath,
            IngressScene => IngressScenePath,
            LtvScene => LtvScenePath,
            MissionScene => MissionScenePath,
            _ => sceneName
        };
    }
}
