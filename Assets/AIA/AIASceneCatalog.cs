using System.Collections.Generic;

public static class AIASceneCatalog
{
    public const string MainScene = "Main";
    public const string AiaScene = "AIA";
    public const string IngressScene = "InEgress";
    public const string LtvScene = "LTV";
    public const string MissionScene = "MIssion";

    private static readonly HashSet<string> SupportedScenes = new HashSet<string>
    {
        MainScene,
        AiaScene,
        IngressScene,
        LtvScene,
        MissionScene
    };

    public static bool IsAiaEnabledScene(string sceneName)
    {
        return !string.IsNullOrWhiteSpace(sceneName) && SupportedScenes.Contains(sceneName);
    }
}
