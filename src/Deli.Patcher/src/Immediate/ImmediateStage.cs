using System.Collections.Generic;
using System.Reflection;
using Deli.VFS;

namespace Deli.Immediate
{
	/// <summary>
	///		An element of the loading sequence which uses immediate loaders. This class is not intended to be inherited outside the framework, so please don't.
	/// </summary>
	/// <typeparam name="TStage">A recursive generic to facilitate strongly typed stage parameters</typeparam>
	public abstract class ImmediateStage<TStage> : Stage<AssetLoader<TStage, Empty>> where TStage : ImmediateStage<TStage>
	{
#pragma warning disable CS1591

		protected abstract TStage GenericThis { get; }

		protected ImmediateStage(Blob data) : base(data)
		{
		}

		private void LoadMod(Mod mod, Dictionary<string, Mod> lookup)
		{
			var table = mod.Info.Assets;
			if (table is null) return;

			var assets = GetAssets(table);
			if (assets is null) return;

			Logger.LogInfo(Locale.LoadingAssets(mod));
			foreach (var asset in assets)
			{
				var loader = GetLoader(mod, asset, lookup, out var loaderMod);

				foreach (var handle in Glob(mod, asset))
				{
					try
					{
						loader(GenericThis, mod, handle);
					}
					catch
					{
						Logger.LogFatal(Locale.LoaderException(asset.Loader, loaderMod, mod, handle));
						throw;
					}
				}
			}
		}

		protected abstract Mod.Asset[]? GetAssets(Mod.AssetTable table);

		protected Empty AssemblyLoader(Stage stage, Mod mod, IHandle handle)
		{
			var assembly = Readers.Get<Assembly>()(AssemblyPreloader(handle));
			AssemblyLoader(stage, mod, assembly);

			return new();
		}

		protected IEnumerable<Mod> Run(IEnumerable<Mod> mods)
		{
			PreRun();

			var lookup = new Dictionary<string, Mod>();
			foreach (var mod in mods)
			{
				lookup.Add(mod.Info.Guid, mod);

				RunModules(mod);
				LoadMod(mod, lookup);

				yield return mod;
			}

			PostRun();
		}

#pragma warning restore CS1591
	}
}
