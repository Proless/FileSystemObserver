# FileSystemObserver
A wrapper around FileSystemWatcher with exteneded functionality.
- Supports monitoring multiple file extensions, note that it doesn't override the ```Filter```.
  Think of it as second layer of filtering on top of the ```Filter```, you could set the ```Filter```
  to "\*.*" and then provide file extensions through the constructor to monitor multiple file types.
- You can also provide a ``` Func<EntryType, string, bool> ``` through the constructor to decide if an event should be raised for
  the affected file system entry. EntryType determines the type of the affected file system entry (File or Directory). 

### Raises extra Events when the affected file system entry is a directory:
