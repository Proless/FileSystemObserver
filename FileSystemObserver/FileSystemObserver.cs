using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FSO
{
	public class FileSystemObserver : IDisposable, ISupportInitialize
	{
		// Fields
		private FileSystemWatcher _fileSystemWatcher;
		private string[] _fileExts;
		private bool checkCondition = false;
		private Predicate<string> _raiseEventsCondition;
		private Dictionary<int, HashSet<string>> _files;
		private Dictionary<int, HashSet<string>> _dirs;

		// Properties
		public bool RaiseEventsForSubdirectories { get; set; } = false;
		public string ObservedPath { get { return _fileSystemWatcher.Path; } set { _fileSystemWatcher.Path = value; GetFileSystemEntries(); } }
		public bool IncludeSubdirectories { get { return _fileSystemWatcher.IncludeSubdirectories; } set { _fileSystemWatcher.IncludeSubdirectories = value; GetFileSystemEntries(); } }
		public int InternalBufferSize { get { return _fileSystemWatcher.InternalBufferSize; } set { _fileSystemWatcher.InternalBufferSize = value; } }
		public string Filter { get { return _fileSystemWatcher.Filter; } set { _fileSystemWatcher.Filter = value; } }
		public bool EnableRaisingEvents { get { return _fileSystemWatcher.EnableRaisingEvents; } set { _fileSystemWatcher.EnableRaisingEvents = value; } }
		public NotifyFilters NotifyFilter { get { return _fileSystemWatcher.NotifyFilter; } set { _fileSystemWatcher.NotifyFilter = value; } }
		public ISynchronizeInvoke SynchronizingObject { get { return _fileSystemWatcher.SynchronizingObject; } set { _fileSystemWatcher.SynchronizingObject = value; } }
		public ISite Site { get { return _fileSystemWatcher.Site; } set { _fileSystemWatcher.Site = value; } }

		// Constructors
		public FileSystemObserver(Predicate<string> raiseEventsCondition = null, params string[] fileExts)
		{
			_fileSystemWatcher = new FileSystemWatcher();
			Initialize(raiseEventsCondition, fileExts);
		}
		public FileSystemObserver(string path, Predicate<string> raiseEventsCondition = null, params string[] fileExts)
		{
			_fileSystemWatcher = new FileSystemWatcher(path);
			Initialize(raiseEventsCondition, fileExts);
		}
		public FileSystemObserver(string path, string filter, Predicate<string> raiseEventsCondition = null, params string[] fileExts)
		{
			_fileSystemWatcher = new FileSystemWatcher(path, filter);
			Initialize(raiseEventsCondition, fileExts);
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
			RaiseChangedEventFor(e.FullPath);
		}
		private void OnRenamed(object sender, RenamedEventArgs e)
		{
			RaiseRenamedEventFor(e.OldFullPath, e.FullPath);
			if (IsFile(e.OldFullPath) || File.Exists(e.FullPath))
			{
				// Update file entries.
				RemoveEntry(_files, e.OldFullPath);
				AddEntry(_files, e.FullPath);
			}
			else if (IsDirectory(e.OldFullPath) || Directory.Exists(e.FullPath))
			{
				// Update file entries
				var renamedFiles = GetRenamedEntries(_files, e.OldFullPath, e.FullPath);
				RemoveEntries(_files, renamedFiles.Keys);
				AddEntries(_files, renamedFiles.Values);
				// Raise renamed events.
				RaiseRenamedEventsFor(renamedFiles);

				// Update directory entries.
				RemoveEntry(_dirs, e.OldFullPath);
				AddEntry(_dirs, e.FullPath);
				var renamedDirectories = GetRenamedEntries(_dirs, e.OldFullPath, e.FullPath);
				RemoveEntries(_dirs, renamedDirectories.Keys);
				AddEntries(_dirs, renamedDirectories.Values);
				if (RaiseEventsForSubdirectories)
				{
					// Raise renamed events.
					RaiseRenamedEventsFor(renamedDirectories);
				}
			}
		}
		private void OnCreated(object sender, FileSystemEventArgs e)
		{
			RaiseCreatedEventFor(e.FullPath);
			if (IsFile(e.FullPath))
			{
				// Update file entries.
				AddEntry(_files, e.FullPath);
			}
			else if (IsDirectory(e.FullPath))
			{
				// Update file entries.
				var createdFiles = EnumerateFiles(e.FullPath, GetSearchOption(), _fileExts);
				AddEntries(_files, createdFiles);
				// Raise created events.
				RaiseCreatedEventsFor(createdFiles);

				// Update directory entries.
				AddEntry(_dirs, e.FullPath);
				var createdDirs = Directory.EnumerateDirectories(e.FullPath, "*.*", GetSearchOption());
				AddEntries(_dirs, createdFiles);
				// Raise created events.
				if (RaiseEventsForSubdirectories)
				{
					RaiseCreatedEventsFor(createdDirs);
				}

			}
		}
		private void OnDeleted(object sender, FileSystemEventArgs e)
		{
			RaiseDeletedEventFor(e.FullPath);
			if (IsFile(e.FullPath))
			{
				// Update file entries.
				RemoveEntry(_files, e.FullPath);
			}
			else if (IsDirectory(e.FullPath))
			{
				// Update file entries.
				var deletedFiles = GetDeletedEntries(_files, e.FullPath).ToArray();
				RemoveEntries(_files, deletedFiles);
				RaiseDeletedEventsFor(deletedFiles);

				// Update directory entries.
				RemoveEntry(_dirs, e.FullPath);
				var deletedDirs = GetDeletedEntries(_dirs, e.FullPath).ToArray();
				RemoveEntries(_dirs, deletedDirs);
				if (RaiseEventsForSubdirectories)
				{
					RaiseDeletedEventsFor(deletedDirs);
				}
			}
		}

		// Initialize methods
		private void Initialize(Predicate<string> raiseEventsCondition, string[] fileExts)
		{
			if (raiseEventsCondition != null)
			{
				_raiseEventsCondition = raiseEventsCondition;
				checkCondition = true;
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

		/** Raise events methods **/

		// Renamed event
		private void RaiseRenamedEventsFor(Dictionary<string, string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseRenamedEventFor(entry.Key, entry.Value);
			}
		}
		private void RaiseRenamedEventFor(string oldPath, string newPath)
		{
			if (checkCondition && _raiseEventsCondition != null)
			{
				if (_raiseEventsCondition(oldPath))
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
		private void RaiseCreatedEventsFor(IEnumerable<string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseCreatedEventFor(entry);
			}
		}
		private void RaiseCreatedEventFor(string entry)
		{
			if (checkCondition && _raiseEventsCondition != null)
			{
				if (_raiseEventsCondition(entry))
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
		private void RaiseDeletedEventsFor(IEnumerable<string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseDeletedEventFor(entry);
			}
		}
		private void RaiseDeletedEventFor(string entry)
		{
			if (checkCondition && _raiseEventsCondition != null)
			{
				if (_raiseEventsCondition(entry))
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
		private void RaiseChangedEventsFor(IEnumerable<string> fileSystemEntries)
		{
			foreach (var entry in fileSystemEntries)
			{
				RaiseChangedEventFor(entry);
			}
		}
		private void RaiseChangedEventFor(string entry)
		{
			if (checkCondition && _raiseEventsCondition != null)
			{
				if (_raiseEventsCondition(entry))
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
		private string GetRelativePath(string basePath, string path)
		{
			if (string.IsNullOrEmpty(basePath)) throw new ArgumentNullException(nameof(basePath));
			if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

			string relativePath = path.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			return relativePath;
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
		private Dictionary<string, string> GetRenamedEntries(Dictionary<int, HashSet<string>> dict, string oldPath, string newPath)
		{
			Dictionary<string, string> dirs = new Dictionary<string, string>();
			string newEntryParentRelativePath = GetRelativePath(ObservedPath, newPath);
			int oldEntryDepth = GetDepth(oldPath);

			foreach (var entry in dict.Keys.Where(key => key > oldEntryDepth).SelectMany(key => dict[key]))
			{
				if (entry.StartsWith(oldPath))
				{
					// an alternative could be to simply use string.Replace(oldPath, newPath); performance ?
					string entryRelativePath = GetRelativePath(oldPath, entry);
					string newEntryPath = Path.Combine(ObservedPath, newEntryParentRelativePath);
					string newEntryFullPath = Path.Combine(newEntryPath, entryRelativePath);
					dirs.Add(entry, newEntryFullPath);
				}
			}
			return dirs;
		}
		private IEnumerable<string> GetDeletedEntries(Dictionary<int, HashSet<string>> dict, string fullPath)
		{
			int pathDepth = GetDepth(fullPath);
			foreach (var entry in dict.Keys.Where(key => key > pathDepth).SelectMany(key => dict[key]))
			{
				if (entry.StartsWith(fullPath))
				{
					yield return entry;
				}
			}
		}
		#endregion
	}
}
