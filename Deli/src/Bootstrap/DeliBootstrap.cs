﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADepIn;
using BepInEx.Configuration;
using BepInEx.Logging;
using Ionic.Zip;

namespace Deli
{
	internal class DeliBootstrap
	{
		private readonly ManualLogSource _log;
		private readonly IServiceKernel _kernel;
		private readonly DirectoryInfo _mods;
		private readonly DirectoryInfo _configs;

		public DeliBootstrap(ManualLogSource log, IServiceKernel kernel)
		{
			_log = log;
			_log.LogInfo($"Deli bootstrap has begun! Version {Constants.Version} ({Constants.GitBranch}-{Constants.GitDescribe})");

			_kernel = kernel;
			_mods = Directory.CreateDirectory(Constants.ModDirectory);
			_configs = Directory.CreateDirectory(Constants.ConfigDirectory);
		}

		private Option<Mod> CreateMod(IRawIO raw)
		{
			const string failurePrefix = "Failed to acquire the ";
			IResourceIO resources = new CachedResourceIO(new ResolverResourceIO(raw, _kernel));

			if (!resources.Get<Option<Mod.Manifest>>(Constants.ManifestFileName).MatchSome(out var infoOpt))
			{
				_log.LogError(failurePrefix + "manifest file");
			}
			else if (!infoOpt.MatchSome(out var info))
			{
				_log.LogError("Manifest file was invalid");
			}
			else if (!_kernel.Get<ConfigFile, string>(info.Guid).MatchSome(out var config))
			{
				_log.LogError(failurePrefix + "config file for " + info);
			}
			else if (!_kernel.Get<ManualLogSource, string>(info.Name.UnwrapOr(info.Guid)).MatchSome(out var log))
			{
				_log.LogError(failurePrefix + "log source for " + info);
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
		///		Enumerates the mods in the mods folder
		/// </summary>
		/// <returns>An enumerable of the mods in the mods folder</returns>
		private IEnumerable<Mod> DiscoverMods(DirectoryInfo dir)
		{
			void LogFailure(string type, object path)
			{
				_log.LogWarning("Failed to create mod from " + type + ": " + path);
			}

			void LogSuccess(string type, object path)
			{
				_log.LogDebug("Created mod from " + type + ": " + path);
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

			foreach (var archiveFile in Constants.ModExtensions.SelectMany(x => dir.GetFiles("*." + x)))
			{
				const string type = "archive";

				var raw = archiveFile.OpenRead();
				var zip = ZipFile.Read(raw);

				if (zip.Entries.Any(x => x.FileName.Contains('\\')))
				{
					_log.LogError($"Found a bad zip path in {archiveFile}. To fix it, try rezipping the archive or use a different zip utility.");

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

				LogSuccess(type, archiveFile);
				yield return mod;
			}

			foreach (var mod in dir.GetDirectories().SelectMany(DiscoverMods)) yield return mod;
		}

		public IEnumerable<Mod> CreateMods()
		{
			// Discover all the mods
			var mods = DiscoverMods(_mods).ToDictionary(x => x.Info.Guid, x => x);
			_log.LogInfo($"{mods.Count} mods to load");

			// Make sure all dependencies are satisfied
			if (!CheckDependencies(mods))
			{
				_log.LogError("One or more dependencies are not satisfied. Aborting initialization.");
				return Enumerable.Empty<Mod>();
			}

			// Sort the mods in the order they depend on each other
			var sorted = mods.Values.TSort(x => x.Info.Dependencies.Keys.Select(dep => mods[dep]), true);

			return sorted;
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
					_log.LogError($"Mod {mod} depends on {DepToString()}, but it is not installed!");
					return false;
				}

				// Check if the installed version satisfies the dependency request
				if (!resolved.Info.Version.Satisfies(dep.Value))
				{
					_log.LogError($"Mod {mod} depends on {DepToString()}, but version {resolved.Info.Version} is installed!");
					return false;
				}
			}

			return true;
		}
	}
}