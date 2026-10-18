Washed Ashore - macOS test build
================================

This is an unsigned test build. It is not code signed or notarized by Apple,
so macOS will refuse to open it the first time. This is expected.

To open it
----------
1. Unzip the download and move WashedAshore.app anywhere you like
   (Applications or your Desktop both work).
2. Right-click (or Control-click) WashedAshore.app and choose Open.
3. In the dialog that appears, click Open again.
   macOS remembers this, so you only do it once.

If macOS says the app "is damaged" or offers only Move to Trash
----------------------------------------------------------------
macOS marked the download as quarantined. Remove the mark in Terminal:

    xattr -dr com.apple.quarantine /path/to/WashedAshore.app

(Type "xattr -dr com.apple.quarantine ", a space, then drag the app into the
Terminal window to fill in the path, and press Return.) Then open the app
normally.

Newer macOS versions
--------------------
If right-click > Open shows no Open button, go to System Settings >
Privacy & Security, scroll to the message about WashedAshore being blocked,
and click Open Anyway.

If something goes wrong
-----------------------
Send us the player log: ~/Library/Logs/Unity/Player.log
