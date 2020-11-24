﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ADepIn;
using ADepIn.Fluent;
using ADepIn.Impl;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Ionic.Zip;
using UnityEngine;
using Valve.Newtonsoft.Json;
using Valve.Newtonsoft.Json.Linq;
using Valve.Newtonsoft.Json.Serialization;

namespace Deli
{
    [BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
    public class Deli : BaseUnityPlugin
    {
        private static StandardServiceKernel _kernel;

        private static IServiceKernel Kernel => _kernel ?? (_kernel = new StandardServiceKernel());
        public static IServiceResolver Services => Kernel;

        private ConfigEntry<bool> WaitForDebugger;

        private void Awake()
        {
            Logger.LogInfo($"Deli is Awake! Version {Constants.Version} ({Constants.GitBranch}-{Constants.GitDescribe})");
            Bind();
            RegisterConfig();
            if (WaitForDebugger.Value) StartCoroutine(WaitForKeypress());
            else Initialize();
        }

        private void OnDestroy()
        {
            var disposables = Services.Get<IList<IDisposable>>().Expect("Could not find disposables.");

            foreach (var disposable in disposables) disposable.Dispose();
        }

        private void Bind()
        {
            // Cleanup
            Kernel.Bind<IList<IDisposable>>().ToConstant(new List<IDisposable>());

            // Simple constants
            Kernel.Bind<Deli>().ToConstant(this);
            Kernel.Bind<ManualLogSource>().ToConstant(Logger);
            {
                var manager = new GameObject("Deli Manager");
                DontDestroyOnLoad(manager);

                Kernel.Bind<GameObject>().ToConstant(manager);
            }

            // JSON
            Kernel.Bind<NamingStrategy>().ToConstant(new CamelCaseNamingStrategy());
            Kernel.Bind<IContractResolver>().ToRecursiveNopMethod(x => new DefaultContractResolver
            {
                NamingStrategy = x.Get<NamingStrategy>().Expect("JSON naming strategy not found")
            }).InSingletonNopScope();
            Kernel.Bind<IList<JsonConverter>>().ToConstant(new List<JsonConverter>
            {
                new OptionJsonConverter()
            });
            Kernel.Bind<JsonSerializerSettings>().ToRecursiveNopMethod(x => new JsonSerializerSettings
            {
                ContractResolver = x.Get<IContractResolver>().Expect("JSON contract resolver not found"),
                Converters = x.Get<IList<JsonConverter>>().Expect("JSON converters not found")
            }).InSingletonNopScope();
            Kernel.Bind<JsonSerializer>().ToRecursiveNopMethod(x =>
            {
                var settings = x.Get<JsonSerializerSettings>().Expect("JSON settings not found.");

                return JsonSerializer.Create(settings);
            }).InSingletonNopScope();
            Kernel.BindJson<Mod.Manifest>();

            // Basic impls
            Kernel.Bind<IAssetReader<Assembly>>().ToConstant(new AssemblyAssetReader());
            Kernel.Bind<IAssetReader<Option<JObject>>>().ToRecursiveNopMethod(x => new JObjectAssetReader(x)).InSingletonNopScope();
            Kernel.Bind<IAssetReader<byte[]>>().ToConstant(new ByteArrayAssetReader());

            // Associative services dictionaries
            Kernel.Bind<IDictionary<string, IAssetLoader>>().ToConstant(new Dictionary<string, IAssetLoader>
            {
                ["assembly"] = new AssemblyAssetLoader()
            });
            Kernel.Bind<IDictionary<string, Mod>>().ToConstant(new Dictionary<string, Mod>());
            Kernel.Bind<IDictionary<Type, Mod>>().ToConstant(new Dictionary<Type, Mod>());

            // Enumerables
            Kernel.Bind<IEnumerable<IAssetLoader>>().ToRecursiveMethod(x => x.Get<IDictionary<string, IAssetLoader>>().Map(v => (IEnumerable<IAssetLoader>) v.Values)).InTransientScope();
            Kernel.Bind<IEnumerable<Mod>>().ToRecursiveMethod(x => x.Get<IDictionary<string, Mod>>().Map(v => (IEnumerable<Mod>) v.Values)).InTransientScope();

            // Contextual to dictionaries
            Kernel.Bind<IAssetLoader, string>().ToWholeMethod((services, context) => services.Get<IDictionary<string, IAssetLoader>>().Map(x => x.OptionGetValue(context)).Flatten()).InTransientScope();
            Kernel.Bind<Mod, string>().ToWholeMethod((services, context) => services.Get<IDictionary<string, Mod>>().Map(x => x.OptionGetValue(context)).Flatten()).InTransientScope();
            Kernel.Bind<Mod, Type>().ToWholeMethod((services, context) => services.Get<IDictionary<Type, Mod>>().Map(x => x.OptionGetValue(context)).Flatten()).InTransientScope();
            Kernel.Bind<Mod, DeliMod>().ToWholeMethod((services, context) => services.Get<Mod, Type>(context.GetType())).InTransientScope();

            // Custom impls
            Kernel.Bind<ManualLogSource, string>().ToContextualNopMethod(x => BepInEx.Logging.Logger.CreateLogSource(x)).InSingletonScope();
            Kernel.Bind<ConfigFile, string>().ToContextualNopMethod(x => new ConfigFile(Path.Combine(Constants.ConfigDirectory, $"{x}.cfg"), false)).InSingletonScope();
        }

        private void RegisterConfig()
        {
            WaitForDebugger = Config.Bind("Debugging", "WaitForDebugger", false, "If set to true, this will delay initializing the framework until you press the R key to give you time to attach a debugger.");
        }

        // Small coroutine to wait for a keypress before initializing.
        private IEnumerator WaitForKeypress()
        {
            while (!Input.GetKeyDown(KeyCode.R)) yield return null;
            Initialize();
        }

        private Option<Mod> CreateMod(IRawIO raw)
        {
            const string prefix = "Failed to acquire the ";
            IResourceIO resources = new CachedResourceIO(new ResolverResourceIO(raw, Services));

            if (!resources.Get<Option<Mod.Manifest>>(Constants.ManifestFileName).Flatten().MatchSome(out var info))
            {
                Logger.LogWarning(prefix + "manifest file");
            }
            else if (!Services.Get<ConfigFile, string>(info.Guid).MatchSome(out var config))
            {
                Logger.LogWarning(prefix + "config file for " + info);
            }
            else if (!Services.Get<ManualLogSource, string>(info.Name.UnwrapOr(info.Guid)).MatchSome(out var log))
            {
                Logger.LogWarning(prefix + "log source for " + info);
            }
            else
            {
                resources = new LoggedModIO(log, resources);
                var mod = new Mod(info, resources, config, log);

                return Option.Some(mod);
            }

            return Option.None<Mod>();
        }

        /// <summary>
        ///     Enumerates the mods in the mods folder
        /// </summary>
        /// <returns>An enumerable of the mods in the mods folder</returns>
        private IEnumerable<Mod> DiscoverMods(DirectoryInfo dir)
        {
            void LogFailure(string type, object path)
            {
                Logger.LogWarning("Failed to create mod from " + type + ": " + path);
            }

            void LogSuccess(string type, object path)
            {
                Logger.LogDebug("Created mod from " + type + ": " + path);
            }

            var manifestPath = Path.Combine(dir.FullName, Constants.ManifestFileName);
            if (File.Exists(manifestPath)) // Directory mod
            {
                const string type = "directory";

                var io = new DirectoryRawIO(dir);

                if (CreateMod(io).MatchSome(out var mod))
                {
                    LogSuccess(type, dir);
                    yield return mod;
                }
                else
                {
                    LogFailure(type, dir);
                }

                // Halt discovery in this directory
                // Used because non-Deli *.zip and manifest.json files would be misinterpretted.
                yield break;
            }

            foreach (var archiveFile in dir.GetFiles("*." + Constants.ModExtension))
            {
                const string type = "archive";

                var raw = archiveFile.OpenRead();
                var zip = ZipFile.Read(raw);

                if (zip.Entries.Any(x => x.FileName.Contains('\\')))
                {
                    Logger.LogError($"Found a bad zip path in {archiveFile}. To fix it, try rezipping the archive or use a different zip utility.");

                    zip.Dispose();
                    raw.Dispose();
                    continue;
                }

                var io = new ArchiveRawIO(zip);

                if (!CreateMod(io).MatchSome(out var mod))
                {
                    LogFailure(type, archiveFile);

                    zip.Dispose();
                    raw.Dispose();
                    continue;
                }

                var disposables = Kernel.Get<IList<IDisposable>>().Unwrap();
                disposables.Add(zip);
                disposables.Add(raw);

                LogSuccess(type, archiveFile);
                yield return mod;
            }

            foreach (var mod in dir.GetDirectories().SelectMany(DiscoverMods)) yield return mod;
        }

        private void Initialize()
        {
            EnsureDirectoriesExist();

            // Discover all the mods
            var modsDir = new DirectoryInfo(Constants.ModDirectory);
            var mods = DiscoverMods(modsDir).ToDictionary(x => x.Info.Guid, x => x);
            Logger.LogInfo($"{mods.Count} mods to load");

            // Make sure all dependencies are satisfied
            if (!CheckDependencies(mods))
            {
                Logger.LogError("One or more dependencies are not satisfied. Aborting initialization.");
                return;
            }

            // Sort the mods in the order they depend on each other
            var sorted = mods.Values.TSort(x => x.Info.Dependencies.Keys.Select(dep => mods[dep]), true);

            // Load the mods
            foreach (var mod in sorted)
                try
                {
                    LoadMod(mod);
                }
                catch (Exception e)
                {
                    Logger.LogError($"Failed to load mod {mod}. No additional mods will be loaded.\nException: " + e);
                    break;
                }

            Logger.LogInfo("Mod loading complete");
        }

        private bool CheckDependencies(Dictionary<string, Mod> mods)
        {
            foreach (var mod in mods.Values)
            foreach (var dep in mod.Info.Dependencies)
            {
                string DepToString()
                {
                    return $"{dep.Key} @ {dep.Value}";
                }

                // Try finding the installed dependency
                if (!mods.TryGetValue(dep.Key, out var resolved))
                {
                    Logger.LogError($"Mod {mod} depends on {DepToString()}, but it is not installed!");
                    return false;
                }

                // Check if the installed version satisfies the dependency request
                if (!resolved.Info.Version.Satisfies(dep.Value))
                {
                    Logger.LogError($"Mod {mod} depends on {DepToString()}, but version {resolved.Info.Version} is installed!");
                    return false;
                }
            }

            return true;
        }

        private static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(Constants.ModDirectory);
            Directory.CreateDirectory(Constants.ConfigDirectory);
        }

        private void LoadMod(Mod mod)
        {
            Logger.LogInfo("Loading " + mod);

            // For each asset inside the mod, load it
            foreach (var asset in mod.Info.Assets)
            {
                var assetPath = asset.Key;
                var assetLoader = asset.Value;

                if (!Services.Get<IAssetLoader, string>(assetLoader).MatchSome(out var loader))
                    // Throw instead of skip, because this might be a critical part of the mod
                    throw new InvalidOperationException("Asset loader not found: " + assetLoader);

                Logger.LogDebug($"Loading asset [{assetLoader}: {assetPath}]");
                loader.LoadAsset(_kernel, mod, assetPath);
            }

            // Add the Mod to the kernel.
            Services.Get<IDictionary<string, Mod>>().Unwrap().Add(mod.Info.Guid, mod);
        }
    }
}