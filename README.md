# FileSystemObserver
A wrapper around FileSystemWatcher with exteneded functionality.
- Supports monitoring multiple file extensions, note that it doesn't override the ```Filter```.
  Think of it as a second filtering layer, you could set the ```Filter```
  to "\*.*" and then provide file extensions through the constructor to monitor multiple file types.
- You can also provide a ``` Func<EntryType, string, bool> ``` through the constructor to decide if an event should be raised for
  the affected file system entry. the fully qualified Path of the affected file/directory will be passed in and the entryType,
  which determines the type of the affected file system entry (File or Directory).
  Same goes here this doesn't override the ```Filter```. Think of it as a second or a third filtering layer
  (in case you provided both this and file extensions).

### Raises extra Events when the affected file system entry is a directory:

- When a directory is deleted / created / renamed, a corresponding event will be rasied for all subdirectories/files.

Note that a file system entry name is considered by the FileSystemWatcher as being the relative path.

#### Example
Monitored Path: C:\Monitored
| Path  | Name |
| ------------- | ------------- |
| C:\Monitored\Subdirectory  | Subdirectory  |
| C:\Monitored\file.txt  | file.txt |
| C:\Monitored\Subdirectory\Subdirectory1  | Subdirectory\Subdirectory1  |
| C:\Monitored\Subdirectory\file.txt  | Subdirectory\file.txt  |
| C:\Monitored\Subdirectory\sub\file.txt  | Subdirectory\sub\file.txt  |
