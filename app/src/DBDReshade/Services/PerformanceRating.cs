namespace DBDReshade.Services;

public enum PerformanceLevel { Light, Medium, Heavy }

/// <summary>How much a preset is likely to cost in FPS, and which of its effects weigh the most.</summary>
public sealed record PerformanceInfo(PerformanceLevel Level, IReadOnlyList<string> HeaviestEffects);

/// <summary>
/// Estimates a preset's performance cost from the effects it turns on (its "Techniques=" line).
/// Colour and sharpening effects cost almost nothing; ambient occlusion, global illumination, depth of
/// field and bloom are what drop the frame rate. It's an estimate, not a benchmark.
/// </summary>
public static class PerformanceRating
{
    /// <summary>Cost of the effects known to be expensive; anything else counts as 1 (cheap).</summary>
    private static readonly Dictionary<string, int> Weights = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ambient occlusion, global illumination, reflections, depth of field: heavy.
        ["RadiantGI.fx"] = 6, ["MXAO.fx"] = 6, ["qUINT_mxao.fx"] = 6, ["qUINT_ssr.fx"] = 6, ["qUINT_rtgi.fx"] = 6,
        ["SSDO.fx"] = 6, ["PPFX_SSDO.fx"] = 6, ["CinematicDOF.fx"] = 6, ["qUINT_dof.fx"] = 6, ["ADOF.fx"] = 6,
        ["NGLighting.fx"] = 6, ["MartysMods_MXAO.fx"] = 6, ["MartysMods_RTGI.fx"] = 6, ["DOF.fx"] = 6,
        // Noticeable.
        ["SuperDepth3D.fx"] = 4, ["Gravity.fx"] = 3, ["computeGravity.fx"] = 3, ["BloomingHDR.fx"] = 3,
        ["MagicBloom.fx"] = 3, ["AmbientLight.fx"] = 3, ["Bloom.fx"] = 3, ["qUINT_bloom.fx"] = 3,
        // A little more than a colour filter.
        ["Clarity.fx"] = 2, ["Dehaze.fx"] = 2, ["SMAA.fx"] = 2, ["Glamarye_Fast_Effects.fx"] = 2,
        ["PD80_01B_RT_Correct_Color.fx"] = 2,
    };

    private static readonly Dictionary<string, PerformanceInfo> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static PerformanceInfo Rate(string presetFile)
    {
        lock (Cache)
        {
            var stamp = $"{presetFile}|{(File.Exists(presetFile) ? File.GetLastWriteTimeUtc(presetFile).Ticks : 0)}";
            if (Cache.TryGetValue(stamp, out var cached)) return cached;

            var effects = File.Exists(presetFile) ? ShaderService.EffectFilesOf(presetFile) : new HashSet<string>();
            var weights = effects.Select(fx => (Fx: fx, Weight: Weights.TryGetValue(fx, out var w) ? w : 1)).ToList();
            var total = weights.Sum(x => x.Weight);
            var max = weights.Count == 0 ? 0 : weights.Max(x => x.Weight);

            var level = max >= 6 || total >= 14 ? PerformanceLevel.Heavy
                : max >= 3 || total >= 8 ? PerformanceLevel.Medium
                : PerformanceLevel.Light;
            var heaviest = weights.Where(x => x.Weight >= 2).OrderByDescending(x => x.Weight)
                .Select(x => Path.GetFileNameWithoutExtension(x.Fx)).Take(4).ToList();

            return Cache[stamp] = new PerformanceInfo(level, heaviest);
        }
    }
}
