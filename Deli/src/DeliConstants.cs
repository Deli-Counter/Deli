using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Deli
{
	// Constants beginning with MACRO are set via the "macros-precompile" recipe
	public static class DeliConstants
	{
		// GUID and Version
		public const string Name = "Deli";
		public const string Guid = "nrgill28.deli";
		public const string Version = "MACRO_VERSION";

		// Git
		public const string GitDescribe = "MACRO_GIT_DESCRIBE";
		public const string GitBranch = "MACRO_GIT_BRANCH";
		public const string GitHash = "MACRO_GIT_HASH";

		// Loader constants
		public const string AssemblyLoaderName = "assembly";
		public static IEnumerable<string> ModExtensions { get; } = new ReadOnlyCollection<string>(new []{"zip", "deli"});
		public const string ModDirectory = "mods";
		public const string ConfigDirectory = "mods/configs";
		public const string ManifestFileName = "manifest.json";
	}
}