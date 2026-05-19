using System;

/// <summary>
/// Maps procedure step labels to the correct DCU reference image.
/// </summary>
internal static class ProcedureDcuSpriteResolver
{
    internal struct Sprites
    {
        public UnityEngine.Sprite Panel;
        public UnityEngine.Sprite Oxy;
        public UnityEngine.Sprite Fan;
        public UnityEngine.Sprite Pump;
        public UnityEngine.Sprite Co2;
        public UnityEngine.Sprite BattLocalUmb;
        public UnityEngine.Sprite BattSecPri;
    }

    internal static UnityEngine.Sprite Resolve(string label, UnityEngine.Sprite current, Sprites sprites)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return current;
        }

        if (label.IndexOf("DCU", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return current;
        }

        if (sprites.Co2 != null && label.IndexOf("CO2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sprites.Co2;
        }

        if (label.IndexOf("BATT", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            bool isLocalOrUmb =
                label.IndexOf("UMB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("LOCAL", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSecOrPri =
                label.IndexOf("SEC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("PRI", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isLocalOrUmb && sprites.BattLocalUmb != null) return sprites.BattLocalUmb;
            if (isSecOrPri && sprites.BattSecPri != null) return sprites.BattSecPri;
        }

        if (sprites.Oxy != null &&
            label.IndexOf("OXY", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sprites.Oxy;
        }

        if (sprites.Fan != null &&
            label.IndexOf("FAN", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sprites.Fan;
        }

        if (sprites.Pump != null &&
            label.IndexOf("PUMP", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sprites.Pump;
        }

        if (sprites.Panel != null &&
            label.IndexOf("disconnect", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sprites.Panel;
        }

        if (sprites.Panel != null &&
            label.IndexOf("umbilical", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sprites.Panel;
        }

        return current;
    }
}
