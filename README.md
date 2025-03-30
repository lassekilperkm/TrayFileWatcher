# TrayFileWatcher

Combined project of DirectoryListener and SystemTrayTest

This project compiles into a system tray app which listens for file changes in a configurable directory. The app can be configured to automatically start on system boot from within the context menu. Auto start can be disabled from there aswell.

Notifications of the app only work when the notification settings in Windows are turned on.

The library which watches for file changes is included into the build from the "include" directory of the project, the code can be found [here](https://github.com/lassekilperkm/DirectoryListener).
