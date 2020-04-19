using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FSO
{
	public enum EntryType
	{
		File,
		Directory
	}
	public class FileSystemObserver : IDisposable, ISupportInitialize
	{
		// Fields
		/// <summary>
		/// Thé underlying FileSystemWatcher component.
		/// </summary>
		private FileSystemWatcher _fileSystemWatcher;
		/// <summary>
		/// The file extensions to monitor.
		/// </summary>
		private string[] _fileExts;
		/// <summary>
		/// A flag whether a predicate should be called or not, to determine if an event for a file/directory should be raised.
		/// </summary>
		private bool checkForCondition = false;
		/// <summary>
		/// The predicate which will be called on the full path of the affected file/directory.
		/// </summary>
		private Func<EntryType, string, bool> _raiseEventCondition;
		/// <summary>
		/// A dictionary to store the paths of monitored files.
		/// </summary>
		private Dictionary<int, HashSet<string>> _files;
		/// <summary>
		/// A dictionary to store the paths of monitored directories.
		/// </summary>
		private Dictionary<int, HashSet<string>> _dirs;

		// Properties
		public string ObservedPath { get { return _fileSystemWatcher.Path; } set { _fileSystemWatcher.Path = value; GetFileSystemEntries(); } }
		public bool IncludeSubdirectories { get { return _fileSystemWatcher.IncludeSubdirectories; } set { _fileSystemWatcher.IncludeSubdirectories = value; GetFileSystemEntries(); } }
		public int InternalBufferSize { get { return _fileSystemWatcher.InternalBufferSize; } set { _fileSystemWatcher.InternalBufferSize = value; } }
		public string Filter { get { return _fileSystemWatcher.Filter; } set { _fileSystemWatcher.Filter = value; } }
		public bool EnableRaisingEvents { get { return _fileSystemWatcher.EnableRaisingEvents; } set { _fileSystemWatcher.EnableRaisingEvents = value; } }
		public NotifyFilters NotifyFilter { get { return _fileSystemWatcher.NotifyFilter; } set { _fileSystemWatcher.NotifyFilter = value; } }
		public ISynchronizeInvoke SynchronizingObject { get { return _fileSystemWatcher.SynchronizingObject; } set { _fileSystemWatcher.SynchronizingObject = value; } }
		public ISite Site { get { return _fileSystemWatcher.Site; } set { _fileSystemWatcher.Site = value; } }

		// Constructors
		public FileSystemObserver(Func<EntryType, string, bool> raiseEventCondition = null, params string[] fileExts)
		{
			_fileSystemWatcher = new FileSystemWatcher();
			Initialize(raiseEventCondition, fileExts);
		}
		public FileSystemObserver(string path, Func<EntryType, string, bool> raiseEventCondition = null, params string[] fileExts)
		{
			_fileSystemWatcher = new FileSystemWatcher(path);
			Initialize(raiseEventCondition, fileExts);
		}
		public FileSystemObserver(string path, string filter, Func<EntryType, string, bool> raiseEventCondition = null, params string[] fileExts)
		{
			_fileSystemWatcher = new FileSystemWatcher(path, filter);
			Initialize(raiseEventCondition, fileExts);
		}

		// Events
		public event FileSystemEventHandler Deleted;
		public event FileSystemEventHandler Created;
		public event FileSystemEventHandler Changed;
		public event RenamedEventHandler Renamed;
		public event ErrorEventHandler Error;

		// Methods
		public void Dispose()
		{
			_fileSystemWatcher.Dispose();
		}
		public void BeginInit()
		{
			_fileSystemWatcher.BeginInit();
		}
		public void EndInit()
		{
			_fileSystemWatcher.EndInit();
		}
		public WaitForChangedResult WaitForChanged(WatcherChangeTypes changeType)
		{
			return _fileSystemWatcher.WaitForChanged(changeType);
		}
		public WaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, int timeout)
		{
			return _fileSystemWatcher.WaitForChanged(changeType, timeout);
		}

		private void OnError(object sender, ErrorEventArgs e)
		{
			Error?.Invoke(this, e);
		}
		private void OnChanged(object sender, FileSystemEventArgs e)
		{
			if (IsFile(e.FullPath))
			{
				// Raise a changed event on the file if applicable.
				RaiseChangedEventFor(EntryType.File, e.FullPath);
			}
			else if (IsDirectory(e.FullPath))
			{
				// Raise a changed event on the directory if applicable.
				RaiseChangedEventFor(EntryType.Directory, e.FullPath);
			}

		}
		private void OnRenamed(object sender, RenamedEventArgs e)
		{
			if (IsFile(e.OldFullPath) || File.Exists(e.FullPath))
			{
				// Update file entry.
				RemoveEntry(_files, e.OldFullPath);
				AddEntry(_files, e.FullPath);

				// Raise a renamed event on the file if applicable.
				RaiseRenamedEventFor(EntryType.File, e.OldFullPath, e.FullPath);
			}
			else if (IsDirectory(e.OldFullPath) || Directory.Exists(e.FullPath))
			{
				// Update directory entry.
				RemoveEntry(_dirs, e.OldFullPath);
				AddEntry(_dirs, e.FullPath);

				// Raise a renamed event on the directory if applicable.
				RaiseRenamedEventFor(EntryType.Directory, e.OldFullPath, e.FullPath);

				// Get renamed files directly within the directory.
				var renamedFiles = GetRenamedEntries(_files, e.OldFullPath, e.FullPath, false);
				// Update renamed files entries, which are directly within the directory.
				RemoveEntries(_files, renamedFiles.Keys);
				AddEntries(_files, renamedFiles.Values);

				// Get all renamed subdirectories.
				var renamedSubDirs = GetRenamedEntries(_dirs, e.OldFullPath, e.FullPath);

				if (IncludeSubdirectories)
				{
					// Raise renamed events on the files if applicable.
					RaiseRenamedEventsFor(EntryType.File, renamedFiles);
					foreach (var dir in renamedSubDirs)
					{
						// Update renamed subdirectory entry.
						RemoveEntry(_dirs, dir.Key);
						AddEntry(_dirs, dir.Value);
						// Raise a renamed event on the subdirectory if applicable.
						RaiseRenamedEventFor(EntryType.Directory, dir.Key, dir.Value);

						// Get renamed files directly within the subdirectory.
						var renamedSubDirFiles = GetRenamedEntries(_files, dir.Key, dir.Value, false);
						// Update renamed files entries.
						RemoveEntries(_files, renamedSubDirFiles.Keys);
						AddEntries(_files, renamedSubDirFiles.Values);
						// Raise renamed events on the files if applicable.
						RaiseRenamedEventsFor(EntryType.File, renamedSubDirFiles);
					}

				}
				else
				{
					// Update renamed subdirectories entries.
					RemoveEntries(_dirs, renamedSubDirs.Keys);
					AddEntries(_dirs, renamedSubDirs.Values);

					// Get all renamed files.
					var allRenamedFiles = GetRenamedEntries(_files, e.OldFullPath, e.FullPath);
					// Update all renamed files entries.
					RemoveEntries(_files, allRenamedFiles.Keys);
					AddEntries(_files, allRenamedFiles.Values);
				}
			}
		}
		private void OnCreated(object sender, FileSystemEventArgs e)
		{
			if (IsFile(e.FullPath))
			{
				// Add file entry.
				AddEntry(_files, e.FullPath);
				// Raise a created event on the created file if applicable.
				RaiseCreatedEventFor(EntryType.File, e.FullPath);
			}
			else if (IsDirectory(e.FullPath))
			{
				// Add directory entry.
				AddEntry(_dirs, e.FullPath);
				// Raise a created event on the created directory if applicable.
				RaiseCreatedEventFor(EntryType.Directory, e.FullPath);

				if (IncludeSubdirectories)
				{
					// Get created files directly within the created directory.
					var createdFiles = EnumerateFiles(e.FullPath, SearchOption.TopDirectoryOnly, _fileExts);
					// Add created files entries.
					AddEntries(_files, createdFiles);
					// Raise a created event on the created files if applicable.
					RaiseCreatedEventsFor(EntryType.File, createdFiles);

					// Get created subdirectories.
					var createdSubDirs = Directory.EnumerateDirectories(e.FullPath, "*.*", GetSearchOption()).OrderBy(path => path.Length);
					foreach (var dir in createdSubDirs)
					{
						// Add subdirectory entry.
						AddEntry(_dirs, dir);
						// Raise a created event on the created subdirectory if applicable.
						RaiseCreatedEventFor(EntryType.Directory, dir);

						// Get created files directly within the created subdirectory.
						var createdSubDirFiles = EnumerateFiles(dir, SearchOption.TopDirectoryOnly, _fileExts);
						// Add created files entries.
						AddEntries(_files, createdSubDirFiles);
						// Raise a created event on the created files if applicable.
						RaiseCreatedEventsFor(EntryType.File, createdSubDirFiles);
					}
				}
			}
		}
		private void OnDeleted(object sender, FileSystemEventArgs e)
		{
			if (IsFile(e.FullPath))
			{
				// Remove file entry.
				RemoveEntry(_files, e.FullPath);
				// Raise a deleted event on the file if applicable.
				RaiseDeletedEventFor(EntryType.File, e.FullPath);
			}
			else if (IsDirectory(e.FullPath))
			{
				// Remove directory entry.
				RemoveEntry(_dirs, e.FullPath);
				// Raise a deleted event on the directory if applicable.
				RaiseDeletedEventFor(EntryType.Directory, e.FullPath);

				// Get deleted files directly within the directory.
				var deletedFiles = GetDeletedEntries(_files, e.FullPath, false).ToArray();

				// Get all deleted subdirectories.
				var deletedSubDirs = GetDeletedEntries(_dirs, e.FullPath).ToArray();

				// Remove deleted files entries.
				RemoveEntries(_files, deletedFiles);

				if (IncludeSubdirectories)
				{
					// Raise a deleted event on the files if applicable.
					RaiseDeletedEventsFor(EntryType.File, deletedFiles);
					foreach (var dir in deletedSubDirs)
					{

						// Remove deleted subdirectory entry.
						RemoveEntry(_dirs, dir);
						// Raise a deleted event on the subdirectory if applicable.
						RaiseDeletedEventFor(EntryType.Directory, dir);

						// Get deleted files directly within the subdirectory.
						var deletedSubDirFiles = GetDeletedEntries(_files, dir, false).ToArray();
						// Remove deleted files entries.
						RemoveEntries(_files, deletedSubDirFiles);
						// Raise a deleted event on the files if applicable.
						RaiseDeletedEventsFor(EntryType.File, deletedSubDirFiles);
					}
				}
				else
				{
					// Remove deleted directories entries.
					RemoveEntries(_dirs, deletedSubDirs);
					// Get all deleted files.
					var allDeletedFiles = GetDeletedEntries(_files, e.FullPath).ToArray();
					// Remove all deleted files entries.
					RemoveEntries(_files, allDeletedFiles);
				}
			}
		}

		#region Initialize methods
		private void Initialize(Func<EntryType, string, bool> raiseEventsCondition, string[] fileExts)
		{
			if (raiseEventsCondition != null)
			{
				_raiseEventCondition = raiseEventsCondition;
				checkForCondition = true;
			}
			_fileExts = fileExts?.Length > 0 ? fileExts : null;

			AddEventHandlers();
			GetFileSystemEntries();
		}
		private void AddEventHandlers()
		{
			_fileSystemWatcher.Changed += OnChanged;
			_fileSystemWatcher.Created += OnCreated;
			_fileSystemWatcher.Deleted += OnDeleted;
			_fileSystemWatcher.Renamed += OnRenamed;
			_fileSystemWatcher.Error += OnError;
		}
		private void GetFileSystemEntries()
		{
			_files = new Dictionary<int, HashSet<string>>();
			_dirs = new Dictionary<int, HashSet<string>>();
			if (!string.IsNullOrWhiteSpace(ObservedPath))
			{
				// Get files
				var files = EnumerateFiles(ObservedPath, GetSearchOption(), _fileExts);
				foreach (var file in files)
				{
					AddEntry(_files, file);
				}

				// Get directories
				var dirs = Directory.EnumerateDirectories(ObservedPath, "*.*", GetSearchOption());
				foreach (var dir in dirs)
				{
					AddEntry(_dirs, dir);
				}
			}
		}
		#endregion

		#region Raise events methods
		// Renamed event
		private void RaiseRenamedEventsFor(EntryType entriesType, Dictionary<string, string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseRenamedEventFor(entriesType, entry.Key, entry.Value);
			}
		}
		private void RaiseRenamedEventFor(EntryType entryType, string oldPath, string newPath)
		{
			if (checkForCondition && _raiseEventCondition != null)
			{
				if (_raiseEventCondition(entryType, oldPath))
				{
					string oldName = GetRelativePath(ObservedPath, oldPath);
					string name = GetRelativePath(ObservedPath, newPath);
					var eventArgs = new RenamedEventArgs(WatcherChangeTypes.Renamed, ObservedPath, name, oldName);
					Renamed?.Invoke(this, eventArgs);
				}
			}
			else
			{
				string oldName = GetRelativePath(ObservedPath, oldPath);
				string name = GetRelativePath(ObservedPath, newPath);
				var eventArgs = new RenamedEventArgs(WatcherChangeTypes.Renamed, ObservedPath, name, oldName);
				Renamed?.Invoke(this, eventArgs);
			}
		}
		// Created event
		private void RaiseCreatedEventsFor(EntryType entriesType, IEnumerable<string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseCreatedEventFor(entriesType, entry);
			}
		}
		private void RaiseCreatedEventFor(EntryType entryType, string entry)
		{
			if (checkForCondition && _raiseEventCondition != null)
			{
				if (_raiseEventCondition(entryType, entry))
				{
					string name = GetRelativePath(ObservedPath, entry);
					Created?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Created, ObservedPath, name));
				}
			}
			else
			{
				string name = GetRelativePath(ObservedPath, entry);
				Created?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Created, ObservedPath, name));
			}
		}
		// Deleted event
		private void RaiseDeletedEventsFor(EntryType entriesType, IEnumerable<string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseDeletedEventFor(entriesType, entry);
			}
		}
		private void RaiseDeletedEventFor(EntryType entryType, string entry)
		{
			if (checkForCondition && _raiseEventCondition != null)
			{
				if (_raiseEventCondition(entryType, entry))
				{
					string name = GetRelativePath(ObservedPath, entry);
					Deleted?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Deleted, ObservedPath, name));
				}
			}
			else
			{
				string name = GetRelativePath(ObservedPath, entry);
				Deleted?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Deleted, ObservedPath, name));
			}
		}
		// Changed event
		private void RaiseChangedEventsFor(EntryType entriesType, IEnumerable<string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseChangedEventFor(entriesType, entry);
			}
		}
		private void RaiseChangedEventFor(EntryType entryType, string entry)
		{
			if (checkForCondition && _raiseEventCondition != null)
			{
				if (_raiseEventCondition(entryType, entry))
				{
					string name = GetRelativePath(ObservedPath, entry);
					Changed?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Changed, ObservedPath, name));
				}
			}
			else
			{
				string name = GetRelativePath(ObservedPath, entry);
				Changed?.Invoke(this, new FileSystemEventArgs(WatcherChangeTypes.Changed, ObservedPath, name));
			}
		}
		#endregion

		#region Helpers
		public IEnumerable<string> EnumerateFiles(string path, SearchOption searchOption, params string[] fileExtensions)
		{
			if (fileExtensions?.Length > 0)
			{
				return EnumerateSpecificFiles(path, searchOption, fileExtensions);
			}
			else
			{
				return EnumerateAllFiles(path, searchOption);
			}
		}
		public IEnumerable<string> EnumerateSpecificFiles(string path, SearchOption searchOption, params string[] fileExtensions)
		{
			IEnumerable<string> files = Directory.EnumerateFiles(path, "*.*", searchOption);
			foreach (string file in files)
			{
				string extension = Path.GetExtension(file);
				if (fileExtensions.Contains(extension, StringComparer.InvariantCultureIgnoreCase))
				{
					yield return file;
				}
			}
		}
		public IEnumerable<string> EnumerateAllFiles(string path, SearchOption searchOption)
		{
			return Directory.EnumerateFiles(path, "*.*", searchOption);
		}
		private bool IsFile(string path)
		{
			bool output = false;
			int pathDepth = GetDepth(path);
			_files.TryGetValue(pathDepth, out HashSet<string> set);
			if (set != null)
			{
				output = set.Contains(path);
			}
			return output || File.Exists(path);
		}
		private bool IsDirectory(string path)
		{
			bool output = false;
			int pathDepth = GetDepth(path);
			_dirs.TryGetValue(pathDepth, out HashSet<string> set);
			if (set != null)
			{
				output = set.Contains(path);
			}
			return output || Directory.Exists(path);
		}
		private void AddEntry(Dictionary<int, HashSet<string>> dict, string path)
		{
			int key = GetDepth(path);
			if (dict.ContainsKey(key))
			{
				dict[key].Add(path);
			}
			else
			{
				dict[key] = new HashSet<string> { path };
			}
		}
		private void AddEntries(Dictionary<int, HashSet<string>> dict, IEnumerable<string> entries)
		{
			foreach (var entry in entries)
			{
				AddEntry(dict, entry);
			}
		}
		private void RemoveEntry(Dictionary<int, HashSet<string>> dict, string path)
		{
			int key = GetDepth(path);
			if (dict.ContainsKey(key))
			{
				dict[key].Remove(path);
			}
		}
		private void RemoveEntries(Dictionary<int, HashSet<string>> dict, IEnumerable<string> entries)
		{
			foreach (var entry in entries)
			{
				RemoveEntry(dict, entry);
			}
		}
		private int GetDepth(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return -1;

			int depth = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
			return depth;
		}
		private SearchOption GetSearchOption()
		{
			return IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		}
		private Dictionary<string, string> GetRenamedEntries(Dictionary<int, HashSet<string>> dict, string oldPath, string newPath, bool includeSubEntries = true)
		{
			Dictionary<string, string> dirs = new Dictionary<string, string>();
			string newEntryParentRelativePath = GetRelativePath(ObservedPath, newPath);
			int oldEntryDepth = GetDepth(oldPath);
			var entries = dict.Keys.Where(key => key > oldEntryDepth).OrderBy(key => key).SelectMany(key => dict[key]);
			if (includeSubEntries)
			{
				foreach (var entry in entries)
				{
					if (entry.StartsWith(oldPath))
					{
						string entryRelativePath = GetRelativePath(oldPath, entry);
						string newEntryPath = Path.Combine(ObservedPath, newEntryParentRelativePath);
						string newEntryFullPath = Path.Combine(newEntryPath, entryRelativePath);
						dirs.Add(entry, newEntryFullPath);
					}
				}
			}
			else
			{
				foreach (var entry in entries)
				{
					int entryDepth = GetDepth(entry);
					if (entry.StartsWith(oldPath) && entryDepth == oldEntryDepth + 1)
					{
						string entryRelativePath = GetRelativePath(oldPath, entry);
						string newEntryPath = Path.Combine(ObservedPath, newEntryParentRelativePath);
						string newEntryFullPath = Path.Combine(newEntryPath, entryRelativePath);
						dirs.Add(entry, newEntryFullPath);
					}
				}
			}
			return dirs;
		}
		private IEnumerable<string> GetDeletedEntries(Dictionary<int, HashSet<string>> dict, string path, bool includeSubEntries = true)
		{
			int pathDepth = GetDepth(path);
			var entries = dict.Keys.Where(key => key > pathDepth).OrderBy(key => key).SelectMany(key => dict[key]);
			if (includeSubEntries)
			{
				foreach (var entry in entries)
				{
					if (entry.StartsWith(path))
					{
						yield return entry;
					}
				}
			}
			else
			{
				foreach (var entry in entries)
				{
					int entryDepth = GetDepth(entry);
					if (entry.StartsWith(path) && entryDepth == pathDepth + 1)
					{
						yield return entry;
					}
				}
			}
		}
		/** Copyright (c) 2014, Yves Goergen, http://unclassified.software/source/getrelativepath
		 * Copying and distribution of this file, with or without modification, are permitted provided the
		 * copyright notice and this notice are preserved. This file is offered as-is, without any warranty.**/
		/// <summary>
		/// Determines the relative path of the specified path relative to a base path.
		/// </summary>
		/// <param name="path">The path to make relative.</param>
		/// <param name="basePath">The base path.</param>
		/// <param name="throwOnDifferentRoot">If true, an exception is thrown for different roots, otherwise the source path is returned unchanged.</param>
		/// <returns>The relative path.</returns>
		private string GetRelativePath(string basePath, string path, bool throwOnDifferentRoot = true)
		{
			// Use case-insensitive comparing of path names.
			// NOTE: This may be different on other systems.
			StringComparison sc = StringComparison.InvariantCultureIgnoreCase;

			// Are both paths rooted?
			if (!Path.IsPathRooted(path))
				throw new ArgumentException($"{nameof(path)} argument is not a rooted path.");
			if (!Path.IsPathRooted(basePath))
				throw new ArgumentException($"{nameof(basePath)} argument is not a rooted path.");

			// Do both paths share the same root?
			string pathRoot = Path.GetPathRoot(path);
			string baseRoot = Path.GetPathRoot(basePath);
			if (!string.Equals(pathRoot, baseRoot, sc))
			{
				if (throwOnDifferentRoot)
				{
					throw new InvalidOperationException("Both paths do not share the same root.");
				}
				else
				{
					return path;
				}
			}

			// Cut off the path roots
			path = path.Substring(pathRoot.Length);
			basePath = basePath.Substring(baseRoot.Length);

			// Cut off the common path parts
			string[] pathParts = path.Split(Path.DirectorySeparatorChar);
			string[] baseParts = basePath.Split(Path.DirectorySeparatorChar);
			int commonCount;
			for (
				commonCount = 0;
				commonCount < pathParts.Length &&
				commonCount < baseParts.Length &&
				string.Equals(pathParts[commonCount], baseParts[commonCount], sc);
				commonCount++)
			{
			}

			// Add .. for the way up from relBase
			string newPath = "";
			for (int i = commonCount; i < baseParts.Length; i++)
			{
				newPath += ".." + Path.DirectorySeparatorChar;
			}

			// Append the remaining part of the path
			for (int i = commonCount; i < pathParts.Length; i++)
			{
				newPath = Path.Combine(newPath, pathParts[i]);
			}

			return newPath;
		}
		#endregion
	}
}
