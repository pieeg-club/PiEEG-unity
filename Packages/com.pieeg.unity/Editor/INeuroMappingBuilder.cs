using System.Collections.Generic;

namespace PiEEG.Unity.Editor
{
    /// <summary>Outcome of a "Build Mappings" run.</summary>
    public struct NeuroBuildResult
    {
        public bool Success;
        public string Message;
        public List<string> CreatedAssetPaths;

        public static NeuroBuildResult Fail(string message) =>
            new NeuroBuildResult { Success = false, Message = message };

        public static NeuroBuildResult Ok(string message, List<string> assets = null) =>
            new NeuroBuildResult { Success = true, Message = message, CreatedAssetPaths = assets };
    }

    /// <summary>
    /// Pluggable backend that turns a <see cref="NeuroBinder"/>'s routing table into a working
    /// avatar setup. The core editor (and its UI) has no compile-time dependency on the VRChat SDK;
    /// the VRChat implementation lives in a separate assembly that only compiles when the SDK and
    /// Modular Avatar are present, and registers itself via
    /// <see cref="NeuroMappingBuilderRegistry"/>.
    /// </summary>
    public interface INeuroMappingBuilder
    {
        /// <summary>Display name shown on the build button (e.g. "VRChat (Modular Avatar)").</summary>
        string DisplayName { get; }

        /// <summary>Validates that this binder can be built; returns false with a reason if not.</summary>
        bool CanBuild(NeuroBinder binder, out string reason);

        /// <summary>Generates parameters, clips, the blend-tree controller, and merge components.</summary>
        NeuroBuildResult Build(NeuroBinder binder);
    }

    /// <summary>
    /// Static registry the active <see cref="INeuroMappingBuilder"/> registers into. Keeps the core
    /// UI decoupled from the VRChat-specific assembly (one-way dependency).
    /// </summary>
    public static class NeuroMappingBuilderRegistry
    {
        /// <summary>The currently registered builder, or null if no SDK backend is installed.</summary>
        public static INeuroMappingBuilder Active { get; private set; }

        public static void Register(INeuroMappingBuilder builder) => Active = builder;

        public static bool HasBuilder => Active != null;
    }
}
