using System.Collections.Generic;

namespace Deli.VFS
{
	/// <summary>
	///		Extension methods pertaining to <see cref="IDirectoryHandle"/>
	/// </summary>
	public static class ExtIDirectoryHandle
	{
		/// <summary>
		///		Gets the root directory of the VFS that the handle resides in. This could be the provided handle.
		/// </summary>
		public static IDirectoryHandle GetRoot(this IDirectoryHandle @this)
		{
			return @this is IChildHandle child ? child.GetRoot() : @this;
		}

		/// <summary>
		///		Gets a file contained by the directory
		/// </summary>
		/// <param name="this"></param>
		/// <param name="name">The name of the file</param>
		public static IFileHandle? GetFile(this IDirectoryHandle @this, string name)
		{
			return @this[name] as IFileHandle;
		}

		/// <summary>
		///		Gets a subdirectory contained by the directory
		/// </summary>
		/// <param name="this"></param>
		/// <param name="name">The name of the subdirectory</param>
		public static IChildDirectoryHandle? GetDirectory(this IDirectoryHandle @this, string name)
		{
			return @this[name] as IChildDirectoryHandle;
		}

		/// <summary>
		///		Enumerates over all of the children of the directory, recursively
		/// </summary>
		public static IEnumerable<IChildHandle> GetRecursive(this IDirectoryHandle @this)
		{
			foreach (var child in @this)
			{
				if (child is IDirectoryHandle subdirectory)
				{
					foreach (var subchild in subdirectory.GetRecursive())
					{
						yield return subchild;
					}
				}

				yield return child;
			}
		}

		/// <summary>
		///		Enumerates over all the subdirectories contained by the directory
		/// </summary>
		public static IEnumerable<IChildDirectoryHandle> GetDirectories(this IDirectoryHandle @this)
		{
			return @this.WhereCast<IChildHandle, IChildDirectoryHandle>();
		}

		/// <summary>
		///		Enumerates over all the subdirectories contained by the directory, recursively
		/// </summary>
		public static IEnumerable<IChildDirectoryHandle> GetDirectoriesRecursive(this IDirectoryHandle @this)
		{
			foreach (var directory in @this.GetDirectories())
			{
				yield return directory;

				foreach (var subdirectory in directory.GetDirectoriesRecursive())
				{
					yield return subdirectory;
				}
			}
		}

		/// <summary>
		///		Enumerates over all the files contained by the directory
		/// </summary>
		public static IEnumerable<IFileHandle> GetFiles(this IDirectoryHandle @this)
		{
			return @this.WhereCast<IChildHandle, IFileHandle>();
		}

		/// <summary>
		///		Enumerates over all the files contained by the directory, recursively
		/// </summary>
		public static IEnumerable<IFileHandle> GetFilesRecursive(this IDirectoryHandle @this)
		{
			foreach (var child in @this)
			{
				if (child is IDirectoryHandle subdirectory)
				{
					foreach (var subfile in subdirectory.GetFilesRecursive())
					{
						yield return subfile;
					}
				}

				if (child is IFileHandle file)
				{
					yield return file;
				}
			}
		}
	}
}
